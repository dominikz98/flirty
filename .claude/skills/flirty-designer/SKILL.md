---
name: flirty-designer
description: Den Blazor-Designer (Flirty.Designer) aufbauen oder erweitern – Dialog-/Frage-/Antwort-/Branching-/Loop-/Trigger-Konfiguration, Multi-DB-Connection-Profile. Verwenden bei "Designer", "Blazor-UI für Dialoge", "Dialog-Editor", "Branching-Editor", "Connection-Profil", "EPIC 7", Issues #37–#43.
---

# Blazor-Designer aufbauen / erweitern

> **Status: EPIC 7 (Issues #37–#43) vollständig umgesetzt** – Connection-Profil-Verwaltung (#37),
> Dialog-CRUD (#38), Frage-Editor (#39), Branching-Editor (#40), Loop-Editor (#41), Trigger-Editor (#42)
> und Test-Runner (#43); `docs/DESIGNER.md` beschreibt alle sieben. Offen ist nur die
> Playwright-E2E-Abdeckung (#46). Dieser Skill ist die **Leitplanke** für Erweiterungen: die
> beabsichtigte Architektur und die Konventionen, an die man sich beim Implementieren halten soll.
> Referenz: `docs/DESIGNER.md`, `docs/ARCHITECTURE.md` §4/§8/§10, `docs/BACKLOG.md` EPIC 7.

## Ist-Zustand (verifiziert)

- `src/Flirty.Designer/Flirty.Designer.csproj`: `Microsoft.NET.Sdk.Web`, referenziert `..\Flirty` **und
  alle drei** `..\Flirty.Migrations.{Sqlite,PostgreSql,SqlServer}` (für Multi-DB-Migrate), plus
  `InternalsVisibleTo("Flirty.Tests")`; `BlazorDisableThrowNavigationException=true`.
- `Program.cs`: `AddRazorComponents().AddInteractiveServerComponents()` +
  `MapRazorComponents<App>().AddInteractiveServerRenderMode()` → **Blazor Web App, Server-interaktiv**.
  Ruft seit #37 **`AddFlirty()` (parameterlos)** auf; der `FlirtyDbContext` wird pro aktivem
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
- **Tests:** `tests/Flirty.Tests/Designer/` (`JsonConnectionProfileStoreTests`,
  `ConnectionProfileOperationsTests`, `FlirtyAdminGatewayTests`, `QuestionFormModelTests`,
  `DesignerExpressionContextTests`, `LoopAnalyzerTests`, `TriggerFormModelTests`,
  `FlirtyRuntimeGatewayTests`, `AnswerValueCodecTests`, `RunExpressionContextTests`,
  `DesignerTriggerLogTests`; gemeinsamer DI-Stack in `DesignerTestHost`) plus im Core
  `Domain/TriggerConfigTests`, `Runtime/DialogTriggerDispatchTests` und
  `Runtime/StartDialogVersionCommandHandlerTests`.

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

## Aufbaureihenfolge (EPIC 7 – abgeschlossen)

#37 Connection-Profile ✅ → #38 Dialog-CRUD-UI ✅ → #39 Frage-Editor ✅ → #40 Branching-Editor ✅ →
#41 Loop-Editor ✅ → #42 Trigger-Editor ✅ → #43 Test-Runner ✅.

Offen bleibt die Playwright-E2E-Abdeckung des Designers (#46, `tests/Flirty.E2E`).

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
- Bestätigungen **inline** im Komponentenzustand lösen, **kein** JS-`confirm`/`alert` – das blockiert
  sonst die Playwright-E2E (#46).
- UI-Texte und Doku **deutsch**. Der Designer ist `IsPackable=false` (kein NuGet-Paket) → CS1591 ist
  hier **kein** Fehler, XML-Docs sind optional.
- E2E-Tests des Designers gehören nach `tests/Flirty.E2E` (Playwright, #46) – aktuell noch Skelett.

## Definition of Done

Feature funktioniert im Server-interaktiven Designer über die Admin-Commands (via `FlirtyAdminGateway`) ·
Ausdrücke werden beim Speichern validiert · Service-Tests in `tests/Flirty.Tests/Designer/` ·
`docs/DESIGNER.md` beim jeweiligen Feature erweitern · ggf. Playwright-E2E (#46).

## Verifikation

```pwsh
dotnet build Flirty.sln
dotnet run --project src/Flirty.Designer     # Designer lokal starten
dotnet test tests/Flirty.Tests
```
