# CLAUDE.md

Projektkontext für Claude Code. Diese Datei **überschreibt** die generischen globalen Vorgaben
(`~/.claude/CLAUDE.md`, `PRINCIPLES.md`): Flirty ist ein reines **.NET-10**-Projekt – `pnpm`, `ng`,
`tsc` und der Node-Workflow sind hier **irrelevant**. Es gilt der `dotnet`-Workflow unten.

## Was ist Flirty?

Wiederverwendbare **Chatbot-/Dialog-Engine für .NET**. Die Host-App baut nur die UI; Flirty übernimmt
Persistenz, Antwort-Validierung, **Branching**, **Loops**, **Resume**, editierbare Antworten und
**Trigger** (In-Process-Notifications + Outbound-Webhooks). Dialoge werden über einen Blazor-**Designer**
konfiguriert. Integration erfolgt über `services.AddFlirty(o => …)` und optional das Paket
`Flirty.AspNetCore` (`app.MapFlirtyEndpoints()`). Repo: `github.com/dominikz98/flirty`.

Ausführliche Doku liegt in `docs/` (siehe Wegweiser unten) – **die eigentliche Tiefe steckt dort**,
nicht in dieser Datei und nicht in den GitHub-Issues (die sind nur Backlog-Index).

## Solution-Layout (`Flirty.sln`, 10 Projekte)

```
src/
├─ Flirty                     Core-Engine. REINE Class-Library, KEIN ASP.NET. NuGet-Paket.
│                               Domain, Persistence (EF Core), Runtime (Mediator), Expressions,
│                               Validation, Pipeline, Hosting, DependencyInjection.
├─ Flirty.AspNetCore          OPTIONAL: WebAPI-Endpunkte (dünn über die Mediator-Commands). NuGet-Paket.
├─ Flirty.Designer            Blazor Web App (Server-interaktiv). AKTUELL NUR SKELETT (Default-Template).
├─ Flirty.Migrations.Sqlite       \
├─ Flirty.Migrations.PostgreSql    } EF-Migrationen pro Provider. IsPackable=false, DLLs ins Flirty-Paket gebündelt.
└─ Flirty.Migrations.SqlServer    /
   Flirty.Samples             Lauffähiges Console-Sample (nur Core, kein ASP.NET).
tests/
├─ Flirty.Tests               xUnit Unit-/Integrationstests.
└─ Flirty.E2E                 Playwright-E2E. AKTUELL NUR SKELETT.
```

**Invariante:** Der Core (`Flirty`) hat **keine** ASP.NET-Abhängigkeit und läuft unverändert in
Console/Worker. Web ist opt-in via `Flirty.AspNetCore` (`FrameworkReference Microsoft.AspNetCore.App`).

## Harte Build-Konventionen (Do / Don't)

Zentral in `Directory.Build.props`, `Directory.Build.targets`, `Directory.Packages.props`, `global.json`.

- **Target:** `net10.0`, `LangVersion=latest`, `Nullable=enable`, `ImplicitUsings=enable`. SDK
  `10.0.204` (`global.json`, `rollForward=latestFeature`).
- **`TreatWarningsAsErrors=true` repo-weit.** Auch NuGet-Pack-Warnungen (NU5xxx) und Security-Advisories
  (NU1903) brechen den Build. Neue transitive Pakete dürfen keine Advisories einschleppen.
- **XML-Docs Pflicht.** `GenerateDocumentationFile=true`; für **packbare** Projekte (`Flirty`,
  `Flirty.AspNetCore`) ist **CS1591 ein Fehler** → jede public API braucht einen **deutschen**
  XML-Doc-Kommentar. Apps/Tests/Designer sind davon per `NoWarn` befreit.
- **Central Package Management:** Versionen **nur** in `Directory.Packages.props` pinnen, im `.csproj`
  `<PackageReference Include="…" />` **ohne** `Version`.
