# ADR 0007 – Canvas-Layout als eigene Tabelle, mit guard-freiem Layout-Command

- **Status:** Akzeptiert
- **Kontext-Issue:** #102 – Layout-Persistenz (Tabelle `DialogLayout`) + Knoten verschieben
- **Betroffen:** `src/Flirty/Domain/`, `src/Flirty/Persistence/`, `src/Flirty/Runtime/Admin/`,
  `src/Flirty.Migrations.*`, `src/Flirty.AspNetCore/`, `src/Flirty.Designer/`

## Kontext

Die Graph-Ansicht aus Stufe 1 von EPIC 11 (#101) ordnet den Dialog über ein deterministisches
Auto-Layout an (`GraphLayout`, „Sugiyama-Light"). Für eine *Ansicht* trägt das. Sobald der Canvas ein
*Editor* wird, trägt es nicht mehr: Die Anordnung ist die des Algorithmus, nicht die des Autors, und es
gibt keinen Weg, sie zu korrigieren. `Question` hat keine Koordinaten.

Zwei Randbedingungen prägen die Entscheidung:

- **Versionieren ist der einzige Weg, einen veröffentlichten Dialog weiterzuentwickeln** (ADR 0005).
  `CreateDialogVersionCommand` vergibt beim Klonen für *jede* Frage eine neue `Guid` und gibt die
  interne `questionIdMap` nicht nach außen. Was Positionen an Frage-Guids hängt, muss diesen Umbau also
  mitmachen – von innen.
- **`DialogEditGuard` sperrt 16 Stellen** – die 15 Graph-Commands und den Wechsel der Einstiegsfrage im
  `UpdateDialogCommand` (ADR 0005). Liefe das Schreiben von Koordinaten über eine davon,
  ließe sich ein veröffentlichter Dialog nicht einmal übersichtlich anordnen: Jedes Verschieben
  quittierte mit `409`. Gerade der produktive Dialog ist aber der, den man am häufigsten ansieht.

## Entscheidung

**Positionen liegen in einer eigenen Tabelle `DialogLayout`**, nicht an einer Graph-Entity:

| Spalte | Typ | Anmerkung |
|---|---|---|
| `Id` | `Guid` | PK |
| `DialogId` | `Guid` | FK auf `Dialog`, **Cascade** |
| `ElementKind` | `LayoutElementKind` (`int`) | zunächst nur `Question` |
| `ElementId` | `Guid` | FK-los, wie die Frage-Verweise in `LoopDefinition` |
| `X`, `Y` | `int` | Canvas-Koordinaten, nie negativ |

Eindeutig über (`DialogId`, `ElementKind`, `ElementId`). **Ohne Zeile greift das Auto-Layout** – das ist
zugleich die Rückkehr: `ResetDialogLayoutCommand` löscht die Zeilen, mehr braucht „Layout zurücksetzen"
nicht.

Geschrieben wird über `SetDialogLayoutCommand` – einen **Batch-Upsert**: Genannte Elemente werden
angelegt bzw. aktualisiert, nicht genannte bleiben stehen. Über HTTP ist das `PUT .../dialogs/{id}/layout`
(die Merge-Semantik steht in der XML-Doc und im WebAPI-Guide, weil `PUT` sonst als Voll-Ersatz zu lesen
wäre); `DELETE .../dialogs/{id}/layout` setzt zurück.

**Beide Layout-Commands laufen bewusst nicht unter `DialogEditGuard`.** Koordinaten berühren die
Session-Semantik nicht: Sessions pinnen `DialogId`/`DialogVersion` und folgen Guids, nicht Pixeln. Mit
einer eigenen Tabelle ist das keine Umgehung der Publish-Sperre aus ADR 0005, sondern deren **Grenze** –
der Command schreibt in etwas, das nicht Teil des Graphen ist. ADR 0005 gilt unverändert; seine 16
Aufrufstellen bleiben unangetastet.

Zwei Zweige sind dabei **Handarbeit** und je durch einen Test festgenagelt:
`CreateDialogVersionCommand` klont die Zeilen und schreibt `ElementId` über die `questionIdMap` um (eine
Zeile ohne abbildbares Element wird verworfen, nicht mitgeschleppt); `DeleteQuestionCommand` räumt
verweisende Zeilen ab, weil `ElementId` FK-los ist.

Im Designer greifen die Positionen an **einer** Stelle: ganz am Ende von `GraphLayout.Render`, wo die
Knotenboxen entstehen. Schichtung, Kantenform, Baryzentrum und Kanalvergabe bleiben am Auto-Layout
hängen. Ein Zug ändert damit nur die Position eines Knotens – nie die Zeichenform einer Kante und nie
die Anordnung der übrigen.

## Verworfene Alternativen

**Gar nicht speichern, jedes Mal neu anordnen.** Kein Schema-Change, kein Command, kein Klon-Zweig. Für
die lesende Ansicht war das die Entscheidung von #101 und richtig. Für einen Editor ist sie es nicht:
Jede Anordnung, die der Autor herstellt, wäre beim nächsten Aufruf weg – und ohne Verschieben bleibt der
Canvas eine hübschere Liste. Die Folgestufen (#103 Editieren, #104 Testlauf im Graphen) setzen eine
stabile Anordnung voraus.

**Designer-lokale JSON-Datei** neben `connection-profiles.json`. Ebenfalls ohne Schema-Change und in
einer Stunde gebaut. Zwei Gründe dagegen, der zweite ist der harte: Das Layout hinge am *Rechner* statt
am Dialog – zwei Autoren desselben Dialogs sähen verschiedene Bilder, und ein Wechsel des
Connection-Profils oder des Arbeitsplatzes verwürfe es. Und: **Es überlebt die Versionierung nicht.**
Weil der Klon jeder Frage eine neue `Guid` gibt und `CreateDialogVersionCommand` die Abbildung nicht
herausreicht, startete jede neue Version – nach ADR 0005 der einzige Weg, einen produktiven Dialog zu
ändern – mit verworfenem Layout. Genau in dem Moment, in dem der Autor weiterarbeiten will.

**`LayoutX`/`LayoutY` (`int?`) auf `Question`.** Die billigste tragfähige Variante: zwei nullable
Spalten, kein eigenes Aggregat, kein Command-Satz – und das Klonen wie das Aufräumen erledigt sich von
selbst, weil die Werte an der Frage hängen. Ausgeschieden aus drei Gründen. Erstens mischt es einen
reinen **Anzeigebelang** unter eine Graph-Entity, die sonst ausschließlich Ablauf beschreibt; die
Laufzeit läse Spalten, die sie nie braucht. Zweitens deckelt es die Erweiterbarkeit: Positionen für
etwas anderes als eine Frage – Kanten-Wegpunkte, Notizknoten, ein Viewport – gäbe es dann nicht ohne
erneuten Griff an eine Graph-Entity. Drittens, und das gab den Ausschlag: Ein Schreibpfad, der die
Publish-Sperre überspringt, wäre bei dieser Variante eine **Konvention** – ein Feld einer gesperrten
Entity, das man am Guard vorbei schreibt, und jeder künftige `UpdateQuestion`-Pfad müsste die Ausnahme
kennen. Bei einer eigenen Tabelle ist dieselbe Freiheit **strukturell**: Es gibt schlicht nichts
Gesperrtes zu umgehen.

**Layout mit unter `DialogEditGuard` stellen** und dafür bei veröffentlichten Dialogen auf das
Verschieben verzichten. Konsequent gedacht – aber der produktive Dialog ist der, den man am häufigsten
öffnet, und ein Canvas, der ausgerechnet dort erstarrt, ist die Hälfte der Funktion. Der Ausweg „erst
eine neue Version ableiten, dann anordnen" erzeugte eine Dialogversion für eine Bildschirmaufteilung.

**`PUT` als Voll-Ersatz** (der Client schickt immer alle Positionen). REST-sauberer und ohne die
Erklärungsnot der Merge-Semantik. Ausgeschieden, weil eine Zieh-Geste ein Element verschiebt: Sie müsste
dann das gesamte Layout mitsenden, und zwei Autoren am selben Dialog überschrieben sich gegenseitig die
Positionen, die sie gar nicht angefasst haben.

## Konsequenzen

**Positiv**

- Knoten sind verschiebbar, und die Position überlebt Reload, Neustart **und** das Ableiten einer neuen
  Dialogversion. Ohne Zeile greift weiter das deterministische Auto-Layout.
- Verschieben funktioniert am veröffentlichten Dialog, ohne dass die Publish-Sperre aufweicht – belegt
  durch je einen Test auf Handler- und HTTP-Ebene, mit der Gegenprobe, dass eine echte Graph-Änderung an
  demselben Dialog weiterhin `409` liefert.
- Die Graph-Entities bleiben frei von Anzeigedaten; ein zweiter `LayoutElementKind` (Wegpunkte, Notizen,
  Viewport) kostet eine Enum-Zeile und keine Schema-Änderung an `Question`.
- Genau **eine** Server-Nachricht je Zieh-Geste: Das JS-Modul schreibt während des Zugs nur das
  `transform` des Knotens und ruft `MoveNodeAsync` erst beim Loslassen (ADR 0006).

**Negativ**

- Klonen und Aufräumen sind Handarbeit – zwei Zweige, die man beim nächsten Elementtyp vergisst. Sie
  hängen an je einem Test (`DialogLayoutTests`), aber ein Test schützt nur, was er kennt.
- Ein Aggregat, ein Command-Paar, zwei Endpunkte und eine Migration je Provider mehr als bei
  `LayoutX`/`LayoutY`.
- `ElementId` ist FK-los: Die Datenbank verhindert keine verwaiste Zeile. Der Aufräum-Zweig ist die
  Regel, das Übergehen unbekannter Elemente in `GraphLayout` der Gürtel.
- Während eines Zugs stimmen die Kanten kurz nicht. Sie werden gedimmt statt im Browser neu berechnet –
  ihre Geometrie entsteht in C# und ist dort getestet, eine zweite Quelle dafür wäre teurer als die
  Ungenauigkeit von einem Zug lang.

**Offen**

- **Kein Undo.** Ein Zug schreibt sofort; zurück geht es über einen zweiten Zug oder „Layout
  zurücksetzen" (das *alle* Positionen verwirft, nicht die letzte).
- **Keine Mehrfachauswahl.** Der Command kann einen Batch, die Oberfläche erzeugt heute stets einen
  Eintrag.
- **Kein Viewport, keine Wegpunkte.** Die Tabelle trägt beides, vorgebaut ist nichts – ein Viewport
  bräuchte eine Zeile ohne Element, Wegpunkte zusätzlich eine `Sequence`-Spalte.

Details: [DOMAIN-MODEL.md](../DOMAIN-MODEL.md), [PERSISTENCE.md](../PERSISTENCE.md),
[DESIGNER.md § Graph-Ansicht](../DESIGNER.md#graph-ansicht-101).
