---
name: flirty-designer
description: Den Blazor-Designer (Flirty.Designer) aufbauen oder erweitern – Dialog-/Frage-/Antwort-/Branching-/Loop-/Trigger-Konfiguration, Graph-Canvas, Multi-DB-Connection-Profile. Verwenden bei "Designer", "Blazor-UI für Dialoge", "Dialog-Editor", "Branching-Editor", "Graph-Ansicht", "Canvas", "Connection-Profil", "Designer-E2E", "EPIC 7", "EPIC 11", Issues #37–#43, #46 und #99–#105.
---

# Blazor-Designer aufbauen / erweitern

> **Status: EPIC 7 (Issues #37–#43) vollständig umgesetzt** – Connection-Profil-Verwaltung (#37),
> Dialog-CRUD (#38), Frage-Editor (#39), Branching-Editor (#40), Loop-Editor (#41), Trigger-Editor (#42)
> und Test-Runner (#43); `docs/DESIGNER.md` beschreibt alle sieben. Die UI ist seit **#46** per
> Playwright-E2E abgedeckt. **EPIC 11** (visueller Graph-Designer, #99) ist ebenfalls **vollständig
> umgesetzt**: Technik-Spike (#100 → ADR 0006), **Graph-Ansicht (#101)**, **Layout-Persistenz
> (#102 → ADR 0007)**, **Editieren auf dem Canvas (#103 → ADR 0008)**, **Testlauf im Graphen (#104)** und
> die **Canvas-E2E (#105)**. Dieser Skill ist die
> **Leitplanke** für Erweiterungen: die beabsichtigte Architektur und die Konventionen, an die man sich
> beim Implementieren halten soll.
> Referenz: `docs/DESIGNER.md`, `docs/ARCHITECTURE.md` §4/§8/§10, `docs/BACKLOG.md` EPIC 7/11,
> `docs/adr/0006-canvas-technik-im-designer.md`, `docs/adr/0008-gesten-auf-dem-canvas.md`.

## Ist-Zustand (verifiziert)

- `src/Flirty.Designer/Flirty.Designer.csproj`: `Microsoft.NET.Sdk.Web`, referenziert `..\Flirty` **und
  alle drei** `..\Flirty.Migrations.{Sqlite,PostgreSql,SqlServer}` (für Multi-DB-Migrate), plus
  `InternalsVisibleTo("Flirty.Tests")` und `InternalsVisibleTo("Flirty.E2E")`;
  `BlazorDisableThrowNavigationException=true`.
- `DesignerApp.cs`: die **gesamte** Komposition (`ConfigureServices(WebApplicationBuilder)` +
  `Configure(WebApplication)`), `Program.cs` ruft seit #46 nur noch beides auf – so hostet die E2E
  denselben Aufbau in-Prozess (Muster wie `WebSampleApp`). Neue Dienste/Middleware gehören dorthin,
  nicht in `Program.cs`. Inhalt: `AddRazorComponents().AddInteractiveServerComponents()` +
  `MapRazorComponents<App>().AddInteractiveServerRenderMode()` → **Blazor Web App, Server-interaktiv**,
  seit #37 **`AddFlirty()` (parameterlos)**; der `FlirtyDbContext` wird pro aktivem
  Connection-Profil über `FlirtyDesignerDbContextFactory : IDbContextFactory<FlirtyDbContext>` erzeugt.
- **Connection-Profile (#37):** `Models/ConnectionProfile.cs`, `Services/IConnectionProfileStore` +
  `JsonConnectionProfileStore` (JSON im ContentRoot, gitignored), `ActiveConnectionProfile` (Scoped,
  mit `Activate`/`Adopt`), `ConnectionProfileOperations` (Test-Connection/Migrate),
  `ConnectionProfileContextBuilder`; UI unter `Components/Pages/ConnectionProfiles.razor`
  (`/connections`) + `Components/Layout/NavMenu.razor`.
- **Dialog-CRUD (#38):** `Services/FlirtyAdminGateway.cs` (+ `AdminResult<T>`),
  `Models/DialogFormModel.cs`, Seiten `Components/Pages/Dialogs.razor` (`/dialogs`) und
  `Components/Pages/DialogEditor.razor` (`/dialogs/{id:guid}`). Gemeinsame UI-Klassen (`.editor`,
  `.field`, `.input`, `.btn`, `.data-table`, `.badge`, `.msg`, `.banner`, `.empty`, `.back`, `.confirm`,
  `h1 .badge`) liegen **global** in `wwwroot/app.css`; `*.razor.css` enthält nur Seitenspezifisches.
- **Frage-Editor (#39):** `Models/QuestionFormModel.cs` (Metadaten + Regel-JSON ⇄ Eingabefelder, mit
  Roh-JSON-Fallback), `Models/AnswerOptionFormModel.cs`, `Models/QuestionTypeLabels.cs` (deutsche
  Typnamen, `UsesOptions`), Seite `Components/Pages/QuestionEditor.razor`
  (`/dialogs/{dialogId:guid}/questions/{questionId:guid}`) und der Abschnitt „Fragen" in
  `DialogEditor.razor` (Liste, Inline-Anlegen, ↑/↓, Löschen).
- **Branching-Editor (#40):** `Models/TransitionFormModel.cs`, `Models/ExpressionVariable.cs`,
  `Services/DesignerExpressionContext.cs` (Musterkontext + Bezeichner-Referenz + Baustein-Einfüger),
  Seite `Components/Pages/TransitionEditor.razor`
  (`/dialogs/{dialogId:guid}/transitions/{transitionId:guid}`) und der Abschnitt „Übergänge (Branching)"
  in `DialogEditor.razor` (je Ausgangsfrage gruppiert, ↑/↓, Warnungen, Inline-Anlegen). Dafür liefert der
  Core `DialogDetail.Loops` (`LoopDetail`) mit.
- **Loop-Editor (#41):** `Models/LoopFormModel.cs`, `Models/LoopInsight.cs`, `Services/LoopAnalyzer.cs`
  (Bereichsermittlung + Warnungen), Seite `Components/Pages/LoopEditor.razor`
  (`/dialogs/{dialogId:guid}/loops/{loopId:guid}`) und der Abschnitt „Schleifen (Loops)" in
  `DialogEditor.razor` (Liste, Inline-Anlegen, Vorschläge aus unmarkierten Rücksprüngen). Dafür kam im
  Core das **Loop-CRUD** dazu (`Create/Update/DeleteLoopCommand`, `IDialogAdminStore.GetLoopAsync` /
  `LoopCollectionKeyExistsAsync` / `GetLoopsReferencingQuestionAsync`) sowie in `Flirty.AspNetCore`
  `Dtos/Admin/LoopDtos.cs`, die `.../loops`-Endpunkte und `Loops` in `DialogDetailResponse`.
- **Trigger-Editor (#42):** `Models/TriggerFormModel.cs`, `Models/TriggerLabels.cs`, Seite
  `Components/Pages/TriggerEditor.razor` (`/dialogs/{dialogId:guid}/triggers/{triggerId:guid}`) und der
  Abschnitt „Trigger" in `DialogEditor.razor` (Liste, Inline-Anlegen; **keine** Sortierung – die Entity
  hat kein `Order`/`Priority`). Dafür kam im Core dazu: `TriggerConfig` (öffentliches Schema der
  `Config`-Spalte), `Create/Update/DeleteTriggerCommand`, `IDialogAdminStore.GetTriggerAsync` /
  `GetTriggersReferencingQuestionAsync`, `Triggers` in `DialogDetail`, in `Flirty.AspNetCore`
  `Dtos/Admin/TriggerDtos.cs` + `.../triggers`-Endpunkte – **und die Laufzeit-Auslieferung** im
  `WebhookNotificationHandler` (`IDialogStore.GetTriggersForSessionAsync`).
- **Test-Runner (#43):** Core-Command `StartDialogVersionCommand` + `IFlirtyEngine.StartDialogVersionAsync`
  (`src/Flirty/Runtime/`), im Designer `Services/DesignerGateway.cs` (gemeinsame Basis, `GatewayResult<T>`),
  `Services/FlirtyRuntimeGateway.cs`, `Services/AnswerValueCodec.cs`, `Services/RunExpressionContext.cs`,
  `Services/DesignerTriggerLog.cs` + `DesignerTriggerLogHandlers.cs`, `Models/AnswerInputModel.cs` +
  `Models/AnswerChoice.cs`, `Components/AnswerInput.razor` und die Seite
  `Components/Pages/DialogTestRunner.razor` (`/dialogs/{dialogId}/test`), verlinkt aus dem `DialogEditor`.
- **Graph-Ansicht (#101):** die lesende Seite `Components/Pages/DialogGraph.razor`
  (`/dialogs/{id}/graph`) samt collocated `DialogGraph.razor.js` (Pan/Zoom – das **erste JSInterop** im
  Designer), die Komponenten `Components/GraphNodeCard.razor` und `Components/GraphInspector.razor`,
  die Services `Services/GraphLayout.cs` (Sugiyama-Light), `Services/DialogGraphBuilder.cs` und
  `Services/TransitionWarningAnalyzer.cs` (aus `DialogEditor.razor` herausgezogen) sowie die Modelle
  `Models/GraphWarning.cs`, `DialogGraphModel.cs`, `GraphLayoutResult.cs`, `GraphMetrics.cs`,
  `SvgFormat.cs`. **Kein Core-Code** – Datenquelle bleibt `GetDialogQuery`. Verlinkt aus `Dialogs.razor`
  und dem Kopf des `DialogEditor`.
- **Layout-Persistenz (#102):** die einzige Stufe von EPIC 11 mit **Schema-Änderung**. Im Core neu:
  `Domain/DialogLayout.cs` + `Domain/LayoutElementKind.cs`, `Dialog.Layout`,
  `Persistence/Configurations/DialogLayoutConfiguration.cs` (Unique-Index über
  `(DialogId, ElementKind, ElementId)`), `IDialogAdminStore.GetLayoutAsync` /
  `GetLayoutsReferencingElementAsync`, `Runtime/Admin/SetDialogLayoutCommand.cs` (Batch-Upsert) und
  `ResetDialogLayoutCommand.cs` – **beide ohne `DialogEditGuard`** (ADR 0007) –, `DialogLayoutDetail` /
  `DialogLayoutEntry` + `DialogDetail.Layout`, je eine Migration `AddDialogLayout` in **allen drei**
  Migrations-Projekten, in `Flirty.AspNetCore` `Dtos/Admin/DialogLayoutDtos.cs`,
  `PUT`/`DELETE .../dialogs/{id}/layout` und `Layout` in `DialogDetailResponse`. Dazu die zwei
  Handarbeits-Zweige: `CreateDialogVersionCommand` klont die Zeilen über die `questionIdMap`,
  `DeleteQuestionCommand` räumt verweisende ab. Im Designer: gespeicherte Positionen in
  `GraphLayout.Render`, `IsPinned` an `GraphNodePosition`/`GraphNode`, Knoten-Drag im JS-Modul plus
  `[JSInvokable] MoveNodeAsync` und „Layout zurücksetzen" in `DialogGraph.razor`.
- **Editieren auf dem Canvas (#103):** **kein Core-Code, keine Schema-Änderung** – die Gesten rufen die
  vorhandenen Admin-Commands (ADR 0008). Neu im Designer: `Components/GraphPalette.razor`,
  `Components/ExpressionField.razor` (der aus `TransitionEditor`/`TriggerEditor` herausgezogene
  Ausdrucks-Editor, von beiden jetzt benutzt), `Components/GraphQuestionPanel.razor` und
  `GraphTransitionPanel.razor` (Editier-Zweige des Inspectors), `Services/GraphEditing.cs` (`NextOrder`,
  `NextPriority`, `Reorder`), `Models/GraphEdits.cs` (public Nutzlasten der Panels),
  `QuestionFormModel.SuggestKey`, `LoopAnalyzer.IsBackJump`/`UnmarkedBackJumps` (aus dem `DialogEditor`
  gezogen, der sie jetzt aufruft), `GraphMetrics.PortSize`/`MinCanvasWidth`/`MinCanvasHeight`,
  `GraphElementKind.Trigger`, Ausgangs-Port in `GraphNodeCard`, vier `[JSInvokable]` in `DialogGraph.razor`
  (`CreateQuestionAtAsync`, `ConnectAsync`, `ConnectToNewQuestionAsync`, `MoveNodeAsync`) und im JS-Modul
  `send()`, `beginLink`/`endLink`, die Palette-Geste.
- **Testlauf im Graphen (#104):** ebenfalls **kein Core-Code und keine Schema-Änderung**. Der Test-Runner
  (`/dialogs/{id}/test`) bekommt eine zweite Ansicht desselben Laufs (Umschalter „Verlauf"/„Graph",
  Deep-Link `?view=graph`); Start/Submit/Edit bleiben in `DialogTestRunner.razor`, die Karte „Aktuelle
  Frage" steht außerhalb des Umschalters und gilt für beide. Neu im Designer:
  `Services/GraphRunAnalyzer.cs` (leitet den Pfad aus der **Antwortfolge** ab – die Engine protokolliert
  keine `TransitionId`; parallele Übergänge sind deshalb *mehrdeutig*), `Models/GraphRunModel.cs`
  (`GraphRunOverlay`, `GraphRunVisit`, `GraphRunAnswer`, `GraphRunEdgeUse`, `GraphRunLoopState`,
  `GraphRunTrigger`), `Components/GraphRunCanvas.razor` (bindet das **vorhandene** `DialogGraph.razor.js`
  und meldet Züge als `NodeMove`), `Components/GraphRunInspector.razor` (Antworten je Iteration,
  Bindungen, Ereignisse am gewählten Knoten), Laufzustand als `[Parameter]` an `GraphNodeCard`,
  `NodeMove` in `Models/GraphEdits.cs` und `RunExpressionSnapshot` jetzt `public` (CS0053).
- **Canvas-E2E (#105):** die Abdeckung des Canvas im Browser (zwei neue Tests, siehe unten) – und die
  einzige Feature-Ergänzung der Stufe: **„Als Einstiegsfrage setzen"** im `GraphQuestionPanel`
  (`SetStart` → `GraphInspector.SetStartQuestion` → `DialogGraph.SetStartQuestionAsync` →
  `UpdateDialogCommand`). Vorher ließ sich der Einstiegspunkt nur im Dialog-Editor setzen, obwohl der
  Graph über sein Fehlen warnte. Kein Core-Code: Der Guard des Commands greift genau dann, wenn sich
  `StartQuestionId` ändert, der Knopf trägt deshalb das übliche `Locked` des Panels.
- **Abnahme-Befunde (#118):** zwei Nachbesserungen aus dem manuellen Durchgang zum Abschluss von EPIC 11,
  beide **ohne** Core-Code. Neu `Services/GraphWarningList.cs` – die Publish-Rückfrage des `DialogEditor`
  liest jetzt **alle** Graph-Warnungen (`DialogGraphModel.AllWarnings`) statt nur die des
  `TransitionWarningAnalyzer`; eine unerreichbare Frage ließ sich vorher ohne Rückfrage veröffentlichen.
  Dazu die eine CSS-Regel `main.flirty-content:has(.graph-layout)`, mit der die Graph-Seiten die
  1100-px-Lesebreite aufheben. Beides unten unter *Konventionen* im Detail.
- **Tests:** `tests/Flirty.Tests/Designer/` (`JsonConnectionProfileStoreTests`,
  `ConnectionProfileOperationsTests`, `FlirtyAdminGatewayTests`, `QuestionFormModelTests`,
  `DesignerExpressionContextTests`, `LoopAnalyzerTests`, `TriggerFormModelTests`,
  `FlirtyRuntimeGatewayTests`, `AnswerValueCodecTests`, `RunExpressionContextTests`,
  `DesignerTriggerLogTests`, `TransitionWarningAnalyzerTests`, `GraphWarningListTests`, `GraphLayoutTests`,
  `DialogGraphBuilderTests`, `GraphEditingTests`, `GraphRunAnalyzerTests`; gemeinsamer DI-Stack in
  `DesignerTestHost`) plus im Core
  `Domain/TriggerConfigTests`, `Runtime/DialogTriggerDispatchTests`,
  `Runtime/StartDialogVersionCommandHandlerTests` und `Runtime/DialogLayoutTests`. Dazu die Browser-Abdeckung in
  `tests/Flirty.E2E/` (`DesignerAppFixture`, `DesignerE2ETests`, gemeinsame Browser-Sitzung in
  `PlaywrightSession`).

## Leitplanken für die Umsetzung

1. **Über die Engine arbeiten, nicht am DbContext vorbei.** Der Designer nutzt die vorhandenen
   Admin-Commands/Queries über `ISender` – **nicht** direkt `FlirtyDbContext` oder `IDialogAdminStore`.
   Vorhanden in `src/Flirty/Runtime/Admin/`:
   - Dialoge: `ListDialogsQuery`, `GetDialogQuery`, `CreateDialogCommand`, `UpdateDialogCommand`,
     `DeleteDialogCommand`, `PublishDialogCommand`, `UnpublishDialogCommand`.
   - Fragen: `Create/Update/DeleteQuestionCommand`. Optionen: `Create/Update/DeleteAnswerOptionCommand`.
     Übergänge (Branching): `Create/Update/DeleteTransitionCommand`. Schleifen:
     `Create/Update/DeleteLoopCommand`. Trigger: `Create/Update/DeleteTriggerCommand`.
   - Sichten (navigationsfrei) in `AdminModels.cs`: `DialogSummary`, `DialogDetail`, `QuestionDetail`,
     `AnswerOptionDetail`, `TransitionDetail`, `LoopDetail`, `TriggerDetail`.
   - DI: `AddFlirty(...)` registriert `IDialogAdminStore`; im `Program.cs` des Designers ergänzen
     (inkl. Provider-Wahl je Connection-Profil).

   **Konkret seit #38: immer über `FlirtyAdminGateway`, nie `@inject ISender`.**
   ```csharp
   var result = await Admin.ExecuteAsync((sender, token) => sender.Send(new ListDialogsQuery(), token));
   if (!result.Success) { _error = result.Error; return; }
   ```
   Das Gateway öffnet je Operation einen frischen DI-Scope (in Blazor Server lebt ein Scope sonst den
   ganzen Circuit → der `FlirtyDbContext` bliebe an das zuerst benutzte Profil gepinnt, der
   Change-Tracker liefe voll, und der nicht threadsichere Kontext würde geteilt) und liefert ein
   `AdminResult<T>` mit deutscher Fehlermeldung statt einer Ausnahme, die den Circuit killt.

2. **Multi-DB per Connection-Profil (#37) — UMGESETZT.** Provider + ConnectionString als Profile lokal
   verwaltet; zur Laufzeit über `IDbContextFactory<FlirtyDbContext>` (Impl. `FlirtyDesignerDbContextFactory`)
   gegen das aktive Profil geöffnet. Das Provider→`MigrationsAssembly`-Mapping liefert das öffentliche
   Core-API `FlirtyDatabaseProvider` + `DbContextOptionsBuilder.UseFlirtyProvider(...)` (Details:
   `docs/DESIGNER.md`, `docs/PERSISTENCE.md`). Nicht duplizieren – dieses API wiederverwenden.

3. **Ausdrücke beim Speichern validieren (#40/#42 umgesetzt).** Branching-Bedingungen und
   Trigger-Ausdrücke über `IExpressionEvaluator.Validate(...)` kompilieren/prüfen, bevor gespeichert
   wird – die Engine ist gesandboxt (kein `eval`), siehe `docs/BRANCHING-EXPRESSIONS.md`. Dasselbe
   Prinzip setzt #39 bereits für Validierungs-**Muster** um:
   `QuestionFormModel.TryBuildValidationRules` kompiliert die Regex mit demselben 250-ms-Timeout wie der
   `AnswerValidator`, statt den Fehler bis zur Laufzeit zu vertagen.

   **Den Kontext dafür liefert `DesignerExpressionContext` (#40) – wiederverwenden, nicht nachbauen.**
   Er bindet je Frage einen Beispielwert, dessen **Typ exakt der Laufzeit-Bindung entspricht** (Zahl →
   `long`, Datum → **Zeichenkette**, Mehrfachauswahl → Liste) und jede Loop-Collection als leere Liste.
   #42 nutzt ihn **unverändert**: `TriggerDefinition.Expression` läuft über dieselbe Engine.
   Zwei Fallen: Zeichenketten-Literale **nicht** per `JsonSerializer` quotieren (dessen
   `\u00XX`-Escapes lehnt der Parser ab), und die Fehlermeldung der Engine ist **englisch** – deutsch
   rahmen statt übersetzen.

   **Fachliches JSON immer über den Core-Typ serialisieren, nicht über ein Duplikat.** `#39` benutzt
   `Flirty.Validation.ValidationRules` direkt (camelCase, `WhenWritingNull`); enthält gespeichertes JSON
   unbekannte Felder, fällt der Editor auf ein Roh-JSON-Textfeld zurück, statt sie beim Speichern
   stillschweigend zu verwerfen. `#42` macht es genauso mit `Flirty.Domain.TriggerConfig`
   (`url`/`name`) für `TriggerDefinition.Config` – inklusive Roh-JSON-Fallback.

4. **Loops sind Branching + Marker (#41 umgesetzt).** Ein Zyklus entsteht durch eine `Transition` auf eine
   frühere Frage; `LoopDefinition` (CollectionKey/Entry/Breaking) macht ihn sichtbar. Der Branching-Editor
   zeichnet ihn als **Rücksprung**-Badge, den Marker pflegt der Loop-Editor. Siehe `docs/LOOPS.md`.

   **Graph-Analysen des Cores spiegeln, nicht importieren.** `Services/LoopAnalyzer.cs` rechnet den
   Schleifen-Bereich nach (`(vorwärts ab Entry, Stopp an Breaking) ∩ (rückwärts zu Breaking) ∪
   {Entry, Breaking}`), weil `LoopResolver` `internal` ist und eine `Dialog`-Entity mit Navigationen
   braucht – der Designer hat nur `DialogDetail`. Dieselbe Abgrenzung wie `DesignerExpressionContext` ↔
   `SessionExpressionContextBuilder`. **Pflicht dabei:** ein Test, der beide Implementierungen auf
   demselben Graphen vergleicht (`LoopAnalyzerTests`, Body indirekt über
   `LoopResolver.ResolveAssignment`), sonst driften sie stillschweigend auseinander.

   **Warnungen spiegeln die Resolver-Regeln, nicht die Intuition.** „Exit unerreichbar" folgt exakt dem
   `TransitionResolver`: erster zutreffender Nicht-Default gewinnt (leerer Ausdruck trifft immer zu),
   sonst der oberste Default. Weitere Fälle: kein Exit, kein Rücksprung, überlappende Bereiche (der
   `LoopResolver` wirft dann schon im Konstruktor – **jede** Session bricht ab) und verdeckende
   `CollectionKey`s (Prüfung über `DesignerExpressionContext.IsBindable`/`IdentifierNote` teilen, nicht
   duplizieren).

   **FK-lose Verweise brauchen Aufräumen.** `LoopDefinition` referenziert Fragen ohne Fremdschlüssel –
   `DeleteQuestionCommand` entfernt verweisende Marker deshalb mit, wie schon die Übergänge. Eindeutig
   erzwungen wird nur der `CollectionKey` je Dialog (`LoopCollectionKeyExistsAsync` →
   `InvalidOperationException` → 409); ohne ihn überschrieben sich zwei gleichnamige Marker in der
   Collection-Bindung still.

5. **Trigger sind Rückkanäle – und feuern seit #42 wirklich (`docs/TRIGGERS.md`).** Bis dahin war
   `TriggerDefinition` tote Konfiguration; jetzt liest der Core-`WebhookNotificationHandler` je
   Notification zusätzlich die Trigger des Session-Dialogs (`IDialogStore.GetTriggersForSessionAsync`)
   und stellt `Kind = Webhook` zu. Merkposten für Erweiterungen:
   - `Kind = InProcess` stellt **nichts** zu (Host-App-Handler) – im UI benennen, nicht verschweigen.
   - **Best-effort ist Pflicht:** unlesbare `Config`, fehlende URL und nicht auswertbare Bedingungen
     werden geloggt und übersprungen. Nie werfen – der Handler läuft im Scope von Submit/Edit.
   - Querfeld-Regeln gehören in den Command (`IValidatableObject` → `ValidationException` → 400), nicht
     nur in die UI: `AfterQuestion` braucht genau dort eine `QuestionId`, `Webhook` eine absolute URL.
   - Wie bei Loops gilt: FK-lose Frage-Verweise räumt `DeleteQuestionCommand` mit ab.

6. **Test-Runner (#43) – umgesetzt.** Ein Dialog-Durchlauf über `IFlirtyEngine` gegen das aktive Profil,
   je Schritt in einem frischen Scope (`FlirtyRuntimeGateway`, Basis `DesignerGateway`). Merkposten:

   **Entwürfe brauchen einen eigenen Start.** `StartDialogCommand` löst über den fachlichen Schlüssel auf
   und startet nur **veröffentlichte** Dialoge. Für den Runner kam deshalb `StartDialogVersionCommand`
   (Start einer konkreten `DialogId`, veröffentlichungs-unabhängig) dazu – bewusst **ohne**
   ASP.NET-Endpunkt: über HTTP bleibt der Publish-Status die Produktionsschranke. Alles ab dem Start
   funktionierte unverändert, weil die Session ihre `DialogId` pinnt.

   **Der Lauf ist echt.** Er schreibt `DialogSession`/`SessionAnswer` in die Datenbank des Profils und
   stellt konfigurierte Webhooks zu. Je Lauf ein frischer `ExternalUserKey` mit Präfix `designer-test-`
   (sonst greift Resume statt Neu-Start); aufgeräumt wird nicht – die Engine kennt kein Löschen von
   Sessions. Beides gehört sichtbar ins UI, nicht in eine Fußnote.

   **Ein Vertrag, eine Stelle.** `AnswerValueCodec` ist die einzige Quelle der JSON-Kodierung je
   `QuestionType` (verbindlich ist der Core-`AnswerValidator`); `DesignerExpressionContext.SampleJson`
   leitet seine Beispielwerte davon ab, damit Ausdrucks-Validierung und Testlauf nicht auseinanderlaufen.

   **Scoped Zustand muss adoptiert werden.** Der `DesignerTriggerLog` wird – wie `ActiveConnectionProfile` –
   per `Adopt` in den Kind-Scope durchgereicht; sonst schrieben die dort konstruierten
   `INotificationHandler<T>` in eine Wegwerf-Instanz. Gilt für **jeden** weiteren Circuit-Zustand, den ein
   Gateway braucht (Hook: `DesignerGateway.Prepare`).

   **`iterationIndex` ist kein Fortschrittszähler.** Er meint den Index der zuletzt *gegebenen* Antwort auf
   die offene Frage (`LoopResolver.ResolveIterationIndex`), nicht die bevorstehende Iteration – als
   „laufende Iteration" an der aktuellen Frage angezeigt wäre er falsch. Die exakten Indizes stehen an den
   Verlaufseinträgen (`SessionAnswerView.IterationIndex`).

   **`[Parameter]` erzwingt `public`.** Razor erzeugt Komponenten als `public` Klassen; `internal`
   Parametertypen scheitern an CS0053. Deshalb sind `AnswerInputModel`/`AnswerChoice` als einzige
   Designer-Modelle `public` (der Designer ist `IsPackable=false`, es entsteht keine Paket-API).

7. **Der Canvas gehört dem Browser (ADR 0006, seit #101 umgesetzt).** Der Designer ist Blazor
   **Server** – jedes Blazor-Ereignis ist ein SignalR-Roundtrip. Der Spike zu #100 hat gemessen, was
   das für eine Zieh-Geste bedeutet: 40 px Rückstand hinter dem Zeiger und 68 Nachrichten, wenn die
   Bewegung in C# läuft, gegenüber 0 px und 2 Nachrichten beim Eigenbau. Daraus folgen vier Regeln für
   **jede** Canvas-Erweiterung:

   - **Zwischen `pointerdown` und `pointerup` geht keine Nachricht an den Server.** Bewegung läuft im
     collocated `*.razor.js`-Modul; erst das Loslassen ruft einen Command.
   - **Attribute, die das JS-Modul setzt, darf C# nie rendern.** Das `transform` auf
     `.graph-viewport` gehört dem Modul – ein einziges gerendertes `transform`, und der nächste
     Re-Render (etwa eine Auswahl) setzt Verschiebung und Zoom zurück.
   - **Kanten werden vor den Knoten gezeichnet.** Ihr breiter, unsichtbarer Trefferpfad läge sonst über
     der Knotenmitte und verschluckte den Klick. Aus demselben Grund hat der Schleifen-Rahmen
     `fill="none"` und `pointer-events: stroke`. Und im `pointerdown`-Handler steht ein frühes `return`
     auf bedienbaren Elementen – **ohne** `preventDefault()`, sonst verpufft Blazors `@onclick`.
   - **Der Canvas setzt `data-canvas-ready`**, sobald das Modul gebunden ist. `InteractWhenReadyAsync`
     trägt hier nicht (siehe Konventionen).

   **Zahlen in SVG-Attributen ausschließlich über `SvgFormat.N`.** Die feste Anzeige-Kultur `de-DE`
   gilt auch beim Rendern: Eine interpolierte `double`-Koordinate wird zu `12,5`, und weil das Komma in
   der Pfadsyntax ein *Trennzeichen* ist, entsteht eine falsche Zahlenfolge – ohne Ausnahme, ohne
   Meldung, nur mit falschem Bild.

   **Auto-Layout muss deterministisch sein**, sonst wackeln E2E-Selektoren: nur Listen nach außen (nie
   Menge oder Wörterbuch), Sortierschlüssel enden mit einem eindeutigen Ordinal (Totalordnung statt
   geliehener `OrderBy`-Stabilität), Koordinaten nur aus ganzzahligen Schicht-/Spaltenwerten. Das
   Ordinal kommt aus `(Order, Id)` und **nicht** aus der Guid: `CreateDialogVersionCommand` vergibt beim
   Klonen jeder Frage eine neue Guid, ein guid-basiertes Layout würfelte bei jeder neuen Dialogversion
   neu durch.

   Knoteninhalte sind **Razor-Komponenten in einem `<foreignObject>`** – Blazors Namensraum-Prüfung
   schließt `foreignObject` aus, Kindelemente entstehen also im HTML-Namensraum. Damit sind Knoten echte
   `<button>`: Fokusring, Enter/Leertaste und Screenreader-Rolle kommen von der Plattform.

8. **Canvas-Positionen liegen in `DialogLayout`, nicht am Graphen (ADR 0007, seit #102 umgesetzt).**
   Muster für jede weitere Geste, die eine Position schreibt:

   - **Schreibpfad ohne `DialogEditGuard`.** Koordinaten berühren die Session-Semantik nicht; ein
     veröffentlichter Dialog muss anordbar bleiben. Weil `DialogLayout` eine eigene Tabelle ist, ist das
     keine Umgehung der Publish-Sperre, sondern deren Grenze. Wer einen weiteren Layout-Command ergänzt,
     setzt dort **keinen** Guard – und schreibt einen Test dagegen, der das festnagelt.
   - **Ziehen in drei Schritten:** `pointerdown` merkt vor (kein `preventDefault`, keine Capture) →
     Schwelle **4 px**, erst darüber wird aus dem Klick ein Zug → `pointerup` schluckt das folgende
     `click` und ruft **einmal** `invokeMethodAsync`. `invokeMethodAsync` gehört ausschließlich in
     `onPointerUp`.
   - **Bildschirm → Nutzerkoordinaten über `viewport.getScreenCTM().inverse()`**, nie über den
     Zoomfaktor allein: Die Matrix enthält auch die `viewBox`-Skalierung gegenüber der CSS-Breite.
   - **Geometrie bleibt in C#.** Während des Zugs werden anliegende Kanten gedimmt (`data-from`/`data-to`
     am Kanten-`<g>`), nicht im Browser neu gerechnet – sonst gäbe es zwei Quellen für dieselbe
     Routing-Logik.
   - **Gespeicherte Positionen greifen erst in `GraphLayout.Render`.** Schichtung, Kantenform,
     Baryzentrum und Kanalvergabe bleiben am Auto-Layout: Ein Zug ändert eine Position, nicht die
     Struktur. Die Zeichenfläche wächst um verschobene Knoten mit.
   - **Der Commit lädt nicht neu.** Das `DialogDetail` liegt in einem Feld; die Antwort des Commands
     ersetzt darin `Layout`, und das Modell wird lokal neu gebaut – ein `GetDialogQuery` je Geste wäre
     ein zweiter Roundtrip für Daten, die man schon hat.
   - **Ein neuer `LayoutElementKind` kostet zwei Handarbeits-Zweige:** den Klon in
     `CreateDialogVersionCommand` (kind-bewusst, nicht abbildbare Zeilen verwerfen) und das Aufräumen in
     `DeleteQuestionCommand`. Beide sind heute je durch einen Test in `Runtime/DialogLayoutTests`
     gesichert – für den neuen Kind kommen zwei dazu.

9. **Canvas-Gesten schreiben – und sind nicht idempotent (ADR 0008, seit #103).** Muster für jede weitere
   Geste:

   - **Ein Gateway-Aufruf je Geste**, darin alle nötigen Commands in der Reihenfolge ihrer Abhängigkeit
     (die Layout-Zeile braucht die Guid der neuen Frage). Ein DI-Scope, ein Fehlerpfad – aber **keine**
     Transaktion: Jeder Handler speichert selbst. Scheitert ein Folge-Command, wird das gemeldet und
     **nicht** kompensiert (eine gerade angelegte Frage wegen eines Layout-Schluckaufs zu löschen wäre der
     teurere Fehler).
   - **Nach einer Graph-Mutation neu laden** (`RunGestureAsync` → `LoadAsync`), nicht lokal fortschreiben.
     `Rebuild` bleibt dem Layout-Pfad, dessen Command den vollständigen Stand zurückgibt. Grund sind die
     graphweit gerechneten Warnungen und die Mit-Aufräumung von `DeleteQuestionCommand`. Danach
     **`ReconcileSelection()`** – eine Auswahl auf ein gelöschtes Element rendert sonst einen leeren
     Inspector-Zweig.
   - **Zwei Riegel, beide nötig.** Clientseitig `send()` im Modul; **das Versprechen von
     `invokeMethodAsync` ist die Quittung**, ein zweiter Rückkanal wäre eine Stelle zum Vergessen.
     Serverseitig Frühausstieg auf `_busy` in **jeder** `[JSInvokable]` – ein Interop-Aufruf sieht kein
     gerendertes `disabled`. Ein direkter `invokeMethodAsync`-Aufruf neben `send()` unterläuft den Riegel.
   - **`data-editable` und `data-busy` gehören C#, das JS liest sie** – bei jeder Geste frisch, nicht als
     `attach`-Option. Gesperrt wird über `pointer-events`, **nie** über `disabled` an Port oder
     Palette-Eintrag: Blazor rendert das Attribut sonst mitten im Zug neu und die Pointer-Capture reißt.
   - **Geometrie einer laufenden Geste in von C# gerenderte Platzhalter** (`.graph-rubber`,
     `.graph-ghost`) – das Modul setzt nur `d` bzw. `x`/`y`/`width`/`height`. Kein `createElement` in
     einem Blazor-Container. Beide brauchen `pointer-events: none`, weil der Ziel-Hit-Test über
     `document.elementFromPoint` läuft (nach `setPointerCapture` ist `event.target` das Capture-Element).
   - **`swallowNextClick` gehört an das Element, dessen `click` folgt** – beim Palette-Zug ist das der
     Palette-Eintrag, nicht der Canvas. Sonst legt jeder Zug zusätzlich die Klick-Aktion aus.
   - **Neue interaktive Elemente im Knoten brauchen ihren Zweig im `pointerdown`** – **vor** der
     `.graph-node`-Prüfung, sonst verschluckt der Verschiebe-Zug ihre Geste; und wie überall ohne
     `preventDefault`, damit Blazors `@onclick` (der zeigerlose Weg) trägt.
   - **Entscheidungsregeln gehören in einen Service, nicht in den `@code`-Block.**
     `tests/Flirty.Tests/Designer` rendert keine Komponenten (kein bUnit) – was im Razor liegt, ist nicht
     prüfbar. Vorbilder: `GraphEditing`, `LoopAnalyzer`, `QuestionFormModel.SuggestKey`.
   - **`[Parameter]`-Typen müssen `public` sein.** Die Formularmodelle bleiben `internal` und privater
     Zustand des Panels; nach außen gehen die Records aus `Models/GraphEdits.cs`. Zuständigkeit:
     **Panel = Formular, Seite = Commands.**
   - **Ein Inspector-Panel arbeitet ohne `EditForm`:** rohe Felder mit `@oninput`, Pflichtprüfung im
     Speichern-Handler, `@key` an der Element-Id. Zwei gemessene Gründe: `onchange` liefert den Wert erst
     beim Verlassen des Felds und verliert ihn, weil das Panel nach jeder Geste neu aufgebaut wird; und
     der Submit einer `EditForm` kam in einem Panel innerhalb wechselnder `@if`-Zweige nicht an.
   - **In der E2E beweist ein DOM-Wert nichts.** Verpufft die erste Interaktion auf einem frisch
     gerenderten Feld, steht der getippte Wert trotzdem im DOM, bis der nächste Render ihn überschreibt –
     ein `ToHaveValueAsync` in diesem Fenster meldet Erfolg, und gespeichert wird der alte Wert. Geprüft
     wird eine **serverseitig erzeugte** Wirkung, und die wiederholte Einheit umfasst Füllen *und*
     Speichern. Umgekehrt darf eine Geste, die ihren Auslöser sperrt, nicht allein wiederholt werden.

## Aufbaureihenfolge

**EPIC 7 – abgeschlossen:** #37 Connection-Profile ✅ → #38 Dialog-CRUD-UI ✅ → #39 Frage-Editor ✅ →
#40 Branching-Editor ✅ → #41 Loop-Editor ✅ → #42 Trigger-Editor ✅ → #43 Test-Runner ✅ →
#46 Designer-E2E ✅.

**EPIC 11 – visueller Graph-Designer (#99, abgeschlossen):** #100 Spike Canvas-Technik ✅ (ADR 0006) →
#101 Graph-Ansicht lesend ✅ → #102 Layout-Persistenz (Tabelle `DialogLayout`) ✅ (ADR 0007) →
#103 Editieren auf dem Canvas ✅ (ADR 0008) → #104 Testlauf im Graphen ✅ →
#105 Playwright-E2E des Canvas ✅.

## Konventionen

- Blazor-Komponenten unter `Components/` (Pages in `Components/Pages/`), Server-interaktiver Render-Mode
  beibehalten.
- **Komponentennamen dürfen die Sichttypen aus `Flirty.Runtime.Admin` nicht verdecken** – deshalb heißen
  die Detailseiten `DialogEditor`/`QuestionEditor`/`TransitionEditor`/`LoopEditor`/`TriggerEditor` und
  nicht `DialogDetail`/`QuestionDetail`/… (sonst verschattet der generierte Komponententyp den
  gleichnamigen Record). Gilt genauso für eine kommende Seite zu `AnswerOptionDetail`.
- **Live-Validierung braucht ein rohes `<textarea>` mit `@oninput`**: an einer `InputTextArea` lässt sich
  `@bind-Value:event="oninput"` nicht mit `@bind-Value:after` kombinieren (RZ10010), und ohne `oninput`
  prüft der Editor erst beim Verlassen des Felds.
- Gemeinsame UI-Klassen gehören nach `wwwroot/app.css` (global), nicht in jede `*.razor.css` kopiert.
- **Braucht eine Seite mehr als die Lesebreite, entscheidet der Inhalt – nicht die Route** (#118).
  `main.flirty-content` deckelt auf 1100 px; der Deckel fällt über
  `main.flirty-content:has(.graph-layout) { max-width: none; }` in `app.css`. Kein zweites Layout und kein
  `@layout` an der Seite: Der Test-Runner rendert `.graph-layout` nur im Graph-Zweig seines Umschalters,
  seine Verlaufsliste soll schmal bleiben. Aufwärts (Kind → Vorfahr) gibt es in Blazor keinen Cascade,
  `:has()` ist hier das Werkzeug – die Regel muss **global** stehen (CSS-Isolation reicht nicht in
  Kindkomponenten) und braucht `main` davor, damit die Spezifität die scoped Regel schlägt.
- Bestätigungen **inline** im Komponentenzustand lösen, **kein** JS-`confirm`/`alert` – das blockiert
  sonst die Playwright-E2E (#46).
- UI-Texte und Doku **deutsch**. Der Designer ist `IsPackable=false` (kein NuGet-Paket) → CS1591 ist
  hier **kein** Fehler, XML-Docs sind optional.
- E2E-Tests des Designers gehören nach `tests/Flirty.E2E` (Playwright, #46). Zwei Fallstricke, die
  `docs/DESIGNER.md` § Tests ausführt: Der in-Prozess gehostete Designer braucht
  `ApplicationName = "Flirty.Designer"` **und** `EnvironmentName = "Development"` (sonst fehlt
  `_framework/blazor.web.js` und nichts ist interaktiv), und nach **jedem** Seitenwechsel verpufft die
  erste Interaktion still, bis der Circuit die Seite übernommen hat – deshalb wird sie über
  `InteractWhenReadyAsync` wiederholt und muss idempotent sein.
- **Auf dem Canvas trägt `InteractWhenReadyAsync` nicht** – Ziehen und Zoomen sind nicht idempotent
  (ein wiederholter Drag verschöbe doppelt). Dort wird auf `svg[data-canvas-ready='true']` gewartet;
  das Attribut setzt das JS-Modul beim Binden, und weil `OnAfterRenderAsync` beim Prerendering gar nicht
  läuft, ist es zugleich der Nachweis, dass der Circuit die Seite übernommen hat. Es ist das **erste**
  `data-`-Attribut im Designer und bewusst eine Ausnahme von der sonstigen Selektor-Praxis (Rolle,
  Überschrift, Feld-`id`, CSS-Klasse).
- **Ein Drag in der E2E braucht `ScrollIntoViewIfNeededAsync` und `page.Mouse`.** `DragToAsync` nutzt die
  HTML5-Drag-and-Drop-API und löst auf einem SVG-Canvas mit Pointer-Events gar nicht aus; und
  Maus-Koordinaten sind fensterbezogen, während der Canvas-Host 70 vh hoch unter Kopfzeile, Hinweis und
  Werkzeugleiste steht – ohne Scrollen zielt die Geste bei einem Knoten der unteren Schichten ins Leere,
  ohne jede Fehlermeldung. Über mehrere `Mouse.MoveAsync`-Schritte ziehen, damit die 4-px-Schwelle wie
  bei einer echten Geste überschritten wird. Gezielt wird in **Bruchteilen** der Fläche
  (`DragToCanvasFractionAsync`), nicht in Pixeln: Das SVG skaliert seinen `viewBox` in den Host, wie groß
  ein Knoten auf dem Schirm ist hängt also am Fenster – eine feste Pixelangabe träfe bei anderem Zuschnitt
  einen Knoten statt die freie Fläche, und aus dem Zug ins Leere würde still eine Verbindung.
- **Hinter jeder Canvas-Geste steht eine serverseitig erzeugte Wirkung, keine Wartezeit** (#105). `send()`
  verwirft eine zweite Geste **still**, solange die erste läuft: Eine zu früh ausgelöste Bewegung
  hinterlässt keinen Fehler, nur einen fehlenden Effekt. Wird ein Canvas-Test rot, lautet die erste Frage
  deshalb „welche Geste wurde still verworfen?" – nicht „welche Assertion ist gescheitert?".
- **Eine nicht idempotente Aktion braucht eine sichtbare Vorbedingung statt einer Wiederholung** (#105).
  Beim Verbinden über `#inspectorConnect` ist das der Knopf „Verbinden": Er wird genau dann bedienbar, wenn
  der Server das Ziel kennt. Wiederholt wird nur das Wählen, geklickt wird einmal. Sonst überholt der
  Re-Render der Knotenauswahl die Listenauswahl und verwirft sie (der `@key` am Panel ersetzt die
  Instanz) – dieselbe Familie wie „ein DOM-Wert beweist nichts", nur an einem `<select>`.
- **Warnungstexte sind Vertrag.** `TransitionWarningAnalyzer` und `LoopAnalyzer` liefern
  `GraphWarning` (Ziel + Text). Der Text ist derselbe, den die Listenansicht seit jeher zeigt – die
  Publish-Rückfrage zählt ihn und die E2E-Suite sucht darin. Wer umformuliert, ändert die Oberfläche
  und muss es bewusst tun (`TransitionWarningAnalyzerTests` nagelt die Wortlaute fest). Eine **zweite**
  Warnungslogik neben diesen beiden gibt es nicht: Graph- und Listenansicht schöpfen aus derselben
  Quelle, verortet wird nach Verursacher (Gruppeneigenschaft → Frage, Eigenschaft eines Übergangs →
  seine Kante).
- **Die Publish-Rückfrage liest den *ganzen* Graphen** (#118), nicht einen einzelnen Analyzer: Quelle ist
  `DialogGraphModel.AllWarnings` über `GraphWarningList.Describe`, und weil die Erreichbarkeit erst aus
  der Anordnung ab der Einstiegsfrage entsteht, hält der `DialogEditor` dafür ein `DialogGraphBuilder`-
  Modell in einem Feld (`_graph`, einmal je Laden – **nie** im Markup, sonst liefe die Anordnung bei jedem
  Klick). Wer eine neue Warnungsart ergänzt, muss die Rückfrage deshalb **nicht** anfassen; wer die Quelle
  wieder auf einen Analyzer verengt, baut den Defekt von #118 nach. Und wer eine Warnung ohne
  `QuestionId` erzeugt (`ForDialog`/`ForLoop`), verlässt sich darauf, dass das Präfix im Service liegt und
  nicht im `@code`-Block.
- **Der gelaufene Pfad ist abgeleitet, nicht gespeichert** (#104). Die Engine hält keine `TransitionId` an
  der Antwort fest; `GraphRunAnalyzer` liest den Weg aus der Antwortfolge. Parallele Übergänge zwischen
  denselben zwei Fragen bleiben damit **prinzipiell** mehrdeutig – das wird ausgewiesen, nicht geraten. Der
  Domäne dafür eine Spalte zu geben, wäre Schema-Änderung und Laufzeit-Schreiblast für einen reinen
  Anzeigebelang; wer es doch braucht, begründet es in einem ADR.

## Definition of Done

Feature funktioniert im Server-interaktiven Designer über die Admin-Commands (via `FlirtyAdminGateway`) ·
Ausdrücke werden beim Speichern validiert · Service-Tests in `tests/Flirty.Tests/Designer/` ·
`docs/DESIGNER.md` beim jeweiligen Feature erweitern · berührt die Änderung einen Fluss der
Designer-E2E, `tests/Flirty.E2E/DesignerE2ETests` mitziehen.

## Verifikation

```pwsh
dotnet build Flirty.sln
dotnet run --project src/Flirty.Designer     # Designer lokal starten
dotnet test tests/Flirty.Tests
dotnet test tests/Flirty.E2E                # Browser-Abdeckung (braucht Chromium, s. docs/DESIGNER.md)
```
