# Flirty – Backlog (GitHub-Issues)

Diese Datei ist die Vorlage zum Anlegen der GitHub-Issues. Jedes `###`-Item = ein Issue.
Reihenfolge = grobe MVP-Priorisierung. Architektur-Referenz: [ARCHITECTURE.md](./ARCHITECTURE.md).

## Labels

| Label | Bedeutung |
|---|---|
| `type:epic` | Übergeordnetes Sammel-Issue |
| `type:feature` | Funktionales Inkrement |
| `type:chore` | Infrastruktur/Setup |
| `type:test` | Test-Arbeit |
| `type:docs` | Dokumentation |
| `area:core` | Projekt `Flirty` |
| `area:api` | Projekt `Flirty.AspNetCore` |
| `area:designer` | Projekt `Flirty.Designer` |
| `area:samples` | Projekt `Flirty.Samples` |

## Meilensteine

- **M1 – MVP-Kern**: EPIC 0, 1, 2, 3, 5 (+ Console-Sample aus 8)
- **M2 – Web & Trigger**: EPIC 4, 6, Web-Sample aus 8
- **M3 – Designer**: EPIC 7
- **M4 – Qualität & Release**: EPIC 9, 10

> **Definition of Done (alle Issues):** Code + XML-Docs (Build bricht bei fehlender public-API-Doku, CS1591=Error) + Tests grün + relevanter `docs/`-Guide aktualisiert.

---

## EPIC 0 – Repo & Solution Bootstrap `type:epic` `type:chore`

### Repo-Grundgerüst & Build-Konventionen
`type:chore`
`git init` + `.gitignore` (VisualStudio/Rider), `.editorconfig`, `Directory.Build.props`
(net10, `Nullable=enable`, `ImplicitUsings=enable`, `TreatWarningsAsErrors`,
`GenerateDocumentationFile=true`, **CS1591 als Error**), `Directory.Packages.props`
(Central Package Management: Mediator (martinothamar), EF Core 10 + Provider, DynamicExpresso).
- **AC:** `dotnet build` grün; fehlende public-API-Doku bricht den Build.

### Projekt-Skelette + Solution-Verdrahtung
`type:chore`
6 Projekte anlegen und in `Flirty.sln` referenzieren:
`Flirty`, `Flirty.AspNetCore`→Flirty, `Flirty.Designer`→Flirty, `Flirty.Samples`→Flirty(+AspNetCore),
`Flirty.Tests`→Flirty, `Flirty.E2E` (standalone).
- **AC:** `dotnet build Flirty.sln` grün; `Flirty` referenziert **kein** ASP.NET.

### Mediator-Setup im Core
`type:chore` `area:core`
Mediator (martinothamar) registrieren (Source-Generator), Basis-`IPipelineBehavior`
(Logging/Validierung) als Skelett.
- **AC:** ein Dummy-Command läuft durch das Pipeline-Behavior.

### NuGet-Packaging vorbereiten
`type:chore` `area:core` `area:api`
Package-Metadaten für `Flirty` + `Flirty.AspNetCore` (Id, Autoren, Lizenz, README, Icon),
`IsPackable` nur für diese zwei, SourceLink, `IncludeSymbols`/`snupkg`, Versionierung (MinVer/Tag).
- **AC:** `dotnet pack` erzeugt `Flirty.*.nupkg` + `Flirty.AspNetCore.*.nupkg` (inkl. `.snupkg`).

### CI-Pipeline-Stub
`type:chore`
build + test + `dotnet pack` (GitHub Actions oder Azure Pipelines).
- **AC:** Pipeline grün, Artefakte = beide `.nupkg`.

---

## EPIC 1 – Domain & Persistenz `type:epic` `area:core`

### Domain-Entities + Enums
`type:feature` `area:core`
Dialog, Question, AnswerOption, Transition, **LoopDefinition**, TriggerDefinition,
DialogSession, SessionAnswer (inkl. `LoopInstanceId`/`IterationIndex`).

### FlirtyDbContext + Konfigurationen
`type:feature` `area:core`
DbContext, Keys, Indizes, JSON-Spalten (Value, ValidationRules, Trigger-Config).

### Provider SQLite / PostgreSQL / SQL Server + Migrationen
`type:feature` `area:core`
Provider-Anbindung + Migrationen je Provider.
- **AC:** DB wird gegen jeden der drei Provider erzeugt.

### Auto-Migration Hosted Service
`type:feature` `area:core`
`FlirtyMigrationHostedService` (aktiv bei `o.ApplyMigrations()`).

### IDialogStore Repository
`type:feature` `area:core`
Repository über `FlirtyDbContext`. `test` Store-Tests (SQLite in-memory).

---

## EPIC 2 – Expression-/Condition-Engine `type:epic` `area:core`

### IExpressionEvaluator + Kontext-Modell
`type:feature` `area:core`
Kontext: `answers`, Loop-Collections (`CollectionKey`), `iterationIndex`, `now`, `session`.

### DynamicExpresso-Implementierung (Sandbox)
`type:feature` `area:core`
Member-Whitelist, keine beliebige Code-Ausführung.

### Expression-Validierung / Compile-Check
`type:feature` `area:core`
Für Designer nutzbar (Fehler beim Speichern melden).
`test` Ausdrücke: Operatoren, UND/ODER, Fehlerfälle, Injection-Abwehr.

---

## EPIC 3 – Dialog-Runtime (Mediator-Commands) `type:epic` `area:core`

### StartDialogCommand + IFlirtyEngine-Facade
`type:feature` `area:core`
Start + Resume bestehender InProgress-Session.