- **Packaging:** Nur `Flirty` + `Flirty.AspNetCore` setzen `IsPackable=true`; alle anderen erben
  `false`. Paketversion ist **datumsbasiert** `JJJJMM.Revision` (z. B. `202607.1`); nie manuell
  hochzählen. Details: `docs/NUGET-PACKAGING.md`.
- **Konvention:** Neue Domänen-/Runtime-Typen möglichst `sealed record`/`sealed class`, `internal` wo
  nicht Teil der öffentlichen API. Timestamps immer **UTC** (`DateTimeOffset`, `UtcNow`).

## Architektur-Invarianten

- **CQRS via Mediator (martinothamar, Source-Generator).** Engine-Operationen = Commands/Queries,
  Trigger = `INotification`, Cross-Cutting = `IPipelineBehavior`. **Der Source-Generator entdeckt
  Handler nur in der Core-Compilation** → alle Commands/Queries/Handler/Notification-Contracts **und**
  der `AddMediator`-Aufruf liegen in `Flirty`. Offen-generische Behaviors werden **manuell** registriert.
- **ASP.NET-frei im Core** (siehe oben).
- **Expression-Engine gesandboxt:** Branching-Bedingungen laufen über `IExpressionEvaluator` (Default
  `DynamicExpresso`, Member-Whitelist) – **kein** roher C#-`eval`. Im Designer werden Ausdrücke beim
  Speichern kompiliert/validiert (`Validate`).
- **Migrationen pro Provider:** getrennte Assemblies `Flirty.Migrations.{Sqlite,PostgreSql,SqlServer}`,
  zur Laufzeit per `MigrationsAssembly(...)` gewählt. Grund: ADR `docs/adr/0001-migrationen-pro-provider.md`.
- **Loops = Branching + Marker** (`LoopDefinition`), kein Runtime-Sonderpfad.
- **Dialog-Versionierung:** Sessions pinnen `DialogVersion`/`DialogId` → das Editieren publizierter
  Dialoge bricht laufende Sessions nicht.

## Zentrale Einstiegspunkte (mit Pfaden)

- **DI:** `AddFlirty()`, `AddFlirty(Action<FlirtyOptions>)`, `AddFlirtyHandler<TNotification, THandler>()`
  in `src/Flirty/DependencyInjection/FlirtyServiceCollectionExtensions.cs` (Namespace bewusst
  `Microsoft.Extensions.DependencyInjection`). Optionen in `FlirtyOptions.cs`.
- **Runtime-Facade:** `IFlirtyEngine` (`src/Flirty/Runtime/IFlirtyEngine.cs`) → `StartDialogAsync`,
  `SubmitAnswerAsync`, `ResumeDialogAsync`, `EditAnswerAsync`. Dahinter Commands/Queries in
  `src/Flirty/Runtime/` (`StartDialogCommand.cs`, `SubmitAnswerCommand.cs`, …), gemeinsamer
  `TransitionResolver.cs`.
- **Admin-CRUD:** Commands/Queries in `src/Flirty/Runtime/Admin/`, Repository
  `src/Flirty/Persistence/IDialogAdminStore.cs`.
- **Web-Endpunkte:** `src/Flirty.AspNetCore/FlirtyEndpointRouteBuilderExtensions.cs` (Runtime) und
  `FlirtyAdminEndpointRouteBuilderExtensions.cs` (Admin), Fehler-Mapping in
  `FlirtyExceptionEndpointFilter.cs` (Namespace `Microsoft.AspNetCore.Builder`).

## Standard-Befehle (pwsh)

```pwsh
dotnet build Flirty.sln
dotnet test                                   # oder: dotnet test tests/Flirty.Tests
dotnet pack -c Release -o artifacts           # nur Flirty + Flirty.AspNetCore
dotnet pack -c Release -p:BuildRevision=7     # Paketrevision setzen -> Flirty.202607.7.nupkg
dotnet tool restore                           # einmalig, für dotnet ef (lokales Tool 10.0.9)
```

## Test-Konventionen (`tests/Flirty.Tests`)

