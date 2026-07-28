# ADR 0006 – Canvas-Technik im Designer: Eigenbau-SVG statt Diagramm-Bibliothek

- **Status:** Akzeptiert
- **Kontext-Issue:** #100 – Spike: Canvas-Technik für den visuellen Dialog-Designer
  (Eigenbau-SVG vs. Blazor.Diagrams); Rahmen: #99 – EPIC 11 Visueller Graph-Designer (Canvas)
- **Betroffen:** `src/Flirty.Designer` (EPIC 11, Stufen 1–5), `Directory.Packages.props`

## Kontext

EPIC 11 gibt dem Designer eine **Canvas-Ansicht**: Fragen als Knoten, Übergänge als Kanten, Schleifen
als Bereich. Bis dahin ist die Oberfläche ein Stapel Formulare – vollständig, aber der Ablauf eines
Dialogs ist daraus nicht ablesbar.

Die harte Randbedingung ist die Interaktivitätsvariante: `Flirty.Designer` ist eine Blazor Web App mit
`AddInteractiveServerComponents()` (`src/Flirty.Designer/DesignerApp.cs`), also **Blazor Server**. Jedes
Blazor-Ereignis ist damit ein SignalR-Roundtrip. Läuft das Ziehen eines Knotens in C#, kostet **jeder
Pointer-Move** eine Netzwerkumlaufzeit – bei zweistelligen Knotenzahlen und einem WAN-Circuit ist die
Seite unbenutzbar. Diese Entscheidung fällt also **vor** der ersten Zeile Canvas-Code und ist später
teuer zu revidieren: Sie bestimmt, ob Knoteninhalte Razor-Komponenten oder Bibliotheks-Widgets sind, wo
Pan/Zoom/Selektion herkommen und wie die E2E-Tests greifen.

