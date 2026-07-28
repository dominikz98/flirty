# ADR 0008 – Editier-Gesten auf dem Canvas: bestehende Commands, Neuladen, Sperre je Geste

- **Status:** Akzeptiert
- **Kontext-Issue:** #103 – Editieren auf dem Canvas (Bausteine ziehen, verbinden, Loops, Trigger)
- **Betroffen:** `src/Flirty.Designer/` (**kein** Core-Code, **keine** Schema-Änderung)

## Kontext

Mit Stufe 3 von EPIC 11 wird der Graph-Canvas vom Betrachter zum Editor: Bausteine werden aus einer
Palette abgelegt, Knoten am Ausgangs-Port verbunden, Übergänge geordnet und gelöscht, Schleifen aus
einem Zyklus erzeugt, Trigger angelegt. Der Designer hatte bis dahin **einen** Schreibweg – die
Formulare – und die Graph-Ansicht war lesend.

Drei Randbedingungen prägen die Entscheidung:

- **Der Formularpfad bleibt vollständig erhalten** (Zusage von EPIC 11). Damit gibt es zwei Oberflächen
  für dieselben Daten, und die Frage ist nicht *ob* sie übereinstimmen, sondern *wodurch*.
- **Der Canvas schreibt aus einem `[JSInvokable]`.** Die Sperre des Formularpfads ist ausschließlich das
  gerenderte `disabled`-Attribut – ein Aufruf aus dem JS-Modul sieht davon nichts.
- **ADR 0007 sagt „der Commit lädt nicht neu"** und begründet das für die Verschiebe-Geste. Ob dieser
  Satz auch für Graph-Änderungen gilt, ist genau die Frage, die hier zu entscheiden ist.

## Entscheidung

**1. Jede Geste ruft die bestehenden Admin-Commands.** Der Canvas bekommt kein eigenes CRUD und keinen
eigenen Command im Core. Ein Palette-Drop ist `CreateQuestionCommand` + `SetDialogLayoutCommand`, eine
Verbindung ist `CreateTransitionCommand`, eine Umsortierung sind mehrere `UpdateTransitionCommand` – alles
in **einem** `FlirtyAdminGateway.ExecuteAsync`-Aufruf, also einem DI-Scope mit einem Fehlerpfad. Die
Rechenregeln, die vorher privat im `@code`-Block von `DialogEditor.razor` lagen (nächste `Order`, nächste
`Priority` je Ausgangsfrage, Positionsindex → `Priority`, Rücksprung-Erkennung), liegen jetzt in
`Services/GraphEditing.cs` bzw. `Services/LoopAnalyzer.cs` und werden von **beiden** Ansichten benutzt.

**2. Nach einer Graph-Mutation wird neu geladen** (`GetDialogQuery`), nicht lokal fortgeschrieben. Das
ist eine bewusste **Einschränkung** von ADR 0007: Dessen Satz gilt weiterhin, aber nur für den
Layout-Pfad, dessen Command das vollständige neue Layout zurückgibt. Die Graph-Commands liefern nur ihren
eigenen Ausschnitt; `DeleteQuestionCommand` räumt zusätzlich Übergänge, Schleifen-Marker, Trigger und
Layout-Zeilen ab. Entscheidend sind aber die **Warnungen**: `TransitionWarningAnalyzer` und `LoopAnalyzer`
rechnen über den *ganzen* `DialogDetail`. Ein neuer Übergang kann „Kein Default-Übergang" an einer
anderen Frage aufheben, ein gelöschter Unerreichbarkeit an mehreren Knoten erzeugen. Das
Akzeptanzkriterium „die Warnungen aktualisieren sich sofort" ist nur mit dem echten Serverstand wahr.

**3. Gesten sind nicht idempotent, also gibt es eine zweistufige Sperre.** Ein doppelter Drop legte zwei
Fragen an, eine doppelte Verbindungsgeste zwei Übergänge:

- **Clientseitig** läuft jede Nachricht über einen `send()`-Helfer im JS-Modul, der bis zur Rückkehr der
  .NET-Methode sperrt. **Das Versprechen von `invokeMethodAsync` ist die Quittung** – Blazor Server
  erfüllt es, wenn der Aufruf abgeschlossen ist. Ein zweiter Rückkanal wäre eine Stelle, die man
  vergessen kann.
- **Serverseitig** beginnt jede schreibende Operation mit einem Frühausstieg auf `_busy`
  (`RunGestureAsync`). Der Client-Riegel ist eine Bedienzusage und umgehbar; das Server-Gate ist die
  Invariante. Es allein zu nehmen verschluckte die zweite *berechtigte* Geste eines schnellen Anwenders
  stillschweigend.

**4. Der Lesemodus ist gerendert, nicht deaktiviert.** Bei veröffentlichtem Dialog entstehen Ports gar
nicht, und die Palette ist gesperrt. Das JS-Modul erfährt den Zustand über `data-editable` am `<svg>` –
ein Attribut, das **C# besitzt und das Modul nur liest**, bei jeder Geste frisch. Das ist die Kehrseite
der Regel aus ADR 0006 („was das JS setzt, rendert C# nie"), nicht ihr Bruch. Eine `attach`-Option wäre
eingefroren; `MoveNodeAsync` prüft `data-editable` bewusst nicht, weil Verschieben erlaubt bleibt
(ADR 0007).

**5. Geometrie einer laufenden Geste lebt in von C# gerenderten Platzhaltern.** Das Gummiband
(`.graph-rubber`) und die Drop-Vorschau (`.graph-ghost`) stehen als leere Elemente im Markup; das Modul
setzt und leert nur ihre Geometrie (`d` bzw. `x`/`y`/`width`/`height`). Per `createElement` erzeugtes DOM
in einem von Blazor verwalteten Container brächte den Renderer beim nächsten Diff über die Kindindizes
aus dem Tritt.

## Verworfene Alternativen

| Alternative | Warum nicht |
|---|---|
| **Lokale Fortschreibung des `DialogDetail`** statt Neuladen | Der Client müsste die Mit-Aufräumung von `DeleteQuestionCommand` nachbauen – wörtlich die „zweite Wahrheit", die das Issue verbietet. Schlimmer noch: Ein handgeflicktes `DialogDetail`, das eine Kaskade übersieht, erzeugt *falsche* Warnungen. Ein zweiter Roundtrip nach dem Schreiben ist der billigere Preis, und er fällt je Geste an, nicht je Zeigerschritt. |
| **Ein Aggregat-Command im Core** (`CreateQuestionWithLayoutCommand`), der Frage, Position und Übergang in einer Transaktion schreibt | Wäre transaktional saubererer, machte aber eine **Designer-Sonderoperation zur Engine-API**. Die Engine kennt kein Canvas; `DialogLayout` ist bewusst kein Teil des Graphen (ADR 0007). Der Preis der Ablehnung ist bekannt und wird getragen: Scheitert die zweite Nachricht, existiert die Frage ohne Position und das Auto-Layout ordnet sie ein – degradiert, aber konsistent. Bewusst **keine** Kompensation durch Löschen: Eine gerade angelegte Frage wegen eines Layout-Schluckaufs zu entfernen wäre der teurere Fehler. |
| **HTML5-Drag-and-Drop für die Palette** | Die Palette ist HTML außerhalb des SVG, dort wäre DnD das native Idiom. Aber: Es wäre ein **zweites Ereignismodell** mit einem zweiten Ort für die Sperre, das Drop-Ereignis liefert die Position nicht in Nutzerkoordinaten (`getScreenCTM().inverse()` müsste man nachbauen), und das Ghost gehört dem Browser statt der Zeichenfläche. Ein Modell je Geste, ein Riegel. |
| **Undo/Redo-Stack** | Bräuchte inverse Commands für jede Operation – und für `DeleteQuestionCommand` mit seiner Kaskade gibt es keinen. Bleibt bewusst außen vor (so bereits in #99 festgehalten); dafür ist Löschen zweistufig bestätigt. |
| **Natives Kontextmenü am Knoten** (`contextmenu`-Ereignis), wie es der Issue-Text nahelegt | Bräuchte JS-Positionierung, Fokusfalle und Escape-Behandlung, wäre nicht tastaturbedienbar und von Playwright nur schwer treibbar – und der Designer verbietet blockierende Browser-Dialoge ohnehin (Begründung an `.confirm` in `app.css`). Trigger entstehen deshalb über einen Abschnitt im Inspector. Der Zweck des Kriteriums – „Trigger am Knoten anlegbar" – ist erfüllt. |
| **Die vier `@page`-Editoren in den Inspector einbetten** | Sie haben eigenen `PageTitle`, eigene Überschrift und eigenen Rücklink; sie einzubetten hieße, sie umzubauen. Stattdessen bekommt der Inspector **eigene Panels**, die dieselben Commands rufen. Die Grenze verläuft entlang der Datenform: skalare Felder im Panel, eigene Unterstruktur (Antwortoptionen, Validierungsregeln, Roh-JSON) im Vollteditor. |
| **`disabled` an Port und Palette-Eintrag während einer Geste** | Blazor rendert das Attribut dann mitten in einem laufenden Zug neu, und die Pointer-Capture geht verloren. Gesperrt wird über `data-busy` am `<svg>` und `pointer-events` – das fasst kein Attribut am Knoten an. |

## Konsequenzen

**Gut:**

- Es gibt **einen** Schreibweg in die Engine. Was auf dem Canvas passiert, steht unmittelbar in der
  Listenansicht – nicht weil beide synchronisiert werden, sondern weil sie dasselbe tun.
- Die Rechenregeln sind erstmals **testbar**: Der Designer hat kein bUnit, aber `GraphEditing` und
  `LoopAnalyzer.UnmarkedBackJumps` sind reine Funktionen über `DialogDetail`. Vorher lagen sie in einem
  `@code`-Block und waren durch keinen Test gedeckt.
- Der Ausdrucks-Editor existiert als Komponente (`ExpressionField`) statt in drei Kopien. Nebenbefund
  der Zusammenlegung: `.expr-status`/`.expr-caret` lagen scoped im `TransitionEditor` – im
  `TriggerEditor` war der Live-Status seit #42 **unstyled**.

**Kosten:**

- **Ein zusätzlicher `GetDialogQuery` je mutierender Geste.** Das Busy-Fenster umfasst damit Schreiben
  *und* Lesen und ist auf einem langsamen Circuit sichtbar länger. Das ist die gewollte Ehrlichkeit
  gegenüber einem Rennen; sichtbar gemacht über `data-busy` und `cursor: progress`.
- **Zwei Sperren müssen zusammenpassen.** Wer eine neue Geste ergänzt, muss beide Enden bedienen: den
  `send()`-Helfer im Modul und `RunGestureAsync` in der Seite. Ein direkter `invokeMethodAsync`-Aufruf
  neben `send()` unterläuft den Riegel.
- **Ein bekanntes, begrenztes Fenster bleibt:** Das Versprechen löst auf, bevor der Render-Batch
  angewandt ist. Ein Klick in diesem Sub-Frame-Fenster arbeitet auf altem DOM – vom Server-Gate
  abgefangen, hier dokumentiert statt wegkonstruiert.
- **Die Panels des Inspectors sind eine dritte Formularstelle.** Sie sind bewusst schmal gehalten (nur
  skalare Felder), aber die Grenze muss bei jeder Erweiterung neu gezogen werden.

**Nicht entschieden** (bleibt offen): Undo/Redo, Mehrfachauswahl, ein Kanten-Routing mit Wegpunkten,
Trigger an dauerhaft eingeblendeten Scope-Markern (die entstehen weiterhin nur, *wenn* sie Trigger
tragen – eine Zusage aus #101, festgenagelt durch einen Test auf `MinY == 0`).