### SubmitAnswerCommand
`type:feature` `area:core`
Validierung → Persistenz → Transition-Auswertung → nächste Frage/Completion → Notifications.

### ResumeDialogQuery
`type:feature` `area:core`
State + bisherige Antworten.

### EditAnswerCommand + Pfad-Neuberechnung
`type:feature` `area:core`
Zurückspringen, überschreiben, nachgelagerte Antworten neu berechnen/invalidieren.

### Loop-Runtime
`type:feature` `area:core`
Zyklus-Erkennung, Iterations-Zähler, Sammlung je Iteration in `CollectionKey`,
Break-Bedingung, danach normaler Fluss; Editieren innerhalb einer Iteration.
`test` mehrere Iterationen, Breaking Question, Collection im Kontext, Edit in Iteration.

### IAnswerValidator (Pipeline-Behavior)
`type:feature` `area:core`
Typ + `ValidationRules`. `test` Branching-, Resume-, Edit-, Validierungs-Tests.

---

## EPIC 4 – Trigger (Notifications + Webhooks) `type:epic` `area:core`

### Notification-Contracts + Publikation
`type:feature` `area:core`
`DialogStartedNotification`, `AnswerSubmittedNotification`, `QuestionAnsweredNotification`,
`DialogCompletedNotification`; Publikation aus den Command-Handlern.

### Convenience für In-Process-Handler
`type:feature` `area:core`
Doku + Helper zum „Reinhängen" eigener `INotificationHandler<T>` (Console-Szenario).

### Webhook-Handler
`type:feature` `area:core`
Eingebauter `INotificationHandler` (IHttpClientFactory + Retry/Timeout, `TriggerDefinition`-getrieben).
`test` Dispatch + Webhook (Mock-HttpMessageHandler).

---

## EPIC 5 – DI-Extensions & Options `type:epic` `area:core`

### AddFlirty(...) Extension-Method
`type:feature` `area:core`
Mediator-Registrierung, Provider-Wahl, `ApplyMigrations`, Webhook-Registrierung,
`UseExpressionEvaluator`.
`test` Registrierungs-/Resolve-Tests inkl. reinem Console-Setup ohne ASP.NET.

---

## EPIC 6 – WebAPI-Endpunkte (`Flirty.AspNetCore`) `type:epic` `area:api`

### Projekt + MapFlirtyEndpoints + DTOs
`type:feature` `area:api`
`FrameworkReference Microsoft.AspNetCore.App`; `MapFlirtyEndpoints` sendet Mediator-Commands;
DTOs für Start/Resume/Answer/Edit.

### Optionale Admin-CRUD-Endpunkte
`type:feature` `area:api`
Dialoge/Fragen/Optionen/Transitions. `test` Integrationstests (`WebApplicationFactory`).

---

## EPIC 7 – Designer (Blazor) `type:epic` `area:designer`

### Connection-Profil-Verwaltung (Multi-DB)
`type:feature` `area:designer`
Mehrere Profile, Test-Connection, Migrate-Button, `IDbContextFactory`-Auswahl.

### Dialog-CRUD-UI
`type:feature` `area:designer`

### Frage-Editor
`type:feature` `area:designer`
Typ, Reihenfolge, Validierung, Antwortoptionen.

### Branching-Editor
`type:feature` `area:designer`
Transitions + Expression-Builder + Live-Validierung via `IExpressionEvaluator`.

### Loop-Visualisierung
`type:feature` `area:designer`
Zyklus als Loop-Block, Breaking Question markieren, `CollectionKey` bearbeiten;
Warnung bei Zyklus ohne erreichbare Exit-Bedingung (Endlosschleife).

### Trigger-Editor
`type:feature` `area:designer`

### Test-Runner
`type:feature` `area:designer`
Dialog im Designer durchspielen (inkl. Loop-Iterationen).
- **AC:** nicht-technischer Nutzer kann einen Dialog inkl. Schleife end-to-end anlegen.

---

## EPIC 8 – Sample-Apps `type:epic` `area:samples`

### Console-Single-Project-Sample
`type:feature` `area:samples`
Nur Core + eigener `INotificationHandler`; Dialog per Facade durchspielen (kein ASP.NET).

### Web-Sample (Minimal-API + Chat-UI)
`type:feature` `area:samples`
Konsumiert die Endpunkte; zeigt Resume/Edit/Branching/**Loop über Liste**/Trigger;
Beispiel-Handler + Webhook-Empfänger.

---

## EPIC 9 – Tests, CI & Publish `type:epic` `type:test`

### Playwright-E2E Designer
`type:test`
Dialog anlegen → Branching → Loop → speichern.

### Playwright-E2E Web-Sample
`type:test`
Branching + Loop durchspielen, Reload→Resume, vorherige Antwort editieren.

### Coverage-Report in CI
`type:chore`

### NuGet-Publish
`type:chore` `area:core` `area:api`
`dotnet pack` + Push (Feed konfigurierbar: NuGet.org oder Azure Artifacts).
- **AC:** beide Packages inkl. Symbols veröffentlicht.

---

## EPIC 10 – Doku-Guides & ADRs `type:epic` `type:docs`

### docs/-Guides
`type:docs`
`ARCHITECTURE.md`, `GETTING-STARTED-Console.md`, `GETTING-STARTED-WebApi.md`, `DESIGNER.md`,
`BRANCHING-EXPRESSIONS.md`, `LOOPS.md`, `TRIGGERS.md`, `NUGET-PACKAGING.md`.

### ADRs
`type:docs`
`docs/adr/`: Mediator, ASP.NET-freier Core, Expression-Engine, Migrationen pro Provider.

### Root-README (Quickstart)
`type:docs`
Console + Web Quickstart, Snippets aus den Samples.