Zur Wahl standen ein **Eigenbau** (SVG in Razor + ein collocated JS-Modul) und
**[Blazor.Diagrams](https://github.com/Blazor-Diagrams/Blazor.Diagrams)** (`Z.Blazor.Diagrams`, MIT) –
der einzige ernsthafte freie Kandidat. Er wirbt ausdrücklich mit „95 % C#/Blazor, JS nur wo nötig" und
mit Server-Tauglichkeit. Genau das war zu **messen**, nicht zu glauben: Seine Dokumentations- und
Demo-Site läuft auf **WebAssembly** (Boot-Manifest geprüft), also mit In-Process-Pointer-Events ohne
Netzwerk. Eine flüssige Demo dort sagt über einen Server-Circuit nichts.

## Entscheidung

Der Canvas wird **selbst gebaut**: SVG in Razor-Komponenten, Pointer-Interaktion in einem collocated
ES-Modul (`*.razor.js`, Muster wie `Components/Layout/ReconnectModal.razor.js`). `Z.Blazor.Diagrams`
wird nicht aufgenommen.

Unabhängig davon gilt als Architekturzusage für jede Canvas-Geste:

- **Ziehen läuft vollständig clientseitig** (SVG-`transform`, inzidente Kanten im JS mitgezeichnet).
  Zwischen `pointerdown` und `pointerup` geht **keine** Nachricht an den Server.
- **Erst das Loslassen ruft einen Command** – genau eine Nachricht je Geste.
- Der Canvas setzt ein **explizites Bereitschaftssignal** (`data-canvas-ready`), sobald das JS-Modul
  gebunden ist. Das Wiederholmuster `InteractWhenReadyAsync` aus `tests/Flirty.E2E` trägt hier nicht:
  Es setzt idempotente Aktionen voraus, ein wiederholter Drag verschöbe doppelt.

Gemessen wurde mit zwei Wegwerf-Prototypen über **demselben** Graphen (30 Knoten, 45 Kanten, Zyklus
17→9, eine Frage mit vier ausgehenden Kanten) gegen einen künstlich gedrosselten Circuit. Der Code liegt
auf dem Branch `spike/dz/100` und ist bewusst **nicht gemergt** – er ist der Herkunftsnachweis der
Zahlen, kein Produktcode.

**Ergebnis** (Median über 7 Gesten je Kandidat, je 300 px in 30 Schritten à 16 ms; frischer Circuit je
Geste, Kandidaten abwechselnd, eine ungemessene Aufwärm-Geste je Seite):

| Kandidat | Rückstand hinter dem Zeiger | Stillstand nach dem Loslassen | Nachrichten ↑/↓ je Geste | Nutzlast je Geste |
|---|---:|---:|---:|---:|
| **Eigenbau-SVG** | **0 px** (0–0) | **0,3 ms** (0,1–0,4) | **2 / 2** | **688 B** |
| Blazor.Diagrams 3.0.4.1 | 40 px ≈ **64 ms** (40–50 px) | **168 ms** (166–231) | **68 / 68** | **50 309 B** |

Randbedingungen der Messung: gemessene Circuit-Umlaufzeit **163 ms** (Eigenbau) bzw. **163 ms**
(Bibliothek) – die Drosselung traf beide gleich; 31 tatsächlich im Browser ausgelöste `pointermove` in
beiden Fällen (Chromium fasst sie auf den Animationsframe zusammen, das begrenzt die Roundtrips der
Bibliothek nach oben und ist damit zu ihren Gunsten). Aufschlüsselung je Geste – Eigenbau:
`BeginInvokeDotNetFromJS ×1`, `JS.EndInvokeDotNet ×1`, `JS.RenderBatch ×1`, `OnRenderCompleted ×1`.
Bibliothek: `BeginInvokeDotNetFromJS ×37`, `JS.EndInvokeDotNet ×37`, `JS.RenderBatch ×31`,
`OnRenderCompleted ×31`.

Maschine: AMD Ryzen 7 5800X (8 Kerne), 64 GB, Windows 11 Enterprise 10.0.26100, .NET SDK 10.0.204,
Playwright 1.61.0 / Chromium Headless Shell, Loopback plus TCP-Verzögerungsproxy 2 × 75 ms.

## Verworfene Alternativen

- **Blazor.Diagrams (`Z.Blazor.Diagrams` 3.0.4.1).** Verworfen wegen seines **Drag-Pfads**, nicht wegen
  Lizenz, Zielframework oder Paket-Hygiene – die sind einwandfrei: MIT, natives `lib/net10.0`, eine
  einzige fremde transitive Abhängigkeit (`SvgPathProperties 1.1.2`) ohne Advisory, und der Designer
  baut mit der Referenz unter `TreatWarningsAsErrors=true` mit **0 Warnungen** (nachgemessen). Da
  `Flirty.Designer` `IsPackable=false` ist, hätte eine Abhängigkeit dort ohnehin kein NuGet-Paket
  belastet. Der Ausschlussgrund liegt in der Architektur: `Components/DiagramCanvas.razor` verdrahtet
  `@onpointermove="OnPointerMove"` als **C#-Handler**, und das mitgelieferte `wwwroot/script.js` (48
  Zeilen, selbst gelesen) enthält ausschließlich `getBoundingClientRect`, `ResizeObserver`,
  `MutationObserver` und einen `scroll`-Horcher – **keinen einzigen Pointer- oder Drag-Handler**. Es gibt
  also keinen clientseitigen Drag-Pfad, den man einschalten könnte; Throttling oder Coalescing findet
  nirgends statt. Die Folge stand oben in der Tabelle: **34-mal so viele Nachrichten und 73-mal so viel
  Nutzlast je Geste**, ein sichtbarer Rückstand von **rund 40 px (≈ 64 ms)** hinter dem Zeiger, und der
  Knoten steht erst eine Umlaufzeit nach dem Loslassen still. Das ist kein Fehler der Bibliothek – für
  WebAssembly, wofür sie ausdrücklich optimiert ist, ist der Ansatz richtig. Für einen Server-Circuit
  ist er es nicht.
