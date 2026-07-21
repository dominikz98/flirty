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

## Solution-Layout (`Flirty.sln`, 11 Projekte)

```
src/
├─ Flirty                     Core-Engine. REINE Class-Library, KEIN ASP.NET. NuGet-Paket.
│                               Domain, Persistence (EF Core), Runtime (Mediator), Expressions,
│                               Validation, Pipeline, Hosting, DependencyInjection.
├─ Flirty.AspNetCore          OPTIONAL: WebAPI-Endpunkte (dünn über die Mediator-Commands). NuGet-Paket.
├─ Flirty.Designer            Blazor Web App (Server-interaktiv). Bisher: Connection-Profil-Verwaltung
│                               (Multi-DB, #37) + Dialog-CRUD (#38); Frage-/…-Editoren (#39–#43) offen.
├─ Flirty.Migrations.Sqlite       \
├─ Flirty.Migrations.PostgreSql    } EF-Migrationen pro Provider. IsPackable=false, DLLs ins Flirty-Paket gebündelt.
└─ Flirty.Migrations.SqlServer    /
   Flirty.Samples             Lauffähiges Console-Sample (nur Core, kein ASP.NET).
   Flirty.Samples.Web         Lauffähiges Web-Sample: Minimal-API + statische Chat-UI (nutzt
                                Flirty.AspNetCore); Resume/Edit/Branching/Loop/Trigger + Webhook-Empfänger.
tests/
├─ Flirty.Tests               xUnit Unit-/Integrationstests.
└─ Flirty.E2E                 Playwright-E2E der Web-Sample-Chat-UI (#45/#47).
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
  `docs/`-Guide aktualisiert + Kontext/Skills mitgepflegt (siehe nächster Abschnitt).

## Skills für wiederkehrende Aufgaben

Unter `.claude/skills/` liegen funktionsspezifische Anleitungen – bei passender Aufgabe zuerst prüfen:
`flirty-runtime-command`, `flirty-ef-migration`, `flirty-trigger-notification`, `flirty-question-type`,
`flirty-nuget-package`, `flirty-designer`.

## Kontext & Doku mitpflegen (wichtig)

Diese Datei, die Skills und `docs/` aktualisieren sich **nicht** von selbst – sie sind Teil der Aufgabe.
Wer Code ändert, zieht die betroffene Doku im **selben** PR mit. Konkret:

- **Neues Muster / neuer Erweiterungspfad** umgesetzt → passenden Skill unter `.claude/skills/`
  anlegen/aktualisieren (Pfade, Schritte, DoD aktuell halten).
- **Neue/geänderte public API, Konvention, Abhängigkeit oder Befehl** → die betroffenen Abschnitte hier
  in `CLAUDE.md` und den zuständigen `docs/`-Guide anpassen (siehe Doku-Wegweiser unten).
- **Feature abgeschlossen / Projektstatus verschoben** → Abschnitt „Stand & offene Baustellen" unten
  nachziehen (Issue-Nummern, „AKTUELL NUR SKELETT"-Hinweise, fehlende Guides).
- **Faustregel:** Wenn eine Aussage in `CLAUDE.md`/einem Skill/`docs/` durch deine Änderung *falsch*
  würde, korrigiere sie jetzt – veralteter Kontext ist schlimmer als keiner.

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
| Getting Started (Console / WebAPI) | `docs/GETTING-STARTED-Console.md`, `docs/GETTING-STARTED-WebApi.md` |
| Getting Started (Web-Sample / Chat-UI) | `docs/GETTING-STARTED-Sample-Web.md` |
| Designer (Blazor) | `docs/DESIGNER.md` |
| Backlog / Roadmap | `docs/BACKLOG.md`, `docs/ROADMAP.md` |
| Entscheidungen | `docs/adr/` |

## Stand & offene Baustellen

**Fertig (M1+M2):** Domain, Persistenz (3 Provider), Expression-Engine, Runtime (Start/Resume/Submit/Edit,
Loops, Validierung), Trigger (Notifications + Webhooks), DI-Fassade, WebAPI-Endpunkte (Runtime + Admin-CRUD),
Console-Sample, **Web-Sample** (Minimal-API + Chat-UI, #45) inkl. Playwright-E2E der Chat-UI (#47).

**Designer (M3):** **Connection-Profil-Verwaltung (Multi-DB, #37) fertig** – Profile (JSON, gitignored),
Test-Connection, Migrate, `IDbContextFactory`-Auswahl gegen das aktive Profil. Dafür neues öffentliches
Core-API `FlirtyDatabaseProvider` + `DbContextOptionsBuilder.UseFlirtyProvider(...)` (zentralisiert das
Provider→MigrationsAssembly-Mapping); Guide `docs/DESIGNER.md` angelegt.
**Dialog-CRUD (#38) fertig** – Liste `/dialogs` + Editor `/dialogs/{id}` (Metadaten, Einstiegsfrage,
Publish/Unpublish, Löschen). Alle Admin-Operationen laufen über `FlirtyAdminGateway`
(`src/Flirty.Designer/Services/`), das **jede** Nachricht in einem frischen DI-Scope sendet – in Blazor
Server lebt ein Scope sonst den ganzen Circuit und pinnt den `FlirtyDbContext` an das zuerst benutzte
Profil. Folge-Editoren nutzen dieses Gateway ebenfalls. Gemeinsame UI-Klassen liegen global in
`src/Flirty.Designer/wwwroot/app.css`.

**Offen:** restlicher Blazor-**Designer** (Editoren #39–#43, es gibt außer Verbindungen und Dialog-CRUD
noch keine UI für Fragen/Branching/Loops/Trigger), Designer-E2E (#46), Coverage in CI (#48),
NuGet-**Publish** (#49), Doku-/README-Ausbau (#50–#52). Beim Arbeiten also nicht von Vollständigkeit des
Designers ausgehen.
