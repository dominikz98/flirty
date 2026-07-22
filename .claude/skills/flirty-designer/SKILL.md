---
name: flirty-designer
description: Den Blazor-Designer (Flirty.Designer) aufbauen oder erweitern – Dialog-/Frage-/Antwort-/Branching-/Loop-/Trigger-Konfiguration, Multi-DB-Connection-Profile. Verwenden bei "Designer", "Blazor-UI für Dialoge", "Dialog-Editor", "Branching-Editor", "Connection-Profil", "EPIC 7", Issues #37–#43.
---

# Blazor-Designer aufbauen / erweitern

> **Status: teils umgesetzt (EPIC 7, Issues #37–#43).** Fertig sind die
> **Connection-Profil-Verwaltung (#37)**, das **Dialog-CRUD (#38)** und der **Frage-Editor (#39)**;
> `docs/DESIGNER.md` beschreibt alle drei. Die weiteren Editoren (Branching #40 … Test-Runner #43) sind
> **noch offen**. Dieser Skill ist die **Leitplanke** für den weiteren Aufbau: die beabsichtigte
> Architektur und die Konventionen, an die man sich beim Implementieren halten soll. Referenz:
> `docs/DESIGNER.md`, `docs/ARCHITECTURE.md` §4/§8/§10, `docs/BACKLOG.md` EPIC 7.

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
  `.field`, `.input`, `.btn`, `.data-table`, `.badge`, `.msg`, `.banner`, `.empty`) liegen **global** in
  `wwwroot/app.css`; `*.razor.css` enthält nur Seitenspezifisches.
- **Frage-Editor (#39):** `Models/QuestionFormModel.cs` (Metadaten + Regel-JSON ⇄ Eingabefelder, mit
  Roh-JSON-Fallback), `Models/AnswerOptionFormModel.cs`, `Models/QuestionTypeLabels.cs` (deutsche
  Typnamen, `UsesOptions`), Seite `Components/Pages/QuestionEditor.razor`
  (`/dialogs/{dialogId:guid}/questions/{questionId:guid}`) und der Abschnitt „Fragen" in
  `DialogEditor.razor` (Liste, Inline-Anlegen, ↑/↓, Löschen).
- **Tests:** `tests/Flirty.Tests/Designer/` (`JsonConnectionProfileStoreTests`,
  `ConnectionProfileOperationsTests`, `FlirtyAdminGatewayTests`, `QuestionFormModelTests`).

## Leitplanken für die Umsetzung

1. **Über die Engine arbeiten, nicht am DbContext vorbei.** Der Designer nutzt die vorhandenen
   Admin-Commands/Queries über `ISender` – **nicht** direkt `FlirtyDbContext` oder `IDialogAdminStore`.
   Vorhanden in `src/Flirty/Runtime/Admin/`:
   - Dialoge: `ListDialogsQuery`, `GetDialogQuery`, `CreateDialogCommand`, `UpdateDialogCommand`,
     `DeleteDialogCommand`, `PublishDialogCommand`, `UnpublishDialogCommand`.
   - Fragen: `Create/Update/DeleteQuestionCommand`. Optionen: `Create/Update/DeleteAnswerOptionCommand`.
     Übergänge (Branching): `Create/Update/DeleteTransitionCommand`.
   - Sichten (navigationsfrei) in `AdminModels.cs`: `DialogSummary`, `DialogDetail`, `QuestionDetail`,
     `AnswerOptionDetail`, `TransitionDetail`.
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

3. **Ausdrücke beim Speichern validieren (#40/#42).** Branching-Bedingungen und Trigger-Ausdrücke über
   `IExpressionEvaluator.Validate(...)` kompilieren/prüfen, bevor gespeichert wird – die Engine ist
   gesandboxt (kein `eval`), siehe `docs/BRANCHING-EXPRESSIONS.md`. Dasselbe Prinzip setzt #39 bereits
   für Validierungs-**Muster** um: `QuestionFormModel.TryBuildValidationRules` kompiliert die Regex mit
   demselben 250-ms-Timeout wie der `AnswerValidator`, statt den Fehler bis zur Laufzeit zu vertagen.

   **Fachliches JSON immer über den Core-Typ serialisieren, nicht über ein Duplikat.** `#39` benutzt
   `Flirty.Validation.ValidationRules` direkt (camelCase, `WhenWritingNull`); enthält gespeichertes JSON
   unbekannte Felder, fällt der Editor auf ein Roh-JSON-Textfeld zurück, statt sie beim Speichern
   stillschweigend zu verwerfen. Bei `LoopDefinition`/`TriggerDefinition` (#41/#42) genauso vorgehen.

4. **Loops sind Branching + Marker (#41).** Ein Zyklus entsteht durch eine `Transition` auf eine frühere
   Frage; `LoopDefinition` (CollectionKey/Entry/Breaking) macht ihn sichtbar. Im Designer den Zyklus als
   Loop-Block mit markierter Breaking Question zeichnen und bei einem Zyklus **ohne erreichbare
   Exit-Bedingung warnen** (Endlosschleifen-Risiko). Siehe `docs/LOOPS.md`.

5. **Test-Runner (#43):** ein Dialog-Durchlauf im Designer über `IFlirtyEngine` gegen das gewählte Profil.

## Empfohlene Aufbaureihenfolge (EPIC 7)

#37 Connection-Profile ✅ → #38 Dialog-CRUD-UI ✅ → #39 Frage-Editor ✅ → #40 Branching-Editor →
#41 Loop-Visualisierung → #42 Trigger-Editor → #43 Test-Runner.

Die Folge-Editoren (#40–#42) hängen sich sinnvoll in die Detailseite
`Components/Pages/DialogEditor.razor` (`/dialogs/{id:guid}`) ein – der `GetDialogQuery` liefert dort
bereits Fragen, Optionen und Übergänge. **Muster aus #39 übernehmen:** Liste (mit Inline-Anlegen,
↑/↓-Sortieren, Inline-Löschbestätigung) im `DialogEditor`, Details auf einer eigenen Unterseite. Beim
Sortieren den **Positionsindex** als neue `Order`/`Priority` schreiben statt nur zwei Werte zu tauschen
(repariert doppelte/lückenhafte Werte) und alle Updates in **einem** `ExecuteAsync`-Aufruf senden.

## Konventionen

- Blazor-Komponenten unter `Components/` (Pages in `Components/Pages/`), Server-interaktiver Render-Mode
  beibehalten.
- **Komponentennamen dürfen die Sichttypen aus `Flirty.Runtime.Admin` nicht verdecken** – deshalb heißen
  die Detailseiten `DialogEditor`/`QuestionEditor` und nicht `DialogDetail`/`QuestionDetail` (sonst
  verschattet der generierte Komponententyp den gleichnamigen Record). Gilt genauso für kommende
  Seiten zu `TransitionDetail`/`AnswerOptionDetail`.
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