- **Blazor.Diagrams mit clientseitigem Drag nachrüsten (Fork oder Beitrag).** Naheliegend, weil der
  Rest der Bibliothek (Router, Pfadgeneratoren, Gruppen, Virtualisierung) genau das ist, was wir sonst
  selbst bauen. Ausgeschieden an der Reichweite des Eingriffs und am Pflegestand: Der Drag hängt an der
  Behavior-Kette (`SelectionBehavior` → `DragMovablesBehavior`), die durchgängig auf serverseitigen
  Modellen arbeitet – ein clientseitiger Pfad wäre kein Patch, sondern eine zweite Wahrheit über
  Knotenpositionen. Dazu: 108 offene Issues, faktisch ein Maintainer, letzter Commit 2026-03-02, und
  die vom Maintainer **selbst** 2022 eröffnete Performance-Review (#217, „so many JS calls that can most
  probably be batched") ist unangetastet. Was dort heute nicht funktioniert, funktioniert absehbar auch
  morgen nicht.
- **Syncfusion / MindFusion Diagram.** Fachlich am weitesten von allen Kandidaten – und beide
  **kommerziell lizenziert**. Flirty ist ein MIT-Repo, das zwei NuGet-Pakete veröffentlicht; eine
  Lizenzpflicht im Designer wäre eine Hürde für jeden, der den Designer starten will, und wäre auch
  durch `IsPackable=false` nicht entschärft. Nicht Teil des Spikes, aus Prinzip ausgeschieden.
- **Eine JS-Diagramm-Bibliothek einbinden** (jsPlumb, Cytoscape.js, React Flow o. ä.) und per Interop
  fahren. Das Drag-Problem wäre damit gelöst – die Kosten liegen woanders: Das Repo hat bewusst **keine**
  Node-Toolchain (kein `package.json`, kein Bundler), Knoteninhalte wären dann JS-Templates statt
  Razor-Komponenten, und der Graph-Zustand lebte doppelt. Eingebundene Fertig-Bundles ohne Build-Kette
  wären zudem an `MapStaticAssets`/CSP vorbei zu pflegen.
- **Nur eine Leseansicht ohne Ziehen** (Auto-Layout, keine Interaktion). Hätte die Entscheidung
  vertagt, nicht beantwortet: Für eine reine Ansicht ist jede Technik geeignet, und die Wahl fiele beim
  ersten Editier-Feature erneut an – dann mit fertiger Investition in die falsche Richtung.

## Konsequenzen

**Positiv**

- Ziehen kostet den Circuit **eine** Nachricht statt einer je Frame, und der Knoten folgt dem Zeiger
  innerhalb eines Frames – unabhängig von der Umlaufzeit. Das ist die Eigenschaft, an der EPIC 11
  hängt.
- **Keine neue Abhängigkeit.** Der `Directory.Packages.props`-Eintrag aus dem Spike entfällt; die
  Paketliste bleibt, wie sie ist.
- Knoteninhalte (Typ-Badge, Pflicht-Marker, Warnmarker, Trigger-Chips) sind **Razor-Komponenten** und
  teilen Klassen sowie Kontrastregeln mit dem restlichen Designer (`wwwroot/app.css`). Das ist nach den
  Kontrast-Befunden aus #95 kein Nebenaspekt.
- Barrierefreiheit bleibt in eigener Hand: Knoten sind fokussierbare SVG-Elemente, keine
  Bibliotheks-Widgets mit fremdem Fokusmodell.

**Negativ**

- **Pan/Zoom, Kantenrouting, Selektion und Snapping sind Eigenbau.** Grobe Schätzung auf Basis der
  Prototypen: Pan/Zoom im JS-Modul rund 100 Zeilen, gerade Kanten mit Ausweichen an Knotenrändern rund
  150, Mehrfachselektion und Snapping je rund 50. Dazu ein deterministisches Auto-Layout
  (Sugiyama-Light, geschätzt ~150 Zeilen) – nötig, weil ohne gespeicherte Position etwas Sinnvolles
  entstehen muss und weil E2E-Selektoren stabile Koordinaten brauchen.
- **Zwei Sprachen je Geste.** Die Wahrheit über Knotenpositionen liegt beim Loslassen im Server, dazwischen
  im DOM. Wer das JS-Modul ändert, muss den C#-Zustand mitziehen – sonst setzt der nächste Render den
  Knoten zurück. Der Prototyp zeigt die Stelle: `OnDragEnd` schreibt die Position fort, sonst gewinnt
  der alte Wert.
- Was der Spike **beiläufig** gelernt hat, bleibt Eigenbau-Pflicht: Kanten dürfen den Knoten nicht
  verdecken. In Blazor.Diagrams liegen die Kanten in derselben SVG-Ebene *nach* den Knoten und tragen
  einen 12 px breiten unsichtbaren Treffer-Pfad – ein `pointerdown` auf die Knotenmitte traf dort eine
  Kante statt des Knotens. Der Eigenbau zeichnet Kanten **vor** den Knoten; das ist eine Zusage, keine
  Zufälligkeit.

**Offen**

- Die **Layout-Persistenz** ist hier ausdrücklich nicht entschieden. Sie ist Gegenstand von Stufe 2
  (#102) und bekommt ihren eigenen ADR – inklusive der Begründung, warum ein Layout-Command bewusst
  nicht unter `DialogEditGuard` fällt.
- Ob der Canvas jenseits von rund 50 Knoten noch trägt, ist ungemessen. Die Messlatte lag bei 30; Ziel
  sind Dialoge in der Größenordnung realer Fragebögen, keine Hunderte-Knoten-Graphen.
- Sollte Blazor.Diagrams je einen clientseitigen Drag-Pfad erhalten, ist diese Entscheidung neu zu
  prüfen – dann per **neuem** ADR, nicht durch Umschreiben dieses.

Details: [DESIGNER.md](../DESIGNER.md).
