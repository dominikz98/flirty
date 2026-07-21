---
name: flirty-designer
description: Den Blazor-Designer (Flirty.Designer) aufbauen oder erweitern – Dialog-/Frage-/Antwort-/Branching-/Loop-/Trigger-Konfiguration, Multi-DB-Connection-Profile. Verwenden bei "Designer", "Blazor-UI für Dialoge", "Dialog-Editor", "Branching-Editor", "Connection-Profil", "EPIC 7", Issues #37–#43.
---

# Blazor-Designer aufbauen / erweitern

> **Status: teils umgesetzt (EPIC 7, Issues #37–#43).** Die **Connection-Profil-Verwaltung (#37)** ist
> fertig; `docs/DESIGNER.md` existiert. Die eigentlichen Editoren (Dialog-CRUD #38 … Test-Runner #43)
> sind **noch offen**. Dieser Skill ist die **Leitplanke** für den weiteren Aufbau: die beabsichtigte
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
  `JsonConnectionProfileStore` (JSON im ContentRoot, gitignored), `ActiveConnectionProfile` (Scoped),
  `ConnectionProfileOperations` (Test-Connection/Migrate), `ConnectionProfileContextBuilder`; UI unter
  `Components/Pages/ConnectionProfiles.razor` (`/connections`) + `Components/Layout/NavMenu.razor`.

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

2. **Multi-DB per Connection-Profil (#37) — UMGESETZT.** Provider + ConnectionString als Profile lokal
   verwaltet; zur Laufzeit über `IDbContextFactory<FlirtyDbContext>` (Impl. `FlirtyDesignerDbContextFactory`)
   gegen das aktive Profil geöffnet. Das Provider→`MigrationsAssembly`-Mapping liefert das öffentliche
   Core-API `FlirtyDatabaseProvider` + `DbContextOptionsBuilder.UseFlirtyProvider(...)` (Details:
   `docs/DESIGNER.md`, `docs/PERSISTENCE.md`). Nicht duplizieren – dieses API wiederverwenden.

3. **Ausdrücke beim Speichern validieren (#40/#42).** Branching-Bedingungen und Trigger-Ausdrücke über
   `IExpressionEvaluator.Validate(...)` kompilieren/prüfen, bevor gespeichert wird – die Engine ist
   gesandboxt (kein `eval`), siehe `docs/BRANCHING-EXPRESSIONS.md`.

4. **Loops sind Branching + Marker (#41).** Ein Zyklus entsteht durch eine `Transition` auf eine frühere
   Frage; `LoopDefinition` (CollectionKey/Entry/Breaking) macht ihn sichtbar. Im Designer den Zyklus als
   Loop-Block mit markierter Breaking Question zeichnen und bei einem Zyklus **ohne erreichbare
   Exit-Bedingung warnen** (Endlosschleifen-Risiko). Siehe `docs/LOOPS.md`.

5. **Test-Runner (#43):** ein Dialog-Durchlauf im Designer über `IFlirtyEngine` gegen das gewählte Profil.

## Empfohlene Aufbaureihenfolge (EPIC 7)

#37 Connection-Profile ✅ → #38 Dialog-CRUD-UI → #39 Frage-Editor → #40 Branching-Editor →
#41 Loop-Visualisierung → #42 Trigger-Editor → #43 Test-Runner.

## Konventionen

- Blazor-Komponenten unter `Components/` (Pages in `Components/Pages/`), Server-interaktiver Render-Mode
  beibehalten.
- UI-Texte und Doku **deutsch**. Der Designer ist `IsPackable=false` (kein NuGet-Paket) → CS1591 ist
  hier **kein** Fehler, XML-Docs sind optional.
- E2E-Tests des Designers gehören nach `tests/Flirty.E2E` (Playwright, #46) – aktuell noch Skelett.

## Definition of Done

Feature funktioniert im Server-interaktiven Designer über die Admin-Commands · Ausdrücke werden beim
Speichern validiert · `docs/DESIGNER.md` (seit #37 vorhanden) beim jeweiligen Feature erweitern · ggf.
Playwright-E2E (#46).

## Verifikation

```pwsh
dotnet build Flirty.sln
dotnet run --project src/Flirty.Designer     # Designer lokal starten
dotnet test tests/Flirty.Tests
```