- **xUnit v2** (`2.9.3`). Kein Mocking-Framework – TestDoubles von Hand (Spy/Recording).
- **SQLite in-memory als Default:** offene `SqliteConnection("DataSource=:memory:")` + `EnsureCreated()`,
  separate Seed-/Read-Contexts. Facade-Tests bauen den echten DI-Stack via `AddFlirty()`.
- **Endpunkt-Tests** über `FlirtyTestHost` (In-Process `TestServer`, Docker-frei).
- **PostgreSQL/SQL Server** via Testcontainers → brauchen Docker; ohne Docker per `[SkippableFact]` +
  `Skip.IfNot(DockerAvailability.IsAvailable, …)` sauber übersprungen.
- **Testnamen deutsch**, snake_case-artig (`StartDialogAsync_startet_Dialog_ueber_die_Facade`).
- Core exponiert Interna an Tests via `[assembly: InternalsVisibleTo("Flirty.Tests")]`.

## Konventionen / Overrides gegenüber den Globals

- **Sprache: Deutsch.** Code-Kommentare, XML-Docs, Commit-Messages und Testnamen sind durchgängig
  deutsch – beibehalten (Umlaute korrekt: ä/ö/ü/ß, kein ASCII-Ersatz).
- **Shell:** `pwsh`. **Paketmanager/Build:** `dotnet` (nicht `pnpm`/`ng`/`tsc`).
- **Git:** Branches `feature/dz/<issue>` bzw. `bugfix/dz/<issue>`. PRs via `gh` CLI (das GitHub-MCP-Token
  kann in diesem Repo keine PRs erstellen).
- **Definition of Done** je Änderung: Code + deutsche XML-Docs (CS1591) + grüne Tests + passender
  `docs/`-Guide aktualisiert.

## Skills für wiederkehrende Aufgaben

Unter `.claude/skills/` liegen funktionsspezifische Anleitungen – bei passender Aufgabe zuerst prüfen:
`flirty-runtime-command`, `flirty-ef-migration`, `flirty-trigger-notification`, `flirty-question-type`,
`flirty-nuget-package`, `flirty-designer`.

## Doku-Wegweiser (`docs/`)

| Thema | Datei |
|---|---|
| Architektur-Gesamtbild | `docs/ARCHITECTURE.md` |
| Domänenmodell & EF-Konfiguration | `docs/DOMAIN-MODEL.md` |
| Runtime (Start/Resume/Submit/Edit) | `docs/RUNTIME.md` |
| Persistenz & Migrationen | `docs/PERSISTENCE.md` |
| Mediator-Setup | `docs/MEDIATOR.md` |
| Branching / Expressions | `docs/BRANCHING-EXPRESSIONS.md` |
| Loops | `docs/LOOPS.md` |
| Trigger (Notifications + Webhooks) | `docs/TRIGGERS.md` |
| Antwort-Validierung | `docs/VALIDATION.md` |
| NuGet-Packaging | `docs/NUGET-PACKAGING.md` |
| CI-Pipeline | `docs/CI.md` |
| Getting Started (Console / Web) | `docs/GETTING-STARTED-Console.md`, `docs/GETTING-STARTED-WebApi.md` |
| Backlog / Roadmap | `docs/BACKLOG.md`, `docs/ROADMAP.md` |
| Entscheidungen | `docs/adr/` |

## Stand & offene Baustellen

**Fertig (M1+M2):** Domain, Persistenz (3 Provider), Expression-Engine, Runtime (Start/Resume/Submit/Edit,
Loops, Validierung), Trigger (Notifications + Webhooks), DI-Fassade, WebAPI-Endpunkte (Runtime + Admin-CRUD),
Console-Sample.

**Offen:** Blazor-**Designer** (Issues #37–#43, `src/Flirty.Designer` ist erst ein Template),
E2E-Tests (#46/#47), Coverage in CI (#48), NuGet-**Publish** (#49), Web-Sample (#45), Doku-Guides/ADRs/
README-Ausbau (#50–#52; u. a. fehlt noch `docs/DESIGNER.md`). Beim Arbeiten also nicht von Vollständigkeit
des Designers/E2E ausgehen.
