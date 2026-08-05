# CLAUDE.md

Project context for Claude Code. This file **overrides** the generic global defaults
(`~/.claude/CLAUDE.md`, `PRINCIPLES.md`): Flirty is a pure **.NET 10** project – `pnpm`, `ng`,
`tsc` and the Node workflow are **irrelevant** here. The `dotnet` workflow below applies.

## What is Flirty?

A reusable **chatbot/dialog engine for .NET**. The host app only builds the UI; Flirty handles
persistence, answer validation, **branching**, **loops**, **resume**, editable answers and
**triggers** (in-process notifications + outbound webhooks). Dialogs are configured via a Blazor
**designer**. Integration is through `services.AddFlirty(o => …)` and optionally the package
`Flirty.AspNetCore` (`app.MapFlirtyEndpoints()`). Repo: `github.com/dominikz98/flirty`.

Detailed docs live in `docs/` (see the guide below) – **the actual depth is there**, not in this file
and not in the GitHub issues (those are only a backlog index).

## Solution layout (`Flirty.sln`, 11 projects)

```
src/
├─ Flirty                     Core engine. PURE class library, NO ASP.NET. NuGet package.
│                               Domain, Persistence (EF Core), Runtime (Mediator), Expressions,
│                               Validation, Pipeline, Hosting, DependencyInjection.
├─ Flirty.AspNetCore          OPTIONAL: WebAPI endpoints (thin over the Mediator commands). NuGet package.
├─ Flirty.Mcp                 OPTIONAL: MCP server over Streamable HTTP (MapFlirtyMcp). NuGet package.
│                               EPIC 13 complete (#126–#130): host, 36 tools in ten tool
│                               classes – 27 configuration + the 5 flirty_session_* of FlirtySessionTools,
│                               each class mirroring one MapXxxEndpoints counterpart, plus the 4
│                               flirty_db_* of the two database classes, which mirror the designer's
│                               ConnectionProfileOperations because the engine has no command for them.
│                               Plus Tools/FlirtyToolNames.cs as the name checklist, ServerInstructions
│                               and two call-tool filters: the error mapping (mirrors
│                               FlirtyExceptionEndpointFilter) and, inside it, the target resolution.
│                               References Flirty ONLY. Guide: docs/MCP.md, skill: flirty-mcp.
├─ Flirty.Designer            Blazor Web App (server-interactive). EPIC 7 complete: connection-profile
│                               management (multi-DB, #37), dialog CRUD (#38), question editor (#39),
│                               branching editor (#40), loop editor (#41), trigger editor (#42),
│                               test runner (#43) and Playwright E2E of the UI (#46). Plus from EPIC 11
│                               the graph canvas (SVG): reading (#101), layout persistence (#102),
│                               editing by gesture (#103) and the test run in the graph (#104).
│                               Composition in
│                               `DesignerApp.cs` (Program.cs only calls ConfigureServices/Configure).
├─ Flirty.Migrations.Sqlite       \
├─ Flirty.Migrations.PostgreSql    } EF migrations per provider. IsPackable=false, DLLs bundled into the Flirty package.
└─ Flirty.Migrations.SqlServer    /
   Flirty.Samples             Runnable console sample (core only, no ASP.NET).
   Flirty.Samples.Web         Runnable web sample: minimal API + static chat UI (uses
                                Flirty.AspNetCore and Flirty.Mcp); resume/edit/branching/loop/trigger,
                                webhook receiver, MCP server at /mcp.
tests/
├─ Flirty.Tests               xUnit unit/integration tests.
└─ Flirty.E2E                 Playwright E2E of the web-sample chat UI (#45/#47) and the designer (#46).
```

**Invariant:** The core (`Flirty`) has **no** ASP.NET dependency and runs unchanged in
console/worker. Web is opt-in via `Flirty.AspNetCore` (`FrameworkReference Microsoft.AspNetCore.App`).
The same holds one layer out: MCP is opt-in via `Flirty.Mcp`, which references `Flirty` **only** – the two
web packages sit beside each other, not on top of each other, so `Flirty.AspNetCore` never drags the MCP
SDK along. Reason: ADR `docs/adr/0009-mcp-as-its-own-opt-in-package.md`.

## Hard build conventions (Do / Don't)

Centralized in `Directory.Build.props`, `Directory.Build.targets`, `Directory.Packages.props`, `global.json`.

- **Target:** `net10.0`, `LangVersion=latest`, `Nullable=enable`, `ImplicitUsings=enable`. SDK **lower
  bound** `10.0.100` (`global.json`, `rollForward=latestFeature`) – any installed 10.0.x SDK from the
  GA band onward is taken. Keep the bound deliberately low: too high a pin simply makes the build fail
  on machines with an older (but sufficient) SDK.
- **`TreatWarningsAsErrors=true` repo-wide.** NuGet pack warnings (NU5xxx) and security advisories
  (NU1903) break the build too. New transitive packages must not drag in advisories.
- **XML docs mandatory.** `GenerateDocumentationFile=true`; for **packable** projects (`Flirty`,
  `Flirty.AspNetCore`) **CS1591 is an error** → every public API needs an **English** XML doc comment.
  Apps/tests/designer are exempted via `NoWarn`.
- **Central Package Management:** pin versions **only** in `Directory.Packages.props`, in the `.csproj`
  `<PackageReference Include="…" />` **without** `Version`.
- **Packaging:** only `Flirty` + `Flirty.AspNetCore` + `Flirty.Mcp` set `IsPackable=true`; all others
  inherit `false`. The package version is **date-based** `YYYYMM.Revision` (e.g. `202607.1`); never bump it
  manually. Details: `docs/NUGET-PACKAGING.md`.
- **A new packable project touches six places, and each fails silently when forgotten:** `Flirty.sln`,
  `Directory.Packages.props`, `coverage.runsettings` (`<Include>` – otherwise unmeasured, no warning),
  `.github/workflows/release.yml` (the *Verify packages* pairs – otherwise it ships unpacked),
  `tests/Flirty.Tests/Flirty.Tests.csproj`, and the project count in this file's § Solution layout.
- **Convention:** new domain/runtime types preferably `sealed record`/`sealed class`, `internal` where
  not part of the public API. Timestamps always **UTC** (`DateTimeOffset`, `UtcNow`).

## Architecture invariants

- **CQRS via Mediator (martinothamar, source generator).** Engine operations = commands/queries,
  triggers = `INotification`, cross-cutting = `IPipelineBehavior`. **The source generator only discovers
  handlers in the core compilation** → all commands/queries/handlers/notification contracts **and**
  the `AddMediator` call live in `Flirty`. Open-generic behaviors are registered **manually**.
  Reason: ADR `docs/adr/0002-mediator-as-in-process-bus.md`.
- **ASP.NET-free in the core** (see above). Reason: ADR `docs/adr/0003-aspnet-free-core.md`.
- **The MCP transport runs stateless, unconditionally** (`WithHttpTransport(t => t.Stateless = true)`).
  Protocol revision `2026-07-28` removed the `initialize` handshake and the `Mcp-Session-Id` header, so a
  stateful server **refuses** current clients with `-32022`. In stateless mode the SDK resolves a
  `tools/call`'s scoped services from the **ASP.NET request scope** – that is why `Flirty.Mcp` needs no
  gateway and no `IServiceScopeFactory` (the designer's `DesignerGateway` exists only because a Blazor
  circuit scope lives forever). Error mapping is **one** `AddCallToolFilter`, not one `try` per tool: it is
  composed inside the SDK's own try/catch, so it sees the raw exception first – without it the SDK swallows
  every message as `"An error occurred invoking 'x'."`. Details: `docs/MCP.md`.
- **The MCP database target comes from the route, and is captured in `ConfigureSessionOptions`** (#129).
  A host declares targets by name (`o.AddTarget(...)`), a client picks one by connecting to
  `/mcp/{target}`; there is no `select_target` tool and no `target` argument, because revision
  `2026-07-28` left no session to hold a selection in. The seam is the transport's session callback,
  which **in stateless mode the SDK invokes per HTTP request** – so it fires *only* on an MCP request,
  which is what makes "declaring a target does not repoint `MapFlirtyEndpoints`" structural rather than
  careful. Concretely: only the `FlirtyDbContext` registration is replaced, only when a target is
  declared, and `DbContextOptions<FlirtyDbContext>` stays as the fallback. Naming an undeclared target is
  a 400, never a silent fallback – on a single-database server too. Reason: ADR
  `docs/adr/0010-mcp-database-targets-by-route.md`.
- **Expression engine sandboxed:** branching conditions run via `IExpressionEvaluator` (default
  `DynamicExpresso`, member whitelist) – **no** raw C# `eval`. In the designer, expressions are
  compiled/validated on save (`Validate`). Reason: ADR
  `docs/adr/0004-sandboxed-expression-engine.md`.
- **Migrations per provider:** separate assemblies `Flirty.Migrations.{Sqlite,PostgreSql,SqlServer}`,
  chosen at runtime via `MigrationsAssembly(...)`. Reason: ADR
  `docs/adr/0001-migrations-per-provider.md`.
- **Loops = branching + marker** (`LoopDefinition`), no runtime special path.
- **Triggers from two sources:** outbound webhooks come from `o.AddWebhook(scope, url, expression?)`
  **and** (since #42) from the `TriggerDefinition`s configured on the dialog; both are served by the same
  `WebhookNotificationHandler`. `Kind = InProcess` deliberately delivers nothing (the host app plugs its
  `INotificationHandler<T>` in). Everything there is **best-effort**: configuration, expression and
  delivery errors are logged, never thrown – a trigger must not break start/submit/edit.
- **Dialog versioning:** sessions pin `DialogVersion`/`DialogId`. For that to hold, a **published**
  version is immutable: graph changes (questions, options, transitions, loops, triggers, entry question)
  throw `DialogPublishedException` → 409 (`DialogEditGuard` as the first precondition of the 15 graph
  commands). It is evolved via `CreateDialogVersionCommand` (clone as a draft, `Version`+1);
  `PublishDialogCommand` retires the previously productive version. Deletion happens only without running
  sessions (`AbandonDialogSessionsCommand` ends them first). Reason: ADR
  `docs/adr/0005-immutable-published-dialog-version.md`.
- **Canvas positions live outside the graph:** `DialogLayout` (its own table, FK-free
  `ElementId`, unique over `(DialogId, ElementKind, ElementId)`) carries the designer's arrangement.
  `Set/ResetDialogLayoutCommand` run **deliberately without `DialogEditGuard`** – a published dialog
  must remain arrangeable, and coordinates do not touch session semantics. This is the only
  exception to the publish lock and not a gap, but its edge. The runtime never reads the table
  (`IDialogStore` does not load `Layout`). The price: cloning (`CreateDialogVersionCommand`) and cleanup
  (`DeleteQuestionCommand`) are manual work. Reason: ADR `docs/adr/0007-layout-as-its-own-table.md`.
  Since #127 the MCP surface carries the same exception: `flirty_layout_set`/`_reset` are the only two
  tools without `DialogEditGuard` behind them, both say so in their `[Description]`, and the pair hangs
  in **one** test (layout on a published dialog succeeds *and* a graph change on it returns 409) –
  written apart, the first half would claim "layout works" without showing that the lock holds.
- **Canvas gestures write through the existing admin commands** (#103): a palette drop is
  `CreateQuestionCommand` + `SetDialogLayoutCommand`, a connection `CreateTransitionCommand` – all in
  **one** gateway call. No canvas CRUD, no new core command. After every graph mutation there is a
  **reload** (the warnings arise graph-wide); gestures are **not idempotent** and therefore locked in
  two stages (JS `send()` on the promise of `invokeMethodAsync` + early exit in
  `RunGestureAsync`). Reason: ADR `docs/adr/0008-gestures-on-the-canvas.md`.

## Central entry points (with paths)

- **DI:** `AddFlirty()`, `AddFlirty(Action<FlirtyOptions>)`, `AddFlirtyHandler<TNotification, THandler>()`
  in `src/Flirty/DependencyInjection/FlirtyServiceCollectionExtensions.cs` (namespace deliberately
  `Microsoft.Extensions.DependencyInjection`). Options in `FlirtyOptions.cs`.
- **Runtime facade:** `IFlirtyEngine` (`src/Flirty/Runtime/IFlirtyEngine.cs`) → `StartDialogAsync`,
  `StartDialogVersionAsync`, `SubmitAnswerAsync`, `ResumeDialogAsync`, `EditAnswerAsync`. Behind it,
  commands/queries in `src/Flirty/Runtime/` (`StartDialogCommand.cs`, `SubmitAnswerCommand.cs`, …),
  shared `TransitionResolver.cs`. `StartDialogVersionCommand` (#43) starts a **specific dialog version
  regardless of publication status** – deliberately **without** an HTTP endpoint: over HTTP the publish
  status stays the production barrier.
- **Admin CRUD:** commands/queries in `src/Flirty/Runtime/Admin/`, repository
  `src/Flirty/Persistence/IDialogAdminStore.cs`. It also contains `SetDialogLayoutCommand` /
  `ResetDialogLayoutCommand` (#102) – the two commands **without** `DialogEditGuard`.
- **Web endpoints:** `src/Flirty.AspNetCore/FlirtyEndpointRouteBuilderExtensions.cs` (runtime) and
  `FlirtyAdminEndpointRouteBuilderExtensions.cs` (admin), error mapping in
  `FlirtyExceptionEndpointFilter.cs` (namespace `Microsoft.AspNetCore.Builder`).
- **MCP server:** `AddFlirtyMcp(Action<FlirtyMcpOptions>?)` in
  `src/Flirty.Mcp/FlirtyMcpServiceCollectionExtensions.cs` (namespace deliberately
  `Microsoft.Extensions.DependencyInjection`; returns the SDK's `IMcpServerBuilder`, so a host – and the
  test host – can add its own tools) and `MapFlirtyMcp(string pattern = "/mcp")` in
  `FlirtyMcpEndpointRouteBuilderExtensions.cs` (namespace `Microsoft.AspNetCore.Builder`; returns the
  `IEndpointConventionBuilder` so `RequireAuthorization()` chains; also accepts `"/mcp/{target}"` and
  rejects any other route parameter). Options + `[Flags] FlirtyMcpSurface` (`Runtime`/`Admin`/`Database`)
  in `FlirtyMcpOptions.cs`, error mapping in `FlirtyMcpExceptionFilter.cs` (+ payload `FlirtyProblem.cs`),
  server instructions in `FlirtyMcpInstructions.cs`, the result wrappers in `FlirtyToolResults.cs`.
  Target resolution (#129) in `FlirtyMcpTarget.cs` (the only type holding a connection string – in no
  tool signature), `FlirtyMcpTargetRegistry.cs` (singleton, prebuilt `DbContextOptions` per target),
  `FlirtyMcpRequestTarget.cs` (scoped), `FlirtyMcpDbContextFactory.cs` and `FlirtyMcpTargetFilter.cs`;
  the three database operations in `FlirtyMcpDatabaseOperations.cs` (+ `FlirtyMcpDatabaseException.cs`).
  Tools in `Tools/`: `FlirtyToolNames.cs` (**every** wire name as a const – the single parity checklist,
  reflected over by the golden test) plus `Flirty{Dialog,Question,AnswerOption,Transition,Loop,Trigger,
  Layout,Session}Tools.cs`, one per `MapXxxEndpoints` counterpart – `FlirtySessionTools` (#128) mirrors the
  **runtime** group `MapFlirtyEndpoints` and is the only class whose tools set `OpenWorld = true` – and
  `FlirtyDatabaseTools.cs` + `FlirtyDatabaseMigrationTools.cs` (#129), the two without such a counterpart;
  the second exists only because `flirty_db_migrate` is registered **conditionally**
  (`o.AllowMigrations()`) and `WithTools<T>()` takes a class as its unit. The shape conventions of all ten
  are documented **once**, on `FlirtyDialogTools`. `AddFlirtyMcp` deliberately does **not** call
  `AddFlirty()`.

## Standard commands (pwsh)

```pwsh
dotnet build Flirty.sln
dotnet test                                   # or: dotnet test tests/Flirty.Tests
dotnet pack -c Release -o artifacts           # only Flirty + Flirty.AspNetCore
dotnet pack -c Release -p:BuildRevision=7     # set the package revision -> Flirty.202607.7.nupkg
dotnet tool restore                           # once, for dotnet ef + reportgenerator (local tools)

# Coverage as in CI (#48): measures ONLY Flirty + Flirty.AspNetCore, filters in coverage.runsettings
dotnet test tests/Flirty.Tests -c Release --collect:"XPlat Code Coverage" `
  --settings coverage.runsettings --results-directory artifacts/coverage/unit
dotnet reportgenerator -reports:artifacts/coverage/unit/**/coverage.cobertura.xml `
  -targetdir:artifacts/coverage/report -reporttypes:"Html;TextSummary" -sourcedirs:$PWD
```

## Test conventions (`tests/Flirty.Tests`)

- **xUnit v2** (`2.9.3`). No mocking framework – test doubles by hand (spy/recording).
- **SQLite in-memory as the default:** an open `SqliteConnection("DataSource=:memory:")` +
  `EnsureCreated()`, separate seed/read contexts. Facade tests build the real DI stack via `AddFlirty()`.
- **Endpoint tests** via `FlirtyTestHost` (in-process `TestServer`, Docker-free).
- **PostgreSQL/SQL Server** via Testcontainers → need Docker; without Docker cleanly skipped via
  `[SkippableFact]` + `Skip.IfNot(DockerAvailability.IsAvailable, …)`.
- **Test names in English**, snake_case-ish (`StartDialogAsync_starts_dialog_via_facade`).
- The core exposes internals to tests via `[assembly: InternalsVisibleTo("Flirty.Tests")]`.

## Conventions / overrides against the globals

- **Language: English.** Code comments, XML docs, commit messages and test names are all in English –
  keep it that way. (Umlauts are no longer an issue at all: there is no German prose left to get wrong.)
- **Shell:** `pwsh`. **Package manager/build:** `dotnet` (not `pnpm`/`ng`/`tsc`).
- **Git:** branches `feature/dz/<issue>` or `bugfix/dz/<issue>`. PRs via the `gh` CLI (the GitHub MCP
  token cannot create PRs in this repo).
- **Definition of Done** per change: code + English XML docs (CS1591) + green tests + the matching
  `docs/` guide updated + context/skills kept in sync (see next section).

## Skills for recurring tasks

Under `.claude/skills/` there are function-specific guides – check first when a task fits:
`flirty-runtime-command`, `flirty-ef-migration`, `flirty-trigger-notification`, `flirty-question-type`,
`flirty-nuget-package`, `flirty-designer`, `flirty-mcp`.

## Keeping context & docs in sync (important)

This file, the skills and `docs/` do **not** update themselves – they are part of the task.
Whoever changes code pulls the affected docs along in the **same** PR. Concretely:

- **New pattern / new extension path** implemented → create/update the matching skill under
  `.claude/skills/` (keep paths, steps, DoD current).
- **New/changed public API, convention, dependency or command** → adjust the affected sections here in
  `CLAUDE.md` and the responsible `docs/` guide (see the docs guide below).
- **Feature completed / project status shifted** → update the "Status & open work" section below (issue
  numbers, "CURRENTLY ONLY A SKELETON" hints, missing guides).
- **Fundamental decision made** (a nearby alternative deliberately discarded, expensive to revise later)
  → a new ADR in `docs/adr/` following the template in `docs/adr/README.md`, and enter it in the table
  there. Existing ADRs are **not rewritten**: addendum or supersession by a new one.
- **Rule of thumb:** if a statement in `CLAUDE.md`/a skill/`docs/` would become *wrong* through your
  change, fix it now – stale context is worse than none.

## Docs guide (`docs/`)

| Topic | File |
|---|---|
| Architecture overview | `docs/ARCHITECTURE.md` |
| Domain model & EF configuration | `docs/DOMAIN-MODEL.md` |
| Runtime (start/resume/submit/edit) | `docs/RUNTIME.md` |
| Persistence & migrations | `docs/PERSISTENCE.md` |
| Mediator setup | `docs/MEDIATOR.md` |
| Branching / expressions | `docs/BRANCHING-EXPRESSIONS.md` |
| Loops | `docs/LOOPS.md` |
| Triggers (notifications + webhooks) | `docs/TRIGGERS.md` |
| MCP server (tools, error mapping) | `docs/MCP.md` |
| Answer validation | `docs/VALIDATION.md` |
| NuGet packaging | `docs/NUGET-PACKAGING.md` |
| CI pipeline | `docs/CI.md` |
| Getting Started (Console / WebAPI) | `docs/GETTING-STARTED-Console.md`, `docs/GETTING-STARTED-WebApi.md` |
| Getting Started (Web sample / chat UI) | `docs/GETTING-STARTED-Sample-Web.md` |
| Designer (Blazor) | `docs/DESIGNER.md` |
| Backlog / roadmap | `docs/BACKLOG.md`, `docs/ROADMAP.md` |
| Decisions (ADRs) | `docs/adr/README.md` (index + format), ADRs 0001–0010 |

## Status & open work

**Done (M1+M2):** domain, persistence (3 providers), expression engine, runtime (start/resume/submit/edit,
loops, validation), triggers (notifications + webhooks), DI facade, WebAPI endpoints (runtime + admin CRUD),
console sample, **web sample** (minimal API + chat UI, #45).

**Chat-UI E2E (#47) done** – seven Playwright tests (`tests/Flirty.E2E/WebSampleE2ETests.cs`): both
branching branches, a loop with two iterations including a trigger round-trip, reload→resume **inside** the
loop and four edit cases (free text, branching question, a specific loop iteration, yes/no). The build-out
uncovered and fixed a bug in the sample chat UI: the edit form now renders the input control
**type-dependently** (shared `renderAnswerControls` for the open question *and* edit). Before, it always
rendered a text field and wrote back the *display form* – for `SingleChoice` therefore the label instead of
the value (→ `AnswerValidator` `400`), for `Boolean` the answer silently flipped to "No". Mnemonic for the
sample UI: **the label is displayed, the value is stored** – `decodeForDisplay` belongs in the bubble,
`decodeRaw` in the input field.

**Designer (M3):** **connection-profile management (multi-DB, #37) done** – profiles (JSON, gitignored),
test-connection, migrate, `IDbContextFactory` selection against the active profile. For this a new public
core API `FlirtyDatabaseProvider` + `DbContextOptionsBuilder.UseFlirtyProvider(...)` (centralizes the
provider→MigrationsAssembly mapping); guide `docs/DESIGNER.md` created.
**Dialog CRUD (#38) done** – list `/dialogs` + editor `/dialogs/{id}` (metadata, entry question,
publish/unpublish, delete). All admin operations run through `FlirtyAdminGateway`
(`src/Flirty.Designer/Services/`), which sends **every** message in a fresh DI scope – in Blazor Server a
scope otherwise lives the whole circuit and pins the `FlirtyDbContext` to the first-used profile.
Follow-up editors use this gateway too. Shared UI classes live globally in
`src/Flirty.Designer/wwwroot/app.css`.
**Question editor (#39) done** – question list in the `DialogEditor` (create, ↑/↓ sort, delete) plus
detail page `/dialogs/{dialogId}/questions/{questionId}` (`QuestionEditor.razor`) with metadata, a
**type-scoped** validation editor (`Models/QuestionFormModel.cs` serializes the core type
`Flirty.Validation.ValidationRules`; regex is compiled on save, unknown JSON lands in a raw-JSON
fallback) and answer-option CRUD.
**Branching editor (#40) done** – the "Transitions" section in the `DialogEditor` (grouped per source
question, ↑/↓ writes the position index as `Priority`, warnings following the `TransitionResolver` rules)
plus detail page `/dialogs/{dialogId}/transitions/{transitionId}` (`TransitionEditor.razor`) with
**live validation** of the condition and a snippet inserter. The core is the sample context
`Services/DesignerExpressionContext.cs`: it binds a type-correct sample value per question (types as at
runtime – a date is a *string*) and every loop collection as an empty list. For this,
`GetDialogQuery` also delivers the loop markers **as reads** since #40 (`DialogDetail.Loops`).
**Loop editor (#41) done** – for this the missing **loop CRUD** was added: `Create/Update/DeleteLoopCommand`
(`CollectionKey` unique per dialog, question references stay FK-free and unchecked), `.../loops` endpoints
and `Loops` in `DialogDetailResponse`; `DeleteQuestionCommand` now cleans up referencing markers too (like
the transitions already did). UI: the "Loops" section in the `DialogEditor` (incl. suggestions from
unmarked back-jumps) plus `/dialogs/{dialogId}/loops/{loopId}` (`LoopEditor.razor`). The core is
`Services/LoopAnalyzer.cs`: it **mirrors** the body computation of the core-internal `LoopResolver` (which
is `internal` and needs a `Dialog` entity) and warns about a missing/unreachable exit (infinite loop),
overlapping ranges and shadowing `CollectionKey`s. A test compares both implementations on the same graph –
pull it along on changes to the `LoopResolver`.

**Trigger editor (#42) done** – and thereby, for the first time, a **`TriggerDefinition`-driven runtime**:
until #41 the entity was dead configuration (webhooks came only from `o.AddWebhook`). Now the
`WebhookNotificationHandler` reads, per notification, the triggers of the session dialog as well
(`IDialogStore.GetTriggersForSessionAsync`, a slim query – the old promise "no DB access without an
expression" no longer holds) and delivers `Kind = Webhook` (header `X-Flirty-Event` + new
`X-Flirty-Trigger`). The expression evaluation is encapsulated: a non-evaluable expression (e.g. an
answer that does not yet exist at `OnDialogStarted`) skips the target instead of tearing down the command.
New in the core: `TriggerConfig` (public JSON schema `url`/`name` for `TriggerDefinition.Config`, analogous
to `ValidationRules`), `Create/Update/DeleteTriggerCommand` (cross-field rules via `IValidatableObject` →
`ValidationException`/400: `AfterQuestion` needs a question exactly there, `Webhook` needs an absolute
URL), `Triggers` in `DialogDetail`/`DialogDetailResponse`, `.../triggers` endpoints;
`DeleteQuestionCommand` cleans up referencing triggers. UI: the "Triggers" section in the `DialogEditor`
plus `/dialogs/{dialogId}/triggers/{triggerId}` (`TriggerEditor.razor`) with live validation over the
**unchanged** `DesignerExpressionContext`.

**Test runner (#43) done** – and thereby EPIC 7 completed. The runner (`/dialogs/{id}/test`,
`DialogTestRunner.razor`) plays a dialog through with the **real engine**: history with
`Iteration n` badges, answer editing per iteration, a live view of the expression bindings and a
log of the published triggers. New in the core for this: `StartDialogVersionCommand` +
`IFlirtyEngine.StartDialogVersionAsync` (see above) – without it a **draft** would not be testable, and
"publish briefly to test" would have armed it for real users. In the designer, alongside the page, came:
`DesignerGateway` as the shared base of `FlirtyAdminGateway` and the new
`FlirtyRuntimeGateway` (`AdminResult<T>` is now `GatewayResult<T>`), `AnswerValueCodec` as the
**single** source of the JSON answer contract (`DesignerExpressionContext` derives its sample values
from it too), `RunExpressionContext` as a mirror of the core `SessionExpressionContextBuilder` (with a
comparison test, as with `LoopAnalyzer`) and `DesignerTriggerLog` with four notification handlers – the
log is passed into the child scope via `Adopt`, like the active profile. **Caution:** test runs
write real sessions (`ExternalUserKey` prefix `designer-test-`) and actually deliver configured webhooks.

**Designer E2E (#46) done** – and thereby EPIC 7 secured in the browser too. For this the designer's
composition was moved out of `Program.cs` into `src/Flirty.Designer/DesignerApp.cs`
(`ConfigureServices`/`Configure`, pattern like `WebSampleApp`), so that `tests/Flirty.E2E/DesignerAppFixture`
can host it in-process on a free Kestrel port – with a pre-activated connection profile on
a migrated SQLite temp database in a temp ContentRoot. Two tests: the issue's creation flow
(dialog → questions → options → entry question → transitions incl. live validation → loop →
publish → reload as a persistence proof) and a test run over the runner (#43) with two
iterations. **Two pitfalls** (both recorded in `docs/DESIGNER.md` § Tests): the host needs
`ApplicationName = "Flirty.Designer"` **and** `EnvironmentName = "Development"`, otherwise
`_framework/blazor.web.js` is missing and nothing is interactive; and after **every** page change the
first interaction fizzles silently until the circuit has taken over the page – there is no reliable JS
signal for it, so `InteractWhenReadyAsync` repeats it (the action must be idempotent).

**Coverage in CI (#48) done** – the pipeline measures with `coverlet.collector` and reports via
**ReportGenerator** (a local tool, like `dotnet ef`): job summary on the Actions run + HTML artifact
`coverage`, `permissions` stays `contents: read`. Measured are **only the two NuGet packages**
(filters centralized in `coverage.runsettings`); migrations/samples/designer are excluded.
Baseline: **95.7 % lines / 85.5 % branches** (Flirty 95.1 %, Flirty.AspNetCore 98.4 %). Deliberately
**without** a threshold gate – a floor value should rest on the measured state. Two pitfalls, both
recorded in `docs/CI.md` § Coverage: `coverlet.collector` **must follow the .NET line** (the
template version 6.0.4 did not instrument `net10.0` assemblies and silently dropped the core out of the
report → now 10.0.1), and coverage comes **only** from `tests/Flirty.Tests` – in the
E2E output directory coverlet fails on `Flirty.dll`, and the E2E raised coverage anyway only
by a single branch.

**NuGet publish (#49) done** – and thereby EPIC 9 completed. The push lives in a **second**
workflow `.github/workflows/release.yml`, not in CI: a version published on NuGet.org
is irreversible (only unlist, not delete), so it is triggered manually
(`workflow_dispatch`, inputs `revision` and `dry_run`). Two jobs, so that the approval gate
sits **between** build and push: `build` (restore → build → unit suite → pack → **verify**
→ artifact) and `push`, which hangs off the GitHub environment `nuget` (secret `NUGET_API_KEY`,
optional reviewers). The verification step is the hard lock: it checks against the real files
`.nupkg` **and** `.snupkg` per package (= the AC "incl. symbols") as well as all four DLLs under
`lib/net10.0/`. The `.snupkg` are pushed along automatically by `dotnet nuget push` – no second
push. The feed is **NuGet.org only**; Azure Artifacts (mentioned in the issue text) was deliberately
dropped, because it does not accept symbol packages via `dotnet nuget push` and thus misses exactly that AC.
**Side finding, recorded in `docs/NUGET-PACKAGING.md`:** the version is two-part as an MSBuild property
(`202607.7`), **but NuGet normalizes to three segments** – file name, `.nuspec` and
nuget.org show `202607.7.0`. The docs claimed `Flirty.202604.1.nupkg` in several places; that
was simply wrong. To be set up manually once (the workflow cannot): an API key on nuget.org
with glob `Flirty*` and scope *Push new packages and package versions*, plus the environment `nuget`
with the secret.

**docs/ guides (#50) done** – a closing pass through the eight guides named in the issue (plus the
clearly wrong sentences in `PERSISTENCE.md`/`MEDIATOR.md`/`RUNTIME.md`). It was no rewrite: the guides
grow along per DoD, what was stale were **statements** that later issues had overtaken. The gravest:
`GETTING-STARTED-WebApi.md` claimed the trigger CRUD was not part of the admin endpoints – since #42
`MapTriggerEndpoints` registers three routes. Also wrong: `TRIGGERS.md` named only the
`StartDialogCommandHandler` as the publisher of `DialogStarted` (since #43 the
`StartDialogVersionCommandHandler` does it too – a designer test run therefore fires `OnDialogStarted`
just the same), and `BRANCHING-EXPRESSIONS.md` still listed #34 under "outlook". **Mnemonic:** what makes
a change *wrong* rarely stands in the file one is editing – the stale scan
`grep -nE 'follows|coming in|later epic|outlook|not yet|once' docs/*.md` finds such pointers
to finished issues. There is no CI gate for dead cross-references; anchors/paths are to be checked before
the PR.

**ADRs (#51) done** – `docs/adr/` now contains the four decisions named in the issue plus an
index `README.md` (table, format template, maintenance rules): **0001** migrations per provider (existing,
extended by an addendum – its open points #20/#34 are long since done), **0002** Mediator
(martinothamar) as the in-process bus, **0003** ASP.NET-free core, **0004** the sandboxed expression engine.
The work was **not** to rephrase the known: the "how" had long been in the guides,
what was missing was the **"why not otherwise"**. That is why the *Discarded alternatives* section is the
core of every ADR (MediatR and its reflection/license situation, a package with an ASP.NET reference or
`#if` variants, Roslyn scripting/NCalc/a custom grammar). **Two rules recorded in the index:** an ADR is
**not rewritten** – it gets an *addendum* or is *superseded* by a new one (status
`Superseded by NNNN`), otherwise it no longer answers "why, actually?"; and **numbers are never reassigned**
– 0001 stays 0001, even though the decision fell chronologically after #13/#14, because `CLAUDE.md`,
`docs/PERSISTENCE.md` and `.claude/skills/flirty-ef-migration/SKILL.md` point at the file name.
Rule of thumb for new ADRs: only when a nearby alternative was deliberately ruled out and the
decision would be expensive to revise later – everything else belongs in the guide.

**Acceptance findings (#95) fixed** – a manual pass through the running application (build, tests,
console sample, web sample over HTTP and in the browser, designer via Playwright) produced eight findings.
The grave one: **dialog versioning existed only on paper.** `Version` was set only on
creation, a second dialog with the same `Key` was rejected – so there was **no way to
a second version**, and because the runtime loads a session's graph via the `DialogId` from
the same row that the admin CRUD changes, deleting the open question of a
published dialog broke the running session (resume **and** submit → 409). The exact opposite stood
in four guides and here. The promise (ADR 0005) is now implemented: published versions are
locked (`DialogEditGuard` in all 15 graph commands, `UpdateDialogCommand` only for the
entry question – name/description stay free), `CreateDialogVersionCommand` +
`POST .../dialogs/{id}/versions` clones the graph as a draft with `Version`+1 (all question references
rewritten), `PublishDialogCommand` retires the predecessor version, and `DeleteDialogCommand`
refuses deletion with running sessions (`AbandonDialogSessionsCommand` +
`POST .../abandon-sessions` ends them). The core check as a test: a session runs to completion on version 1
while version 2 is derived, changed and published. The remaining findings were UI-side:
navigation links with contrast **1.7:1** (cause: CSS isolation does **not** carry its scope attribute
into child components like `<NavLink>` – the rules now live globally in `wwwroot/app.css`),
a stale start page ("editors follow in later build-out stages" – they have all been there since #43), an
English 404 page, connection-profile deletion without a confirmation, a deleted **active** profile that
lived on in the circuit (`ActiveConnectionProfile.Clear()`), and date formats in the host culture in the
middle of German text (`DesignerApp.DisplayCulture = "de-DE"`).

**Root README (#52) done** – and thereby EPIC 10 and **M4** completed. The README, like the
guides at #50, had grown along incrementally; the pass fixed three defects. **The most important
finding:** `Directory.Build.targets` sets `PackageReadmeFile` – the root README is the
**description page of both NuGet packages**, not only the GitHub start page. On nuget.org there is no
repo root directory, so **all 15** `](docs/…)` links ran into the void there; they are now absolute
(`https://github.com/dominikz98/flirty/blob/main/…`), the badge hosts (`img.shields.io`,
`github.com/.../badge.svg`) were on the allowlist anyway. Second, the
**console quickstart snippet was simply broken**: `new ServiceCollection()` + `o.ApplyMigrations()` – that
registers only an `IHostedService` that nobody starts without a Generic Host, the DB was never created
(exactly the trap `GETTING-STARTED-Console.md` §1 warns about). Both quickstarts now come from
the compilable samples (`Flirty.Samples/Program.cs`, `Flirty.Samples.Web/WebSampleApp.cs`). Third,
the designer start, the admin endpoints and 11 of the 17 guides were missing from the docs index. Newly
secured: `tests/Flirty.Tests/Docs/PackageReadmeTests.cs` (README via `Content` copy in the test output
directory, pattern like the wwwroot copy in `Flirty.E2E`) checks "no relative targets" and "images only
from the nuget.org allowlist" – needed because the warning about it is seen **only by the package owner**
and can only be corrected with the next published version. The rules are in
`docs/NUGET-PACKAGING.md` § *The root README is the package page*.

**Test-run findings (#97) fixed** – a second manual pass (build, both suites, both
samples, designer via Playwright, a custom dialog end-to-end) produced seven points; the engine
itself was clean. The most important: **a reproducibly red E2E test**
(`Editing_the_branching_question_switches_the_branch`) – not a product bug, but a race in the test.
`FillAndSendAsync` returns immediately, the following edit overtook the still-flying submit, the
server discarded one answer too few (1 instead of 2). Fixed at **both** ends: the chat UI locks the
✏️ buttons for the duration of a request (`setBusy`), and the test makes the precondition visible with
`AwaitAnsweredAsync` instead of silently assuming it. **Mnemonic:** a green test
on CI hardware does not mean it has no race – whoever triggers an action and immediately clicks the next
needs a signal that the first is through. The remaining points: the designer set
**no `font-family`** and thus ran entirely in the browser default font (Times New Roman) –
`body` rule now global in `wwwroot/app.css`, as in the chat UI; the hint "Without an entry question …"
stood even with the entry question set; **publish** ignored open transition warnings
(now: `GraphWarnings()` repeats them in the publish section and asks back – once published
the graph is locked, so the mistake then costs a new version); the expression sandbox passed the
DynamicExpresso message "enable reflection via `Interpreter.EnableReflection()`" through to the
dialog author (now its own message, recognized by the type `ReflectionNotAllowedException`, not by the
localized text); and the loop suggestion pluralized the English way (`belag` → `belags`, now
`belag_liste`).

**Canvas spike (#100) done** – stage 0 of **EPIC 11** (visual graph designer, #99). The result is
no code, but ADR `docs/adr/0006-canvas-technology-in-the-designer.md`: the canvas is **built in-house**
(SVG in Razor + collocated `*.razor.js`), `Z.Blazor.Diagrams` drops out. Measured with two
throwaway prototypes over the same graph (30 nodes / 45 edges) against a throttled circuit
(2 × 75 ms); the code lives on the **never-merged** branch `spike/dz/100`. Numbers per drag gesture:
in-house **0 px lag, 2↑/2↓ messages, 688 B**; Blazor.Diagrams **40 px (≈ 64 ms), 68↑/68↓,
50,309 B** – so 34 times as many messages. The cause is not the license or target framework (MIT, native
`net10.0`, no advisory, builds with **0 warnings** under `TreatWarningsAsErrors`), but the
drag path: `DiagramCanvas.razor` wires `@onpointermove` as a C# handler, and the shipped
`script.js` contains only `ResizeObserver`/`MutationObserver`/`getBoundingClientRect` – there is no
client-side drag path there. **Mnemonic:** a library's demo proves nothing about
your own interactivity variant – ZBD's runs on **WebAssembly**, i.e. without a network between
pointer and model. Three things learned during measuring itself and valid for EPIC 11:
CDP `Network.emulateNetworkConditions` **does not throttle the latency of WebSocket frames** (Chromium
only applies throughput there) – artificial latency needs its own TCP proxy, and it must **decouple** the
read and write sides, otherwise it is a rate limiter instead of a latency model; a
WebSocket frame is **not** a SignalR message (`blazorpack` packs several with a varint prefix into
one frame, whoever counts frames undercounts); and browser events arrive in .NET 10 as
`BeginInvokeDotNetFromJS` (`DispatchEventAsync` over a `DotNetObjectReference`), **not** as
`DispatchBrowserEvent`. For the tests it follows: the canvas needs an **explicit**
readiness signal (`data-canvas-ready`) – `InteractWhenReadyAsync` presupposes idempotent actions,
a repeated drag moves twice.

**Graph view (#101) done** – stage 1 of EPIC 11. A new page `/dialogs/{id}/graph`
(`DialogGraph.razor`, linked from the dialog list and dialog editor) shows the dialog **as a read** as a
graph: questions as nodes, all transitions as labeled edges, loops as range frames over the
`LoopAnalyzer` body, triggers as chips, warnings **at the causing element**; selection opens an
inspector panel that jumps into the existing editors (no rewrite – the four editors are
`@page` components with their own `PageTitle`/`h1`/back link). **No new core code**: the data source stays
`GetDialogQuery`. New in the designer: `Services/GraphLayout.cs` (Sugiyama-light),
`Services/DialogGraphBuilder.cs`, `Services/TransitionWarningAnalyzer.cs`, `Models/GraphWarning.cs`,
`DialogGraphModel.cs`, `GraphLayoutResult.cs`, `GraphMetrics.cs`, `SvgFormat.cs`, the components
`GraphNodeCard`/`GraphInspector` and **the designer's first JSInterop** (`DialogGraph.razor.js` for
pan/zoom). Four things that tipped the scales:

- **Warnings needed a location.** `GraphWarnings()`/`GroupWarnings()` sat privately in
  `DialogEditor.razor` and produced running text with a `"{Key}: "` prefix. They moved unchanged into the
  `TransitionWarningAnalyzer` and now produce `GraphWarning` (target + text); `LoopAnalyzer`
  likewise (`LoopInsight.TargetedWarnings`, `Warnings` is a computed text view). **The wordings
  are a contract** – the list view, publish confirmation and E2E hang on them, a test nails all four.
  Located by *cause*: "No default"/"Multiple defaults" are group properties and
  hang on the question, "always matches"/"is not evaluated" on their edge.
- **The layout ordinal comes from `(Order, Id)`, not from the Guid.**
  `CreateDialogVersionCommand` assigns each question a new Guid on cloning (ADR 0005) – a
  guid-based layout would reshuffle on **every** new dialog version. Plus: only lists to the
  outside (hash order is not guaranteed), sort keys end with the unique ordinal
  (total order instead of borrowed `OrderBy` stability), and coordinates arise **only** from
  integer layer/column values – a barycenter determines the order, never the position.
  All three are test cases.
- **`<foreignObject>` carries.** Proven against the shipped `blazor.web.js`: the
  SVG namespace check reads `namespaceURI === svg && tagName !== "foreignObject"`, so child elements
  arise in the HTML namespace. That makes nodes real `<button>`s – focus ring, Enter/space and
  screen-reader role come from the platform instead of from hand work.
- **`SvgFormat.N` is mandatory for every number in the SVG markup.** The designer runs under `de-DE`; an
  interpolated coordinate writes `12,5`, and since the comma is a *separator* in path syntax,
  it silently becomes a wrong number sequence – no exception, no message, only a wrong
  picture. A test under `de-DE` secures this.

**Mnemonic from the build:** a test that only checks structure sees no crooked picture. The
determinism test uncovered a real drawing bug that no assertion had looked for – at
80 px layer spacing the Bézier control points crossed (`C 160 206 160 146`), the edge ran
as an S-loop. The bend radius is now coupled to half the span.

**Layout persistence (#102) done** – stage 2 of EPIC 11 and the **only stage with a
schema change**. Nodes are movable on the canvas; the position lives in the new table
`DialogLayout` (`ElementKind`/`ElementId`, unique over `(DialogId, ElementKind, ElementId)`, cascade on
the dialog) plus migration `AddDialogLayout` in all three provider sets. New in the core: `DialogLayout` +
`LayoutElementKind`, `Dialog.Layout`, `IDialogAdminStore.GetLayoutAsync`/`GetLayoutsReferencingElementAsync`,
`SetDialogLayoutCommand` (batch upsert) and `ResetDialogLayoutCommand`, `DialogLayoutDetail`/`DialogLayoutEntry`
and `DialogDetail.Layout`; in `Flirty.AspNetCore` `PUT`/`DELETE .../dialogs/{id}/layout` plus `Layout` in
`DialogDetailResponse`. Reason: ADR `docs/adr/0007-layout-as-its-own-table.md`. Five things that tipped
the scales:

- **The table exists because of the write path, not because of extensibility.** Two columns
  `LayoutX`/`LayoutY` on `Question` would have been cheaper and would have brought cloning and cleanup
  for free. But: a layout write must **not** fall under `DialogEditGuard`, otherwise
  a published dialog could not even be laid out clearly – and that is exactly the one opened
  most often. With two columns on a locked entity that would be a **convention** (a field written
  past the guard, and every future path would have to know the exception); with its own
  table it is **structural**: there is nothing locked to bypass. Two tests hold this down –
  "layout on the published dialog works" **and** the counter-check "a graph change on the same dialog
  returns 409".
- **A designer-local JSON file was the real temptation** – and it fails not on convenience,
  but on ADR 0005: `CreateDialogVersionCommand` assigns each question a new Guid and does not hand out the
  `questionIdMap`. Every new version – the only way to change a productive dialog –
  would start with a discarded layout, exactly at the moment of continuing to work.
- **Saved positions take effect at *one* place:** at the end of `GraphLayout.Render`, where the
  node boxes arise. Layering, edge shape, barycenter and channel assignment stay with the auto-layout –
  otherwise a single drag could resort the whole graph. Because edge routing and loop frames read
  from the same boxes, they follow along automatically; the drawing surface grows to include moved nodes.
- **The drag needs three little things without which it is not usable:** a **4-px threshold** (otherwise
  every wobbly click swallows the selection), the **swallowing of the following `click`** (otherwise every
  drag additionally selects the node – a second message no one triggered) and
  `viewport.getScreenCTM().inverse()` instead of a calculation over the zoom factor: the matrix also
  contains the `viewBox` scaling relative to the CSS width, without it the node would run faster or slower
  than the pointer depending on the window width. The adjacent edges are **dimmed** during the drag,
  not recomputed in the browser – their geometry lives in C# and is tested there.
- **`invokeMethodAsync` stands exclusively in `onPointerUp`.** That is the form in which the AC "one
  server message per gesture" is held; the spike's (#100) measuring rig lives on the never-merged
  branch `spike/dz/100` and was not available here. In the module header stands the promise with its warning.

**Mnemonic:** a failed E2E drag shows the effect, never the cause. The test was red because
`page.Mouse` takes **window-relative** coordinates and the 70 vh tall canvas host stands below header,
hint and toolbar – the gesture aimed past a node of the lower layers, without
any message. `ScrollIntoViewIfNeededAsync` before the drag, and do not even try `DragToAsync`: that
uses the HTML5 drag-and-drop API, which triggers nothing on a pointer-events canvas.

**Editing on the canvas (#103) done** – stage 3 of EPIC 11 and the stage that turns the canvas into an
editor: a palette of question types (dragging creates at the drop point, clicking appends at the end),
a source port per node (dragging connects, dragged into the void a question **and** a transition arise in
one drag), inspector panels for header fields, evaluation order, default toggle, condition, triggers
and delete; a loop suggestion **at the cycle** instead of in a list; a published dialog ⇒ a real
read mode in which moving still works. **No core code, no schema change** – rationale in ADR
`docs/adr/0008-gestures-on-the-canvas.md`. Six things that tipped the scales:

- **After a graph mutation there is a reload** – this restricts ADR 0007's "the commit does not reload"
  explicitly to the *layout* path. Not because of the entities, but because of the **warnings**:
  `TransitionWarningAnalyzer`/`LoopAnalyzer` compute over the *whole* `DialogDetail`, a new transition
  can lift a warning on another question. And `DeleteQuestionCommand` cleans up transitions, markers,
  triggers and layout rows along with it – rebuilding that cascade locally would be the second truth the
  issue forbids. It is reported as a diff before/after the reload; `ReconcileSelection()` discards
  a selection whose element is gone (otherwise the inspector renders into an empty branch).
- **The gesture lock needs both ends.** The `DialogEditor`'s lock is exclusively the
  rendered `disabled` – a `[JSInvokable]` call sees none of it, and every `await` releases the
  circuit context. Client-side everything runs through `send()`, where **the promise of
  `invokeMethodAsync` is the receipt** (no second back channel one can forget); server-side
  `RunGestureAsync` exits early on `_busy`. `MoveNodeAsync` hung until then **entirely without** a gate and
  was harmless only because `SetDialogLayoutCommand` is an upsert.
- **The empty dialog now renders a drawing surface.** Before, a hint replaced the canvas as long as
  there were no questions – nothing can be dragged onto a non-existent surface, and that is exactly where
  every new dialog begins. The hint sits above it, `GraphMetrics.MinCanvasWidth/Height` gives a lower
  bound (previously 80 × 80 px), and the flag `_canvasAttached` was dropped: the truth is `_module`.
- **`data-editable` belongs to C#, the JS only reads it** – fresh on each gesture instead of frozen as an
  `attach` option. That is the flip side of the ADR-0006 rule "what the JS sets, C# never renders", not
  its breach. Likewise `data-busy`: locking is done via `pointer-events`, **not** via `disabled` on the
  port and palette entry – Blazor otherwise re-renders an attribute mid-drag and the pointer capture is
  lost.
- **Rubber band and drop preview are C#-rendered, empty placeholders** (`.graph-rubber`,
  `.graph-ghost`); the module only sets their geometry. DOM created via `createElement` in a
  Blazor-managed container throws the renderer off over the child indices on the next diff.
  Both need `pointer-events: none`, because the target hit test runs via `document.elementFromPoint`
  (after `setPointerCapture` the `event.target` is the capture element) and would otherwise hit the rubber
  band.
- **Rules that live in the `@code` block are not testable** – the designer has no bUnit. `NextOrder`,
  `NextPriority` per source question and the resorting (position index → `Priority`, repairs duplicate
  values) therefore moved into `Services/GraphEditing.cs`, `IsBackJump`/`UnmarkedBackJumps` into the
  `LoopAnalyzer`; both views now use them. New: `QuestionFormModel.SuggestKey` – unlike
  `LoopFormModel.SuggestCollectionKey` it must **never** return empty, because the suggestion carries a
  gesture that writes immediately. Along the way it turned out that `SuggestCollectionKey` itself was
  untested.

**The test build found two real defects that no unit test could see** – the reason why the
inspector path got a browser test despite "E2E belongs in #105": the panels now work
**without `EditForm`** (raw fields with `@oninput`, required check in the handler, `@key` on the
element id). `onchange` delivers the value only when the field is left and lost it, because the panel is
rebuilt after every gesture – the **old** value was silently saved; and the submit of an
`EditForm` did not arrive at all in a panel inside changing `@if` branches. Plus the E2E lesson: **a
DOM value does not prove Blazor saw the input.** If the first interaction on a
freshly rendered field fizzles, the typed value is still in the DOM until the next render overwrites it –
`ToHaveValueAsync` then reports success, and the test goes red **under load** and green alone. What is
checked is a server-produced effect, and the repeated unit comprises filling *and* saving; conversely,
a gesture that locks its own trigger (`disabled` during `_busy`) must not be repeated on its own.

**Mnemonic:** a new affordance breaks existing selectors with inevitability. The node now carries
a second button (the port), and `GetByRole(Button)` within `.graph-node` thereby became a
strict-mode violation – the E2E test from #101 was affected, without anything about its intent
changing. A second find of the same kind: `swallowNextClick` listened on the canvas, but the `click` after
a palette drag fires on the **palette entry** – without its own lock there, every drag additionally
created the click question, two questions from one gesture. A side finding of merging the
expression editor into `Components/ExpressionField.razor`: `.expr-status`/`.expr-caret` sat scoped in the
`TransitionEditor` but were used by the `TriggerEditor` too since #42 – there the live status was
**unstyled**.

**Test run in the graph (#104) done** – stage 4 of EPIC 11. The test runner (`/dialogs/{id}/test`) now
has **two views of the same run**: "History" (the list from #43, unchanged) and "Graph" – a canvas
with the taken path highlighted, the iteration count on the loop frame, published triggers at the
triggering node and the expression bindings with answer editing per iteration **at the selected node**.
Deep link `?view=graph`. **No core code, no schema change, no new command**; no ADR (no
nearby alternative was ruled out – the derivation below is the only possible one). New in the designer:
`Services/GraphRunAnalyzer.cs`, `Models/GraphRunModel.cs`, `Components/GraphRunCanvas.razor`,
`Components/GraphRunInspector.razor`; `GraphNodeCard` knows an optional run state,
`RunExpressionSnapshot` became `public` (CS0053 on a `[Parameter]`). Five things that tipped the
scales:

- **The engine logs no path.** `SessionAnswer` carries no `TransitionId`,
  `QuestionAnsweredNotification` only the next *question*. It is derived from the answer sequence: two
  consecutive answers = one pair *(from, to)*, the last answer plus the open question the
  last pair. From that follows a **fundamental** limit: if several transitions lie between the same two
  questions, it is not decidable which one took effect – then all are marked and reported as *ambiguous*.
  Recomputing the evaluation would be not only another mirror of the
  `TransitionResolver`, but an impossible one: it would need the expression values from back then.
- **The edit path therefore needed no code of its own.** `EditAnswerCommand` discards the downstream
  answers, the derivation shrinks along – even across a branch switch. That is exactly the
  core-check test.
- **A toggle instead of a second page.** Both views share start/submit/edit; the "Current question"
  card sits outside the toggle. That makes "the list-based runner stays
  equally usable" structurally true, instead of promised – and the list's E2E helpers carry unchanged in
  graph mode.
- **The graph is not editable in the run view** (no palette, no ports,
  `data-editable="false"`) – a running session works on exactly this graph, and changing it out from under
  it is the trap from #95. Moving stays allowed (guard-free, ADR 0007) and is the only
  writing path. The JS module binds the **component** here (`GraphRunCanvas`), not the page: the
  canvas belongs to it, otherwise the page would have to pass an `ElementReference` through.
- **The screenshot showed a flaw that no test looked for** (the same lesson as with the
  determinism test in #101): the event chips grew with every step – a loop question collects
  two per iteration – and burst the card, which clips overflow. They are now bundled per point in time
  ("⚡ Answer 2×"), the individual events stand in the inspector.

**Mnemonic:** where a fixed surface is filled with progress, the count is the problem, not the
representation – and a test that checks structure does not see an overflowing card. A look at the
rendered picture (here via a Playwright screenshot in the existing E2E run) costs minutes.

**Canvas E2E (#105) done** – stage 5 of EPIC 11 and thereby **EPIC 11 / M5 completed**. The
cut deliberately deviates from the issue text: of the "three tests" named there, two already passed as
smoke tests from #101–#104 (`Test_run_in_the_graph_highlights_the_path_taken` = test run,
`Graph_gestures_are_disabled_on_a_published_dialog` + `Graph_node_move_survives_the_reload`
= read mode). Added were the two that `docs/DESIGNER.md` had named "deliberately open for #105":
`Graph_creation_flow_on_the_canvas_survives_publishing_and_the_reload` (palette drag, **twice
dragging into the void**, a condition with live validation, default, entry question, moving, publishing,
reload incl. positions) and `Graph_inspector_creates_a_trigger_and_a_loop_at_the_cycle`. The suite thereby
comprises 17 tests and ran green three times in a row. Four things that tipped the scales:

- **The creation flow uncovered a real gap: the entry question could not be set on the canvas at all.**
  An author could build the whole graph from gestures but had to leave the surface for a single
  field – while the graph warned "No entry question set" the whole time, without offering a way
  there. Retrofitted as "Set as entry question" in the `GraphQuestionPanel`
  (`SetStart` → `GraphInspector` → `DialogGraph.SetStartQuestionAsync` → `UpdateDialogCommand`). **No
  core code:** the command's guard takes effect exactly when `StartQuestionId` changes (name and
  description stay free on a published version) – so the button carries the panel's usual `Locked`,
  and the publish lock becomes a non-triggerable action instead of an error message. If
  the node is already the entry, the button is omitted; the "Role" line already says so.
- **A discarded gesture is silent.** `send()` in the JS module drops a second message while the
  first runs – without a message, just without effect. That is why behind **every** gesture stands a
  server-produced quantity (node/edge count, `is-pinned`, `is-start`, edge label, wording in
  `.banner.ok`), never a wait time. Reading direction reversed: if a canvas test goes red, the
  first question is "which gesture was silently discarded?", not "which assertion failed?".
- **The test build found a latent race in the existing #103 test** – the same family as "a
  DOM value proves nothing", just on a `<select>`: the re-render of the node selection replaces via the
  `@key` the whole panel instance and thereby discards the just-made selection in `#inspectorConnect`;
  "Connect" stayed `disabled`, and the click ran into a timeout. The test was only green because the panel
  was already "warm" there. Fixed with a **visible precondition**: only the selection is repeated
  (harmless), and that the server knows the target is shown by the operable button – then it is clicked
  exactly once. The #103 test was pulled along.
- **Two selector traps that belong to the canvas:** an edge is selected via the list in the inspector,
  **not** by clicking `.graph-edge-hit` – the hit path is a Bézier whose bounding-box center
  need not lie on the stroke, and Playwright then aims beside it. And one aims on the surface in
  **fractions** instead of pixels: the SVG scales its `viewBox` into the 70 vh tall host whose width
  is shared by palette and inspector – a fixed pixel value hits a node at a different layout
  instead of the free surface, and the drag into the void silently becomes a connection.

**Mnemonic:** whoever replays a flow *completely* for the first time finds what is missing from it – not
only what is broken about it. The missing entry-question affordance stood in no issue and could not have
been shown by any unit test; it became visible only when the test had to walk the way a user walks.

**Acceptance findings (#118) fixed** – a manual pass through the running application wrapping up
EPIC 11 (pattern of #95/#97), against a dialog built **exclusively via canvas gestures**.
Stages #100–#105 themselves were not to be criticized; three findings, one of them deliberately
handed off. **No core code, no schema change, no new command, no ADR.**

- **Finding 1 (the English `ReconnectModal`) was handed off to #112**, not fixed: the repo's language
  switch to English overtakes it, a German translation would be double work in the opposite direction.
- **The publish confirmation was hand-picked – that was the defect, not the one missing warning.**
  `GraphWarnings()` in the `DialogEditor` read exclusively `TransitionWarningAnalyzer.Analyze`; a dialog
  with an **unreachable** question could therefore be published without a confirmation, even though the
  graph clearly showed it (dashed frame, badge "Unreachable", ⚠ marker). Reachability, namely, does not
  arise in the analyzer at all, but only from the layering starting at the entry question
  (`GraphLayout.AssignLayers` → `GraphNodePosition.IsReachable` → `DialogGraphBuilder`). Recomputing it in
  the `DialogEditor` would have been a second truth; instead it now holds a `DialogGraphBuilder` model in a
  field (`_graph`, **once per load** – called from the markup the whole layering would run on every click)
  and the confirmation draws from `DialogGraphModel.AllWarnings`. That makes it **structurally closed**:
  transitions, loops, a missing entry question and reachability are in, and a future
  warning kind does not fall out again. The text version lives in `Services/GraphWarningList.cs` and
  not in the `@code` block, because nothing is testable there; it sets **only** the prefix – question or
  source question by its key, loop marker by its `CollectionKey`, dialog warning
  **without** a prefix. That was exactly the trap: the old code accessed `warning.QuestionId!.Value` hard,
  and `ForDialog`/`ForLoop` return `null` there. Wordings unchanged, `GraphWarningListTests` nails
  texts *and* order (nodes before edges).
- **The canvas hung on the reading width.** `main.flirty-content` caps at 1100 px – right for text
  columns, the limiting factor for a graph editor: palette (12 rem) + canvas (base 640 px) +
  inspector (340 px) need **1204 px**, below which the inspector slips via `flex-wrap` under the
  palette, while at 2560 px over 1400 px stays empty on the right. Fixed with **one** rule in
  `wwwroot/app.css`: `main.flirty-content:has(.graph-layout) { max-width: none; }`. Deliberately so and not
  via a second layout: the test runner renders `.graph-layout` **only** in the graph branch of its
  toggle, so its history list stays narrow and readable – a `@layout` on the page could not
  distinguish that. Upward (child → ancestor) there is no cascade in Blazor; the rule must
  stand **globally** (CSS isolation does not reach into child components) and needs `main` in front so its
  specificity `(0,2,1)` beats the scoped rule `.flirty-content[b-…]` with `(0,2,0)`.
- **Two observation errors of the pass are documented in the issue**, so that they do not cost time
  again: "Reset layout" does work – the action **replaces its own button** with the
  confirmation, a selector on the old button text considers it gone; and at the shrunk
  canvas font the typographic closing quote in "auswahl" is easily read as `auswahl1`.
  **Lesson:** on the canvas the DOM is the truth (`aria-label`, `is-taken`/`is-visited`/`is-pinned`),
  overlaps are to be measured via `getBoundingClientRect`, the counter-check stands in the server log
  (`Mediator processes …Command`) – a screenshot alone does not carry the burden of proof.

**Mnemonic:** a list that enumerates instead of deriving is the bug – not its missing entry.
The confirmation named three of four warning kinds, and that was not a slip at one place, but a
selection that would have lost every further kind again. And: a reading-width cap is a statement about
**text**; where a drawing surface stands, it is a false assumption that only shows up at the large window.

**Repository language switch to English (EPIC 12, #112) done.** The whole repo is
switched from German to English (README, guides, ADRs, XML docs, comments, UI, test names). **Stage 1
(#113) done** – the language convention itself now prescribes English: this file, the six skills, the PR
template and both workflow files (`ci.yml`/`release.yml`) are English, and the three rules above (§
Conventions, its Definition of Done, § Test conventions) prescribe English comments, XML docs, commit
messages and test names. As long as those said German, every following PR correctly produced *new* German
text – flipping the rule first is what makes the rest a one-way street. **Stage 2 (#114) done** – README,
the 17 `docs/` guides and the 9 ADRs are English; the 8 ADR files were renamed via `git mv` (numbers
unchanged) and every referrer (this file, the skills, `docs/adr/README.md` and the guides) now points at
the new English slugs (`docs/adr/0001-migrations-per-provider.md` … `0008-gestures-on-the-canvas.md`).
**Stage 4 (#116) done** – the designer UI (27 `.razor`, 42 `.cs`, both JS modules, `app.css`), the chat
UI (`Flirty.Samples.Web`, incl. `app.js` banners and the auto-provisioned demo dialog) and the console
sample are English; `DesignerApp.DisplayCulture` is now `"en-US"`. Everything moved in **one** commit,
because the graph warning wordings (`TransitionWarningAnalyzer`, `LoopAnalyzer`, `DialogGraphBuilder`,
plus `DesignerExpressionContext.IdentifierNote`) are a contract: their exact texts are consumed by the
list view, the publish confirmation, the canvas **and** asserted verbatim in `tests/Flirty.Tests/Designer`
and the E2E suite – splitting UI and tests would leave the suite red in between. Four points that shaped
the work:
- **`SvgFormat` is unchanged in behavior, not in prose.** The `N()` method and its `de-DE` test stay; only
  the German XML-doc comments were translated, and reworded to say the guard is against **any**
  comma-decimal display culture (which stays configurable), not against `de-DE` specifically – with the
  default now `en-US` the immediate risk is gone, but the guard is not.
- **The AC's umlaut grep is necessary, not sufficient.** `NavMenu.razor` ("Verbindungen"/"Dialoge") and
  ~256 more lines carried umlaut-free German; a wordlist sweep alongside the grep is what actually caught
  them (and two files no agent had been assigned: `Designer/Program.cs`, several `.razor.css`).
- **#97's German pluralization flips back.** `LoopFormModel.SuggestCollectionKey` now appends `_list`
  (was `_liste`), covered by the new `LoopFormModelTests`. And `QuestionFormModel.SuggestKey`'s stems are
  English now (`choice`/`multi`/`number`/`date`/`yesno`/`text`) – that rippled straight into the E2E,
  where the SingleChoice default key `auswahl` became `choice` and the loop suggestion `auswahl_liste`
  became `choice_list`.
- **`ReconnectModal` was already English** (pre-empted under #118), so nothing to do there.
The E2E suite (17 tests) ran green **three times in a row**, and
`grep -rlE '[äöüßÄÖÜ]' src/Flirty.Designer src/Flirty.Samples src/Flirty.Samples.Web` returns nothing.

**Stage 3 (#115) was merged into the wrong branch and had to be restored under #117.** PR #122 was
merged into `docs/dz/114` instead of `main` – an hour *after* that branch had itself already reached
`main` via PR #121. Its merge commit `123ddf5` therefore has **no child** and is not an ancestor of
`origin/main`, while GitHub shows the issue as closed and the EPIC as 4/5. For four days
`src/Flirty` (108 files), `src/Flirty.AspNetCore` (22) and the three migration projects stayed German,
and the engine kept returning **German validation messages as the HTTP 400 body** to every consumer.
The restore was exact, not a re-translation: `main` had never touched those paths since (checked with
`git log origin/main --not 123ddf5 -- <paths>`), so the files were taken verbatim from `123ddf5`. The
designer and samples sources were deliberately *not* restored – #116 translated them independently
afterwards, so there `main` is the newer state. **Mnemonic:** a merged PR is not a landed PR. What
GitHub calls "Merged" only says the PR reached *its base branch*; whether that base still leads to
`main` is a separate question, and `git merge-base --is-ancestor` is the only one that answers it.
The stage-3 content itself is described in the paragraph its PR wrote: the two NuGet packages are
English throughout, including both `.csproj` `<Description>`s, and the engine's validation messages
are English from now on – the *contract* (`AnswerValidationResult` shape, status codes,
ProblemDetails) is unchanged. `ReflectionNotAllowedException` is still detected **by exception type**,
never by matching localized text.

**Stage 5 (#117) done** – and thereby EPIC 12 completed. All **537** test methods (520 unit + 17 E2E)
are English; measured, 532 were German and 5 already were not (`LoopFormModelTests`, from #116, was
the style template). The shape is unchanged – `Subject_does_something`, snake_case-ish – because the
point of the convention was never the language but that a failing test reads as a sentence in the
runner output. The transliterations (`ueberlebt`, `fuehrt`, `schliesst`) are gone outright; in English
there is nothing to work around. Along with the names went ~1100 German comment lines, ~30 local
variables, the two German helpers and the German test data. Four things worth keeping:

- **The proof is the count, not the compiler.** Two same-named parameterless methods in one class are
  CS0111, but a `[Fact]` deleted together with its doc comment is no compiler error at all. Baseline
  and counter-check therefore come from the **TRX**, and from `total`, not `executed` – a
  `Skip.IfNot` produces a result with outcome `NotExecuted`, so it counts in `total` but not in
  `executed`, which makes `total` the only number that is the same with and without Docker.
  `--list-tests` is useless here: without an `xunit.runner.json` the discovery does not pre-enumerate
  theories, so its count sits ~87 below the run. On top of the two totals runs a **per-class** diff
  (the 56 test class names are English and were not renamed, so the class is a stable key).
- **A `<see cref>` onto a test method in another class is a build error when that method is renamed.**
  CS1574, not a warning – `Directory.Build.targets` adds only CS1591 to `NoWarn`. Two of them exist
  (`GraphWarningListTests`), and they forced the renaming order.
- **A German test datum can be load-bearing.** `AnswerValueCodecTests` asserts
  `error.Contains("Zahl")` against the *core* `AnswerValidator`. With the English message that passes
  only because the datum `"keine Zahl"` is interpolated back into it. Translating the datum alone
  would have turned a green test red without anyone touching the assertion.
- **The EPIC's machine gate has a blind spot.** It filters `*.cs`/`*.razor`/`*.js`/`*.md`/`*.yml`/
  `*.html`/`*.csproj` – not `*.props`, `*.targets`, `*.runsettings`. Exactly there stood ~30 lines of
  German, in the files this document calls the *hard build conventions*, and no stage owned them.
  They went along here; the gate below is the extended one.

The gate over `src docs tests README.md CLAUDE.md .claude .github` plus the four build-configuration
files returns exactly **one** known hit: the line in the stage-4 paragraph above that quotes the
search pattern itself. That one is there by construction and is not a finding. Both suites are green
and count-identical to the baseline (608 unit + 17 E2E), and the E2E suite ran green three times in a
row.

**MCP host scaffolding (EPIC 13, #124 / stage 1 #126) done** – the **11th project** and the **third NuGet
package**: `src/Flirty.Mcp` serves the engine as a Model Context Protocol server over Streamable HTTP
(`AddFlirtyMcp()` + `MapFlirtyMcp("/mcp")`), with the ten dialog-level tools as its smoke surface. **No core
code, no schema change, no new command** – it is the same kind of thin `ISender` adapter that
`Flirty.AspNetCore` is. Reason for the separate package: ADR
`docs/adr/0009-mcp-as-its-own-opt-in-package.md`. Suite 608 → **646** (37 MCP + 1 web-sample test).
Six things that shaped the work:

- **The EPIC said "no new project is introduced"; measuring the dependency overturned that.** Hosted inside
  `Flirty.AspNetCore`, the MCP SDK plus `Microsoft.Extensions.AI.Abstractions` and
  `Microsoft.Extensions.Caching.Abstractions` become **hard dependencies of an already published package** –
  someone who wants four HTTP routes over a dialog engine would restore an AI SDK. That is ADR 0003's
  argument one layer out: web is opt-in over the core, so MCP is opt-in over web. Six infrastructure edits
  are a one-time cost; a dependency is paid forever, by people who never asked.
- **`Stateless = true` is not tuning, it is what makes clients work.** Revision `2026-07-28` removed
  `initialize` (SEP-2575) and `Mcp-Session-Id` (SEP-2567); a stateful server **refuses** those clients with
  `-32022`. The dividend is the whole reason this package needs no gateway: in stateless mode the SDK sets
  the tool call's provider to the **ASP.NET request scope**, so `ISender` and `FlirtyDbContext` resolve with
  a minimal-API endpoint's lifetime. The designer's `DesignerGateway` exists only because a Blazor circuit
  scope lives forever – there is nothing to work around here.
- **The error mapping is one `AddCallToolFilter`, and that is load-bearing.** The SDK swallows messages:
  anything not deriving from `McpException` reaches the client as `"An error occurred invoking 'x'."`. A
  call-tool filter is composed **inside** the SDK's own try/catch, so it sees the raw exception first – the
  exact structural analogue of `AddEndpointFilter` on the two route groups, which is what makes "mirrors
  `FlirtyExceptionEndpointFilter`" true by construction instead of by 32 copies of a `try`. **Worth knowing:
  this was broken in the SDK until Oct 2025** (issue #820, fixed by #844) – the exception was converted
  *before* the filter. Without that fix the whole approach is impossible, which is why the tests drive a
  real `McpClient` and not the filter delegate: a unit test of the mapping table would stay green while the
  package silently reverted. The six engine branches are verbatim, the compiler enforces their order
  (CS0160), and two MCP-only branches follow: a binder failure → 400 (over HTTP the `{id:guid}` constraint
  rejects that at routing) and a catch-all → 500 with a generic detail plus a log. `type` from
  `ProblemDetails` is deliberately **not** carried across – it points into HTTP *response* semantics – so
  parity is compared field by field, never whole-object.
- **Three of the issue's own premises were wrong, and each cost a cycle.** (1) `UseStructuredContent = true`
  is **not** the SDK default: without it every successful call returns prose only, `structuredContent` stays
  empty and no `outputSchema` is advertised. It is now set on all ten tools; ten tests were red until it
  was. (2) Enums arrive as **names** (the real advantage over HTTP, where they are integers) – but the names
  are **PascalCase**, not camelCase: `McpJsonUtilities` adds a bare `JsonStringEnumConverter` with no naming
  policy. Reading is case-insensitive, so camelCase *works*, which is exactly why the wrong claim survives
  testing unless you assert on the **schema**. (3) An **omitted** required argument never reaches
  `ValidationPipelineBehavior` – the marshaller rejects it first with `ArgumentException(ParamName:
  "arguments")`. Hence: every optional tool parameter needs an explicit `= null`, and the validation parity
  row uses an **empty** key, not a missing one.
- **`AddFlirtyMcp` returns `IMcpServerBuilder`, and that is the test seam.** Of the six mapped exceptions
  only three are reachable through dialog tools; the other three need the runtime operations of #128. So
  `FlirtyMcpTestHost` chains a test-only `flirty_test_throw` onto the returned builder – no hook in
  production code – and all six run through the real registration, the real SDK composition and the real
  wire. It is also a genuine host feature. The honest bookkeeping is written into the test's own docs: the
  AC hides two halves (H1 *same command ⇒ same exception*, H2 *same exception ⇒ same status/title/detail*),
  and the three runtime rows prove **H2 only**. The HTTP side is the real endpoint in all six.
- **The test host maps both HTTP surfaces *and* `/mcp` over one database.** That is what makes "the same
  seeded database through both surfaces" literal instead of two hosts sharing a connection string. Side
  effect worth having: these are the **first** tests in the repo to pin the HTTP `title` and `detail`
  strings – the existing endpoint tests only ever asserted status codes.

**Mnemonic:** a premise recorded in an issue is not a measurement, even when the issue says it was spiked.
All three wrong ones were *plausible* and two of them still let the tests pass by accident – the
case-insensitive enum read is the sharp example: the feature worked, the claim about it did not, and only
an assertion on the advertised **schema** could tell the difference. And a second one for new packages: of
the six places a new project must be registered, four fail **silently** – an unlisted assembly is simply
unmeasured, an unlisted package simply ships unpacked.

**MCP graph tools (EPIC 13 stage 2, #127) done** – the whole dialog configuration graph is editable over
MCP: 17 new tools in six tool classes, **one class per existing `MapXxxEndpoints` counterpart**, so the
parity AC is reviewable file-against-file instead of by counting. Plus `Tools/FlirtyToolNames.cs`, which
holds **every** wire name as a const, and `ServerInstructions`. **No core code, no schema change, no new
command, no ADR** – and, worth saying because this file trains the opposite reflex, **no new project**, so
none of the six registration places applies. Suite 647 → **708**. Six things that shaped the work:

- **A golden list that reflects both sides is not a golden list.** The checklist only pays off if exactly
  one side of the comparison is hard-coded: derive the expectation *and* the actual value from
  `FlirtyToolNames` and a renamed const changes both at once, so the test stays green through the very
  rename it exists to surface. Literals in the test force a visible three-place edit (attribute, const,
  list). What no assertion can see, and is therefore written into the test docs rather than assumed: a tool
  spelling its name as a literal instead of referencing the const emits an identical wire name.
- **An unset annotation is not a neutral annotation.** Measured: the four hints are `bool?` from the
  attribute to the wire, an omitted one is *absent*, and the protocol then lets a client assume
  `destructive: true` / `openWorld: true`. So the ten stage-1 tools looked, to every client, like each of
  them might destroy data. That is why all four are now set on all 27 rather than only on the ones the issue
  named – and the same fact makes the test sharp: **`Assert.False` accepts a `bool?` and reads `null` as
  `false`**, so the naive assertion passes on exactly this bug. Every hint is compared as
  `Assert.Equal<bool?>`. Three cells deviate from the issue's wording on purpose: `Destructive = false` on
  every create (see above), `Destructive = true` on `flirty_dialog_abandon_sessions` (ending live sessions is
  irreversible even though nothing is deleted), `Idempotent = true` on `flirty_layout_reset` (unlike the
  deletes it succeeds on an empty layout).
- **A tool class forgotten in the `WithTools<>()` chain is invisible.** It compiles, ships, and no other
  test in the suite notices – the same family as the four *silent* registration places of a new project. The
  guard is a test that compares the assembly's `[McpServerToolType]` metadata against what `tools/list`
  actually returns; it also pins that every `[McpServerTool]` sets `Name`, i.e. that `DeriveName` is nowhere
  in play.
- **The layout tool takes the batch, and that is the package's one exception to "primitives, `Guid` and
  enums only".** A model is not the canvas: the designer moves one node per gesture (ADR 0008) over HTTP,
  whereas an MCP caller that just authored a twelve-question graph arranges it in one call – one element per
  call would be twelve transactions, each answering with the *whole* layout. Admissible because it was
  measured, not assumed: the SDK generates the schema inline (no `$defs`), camelCase, `elementKind` as
  `enum: ["Question"]`, all four fields required. A test asserts that shape, because the exception stops
  being defensible the day the SDK stops generating it. The core `DialogLayoutEntry` is used directly –
  `Flirty.AspNetCore`'s copy of it exists only because HTTP needs a body wrapper.
- **`ServerInstructions` arrive – but not the way both the issue's premise and my own planning said.** The
  planning measured (by IL) that the SDK *can* copy them into `DiscoverResult.Instructions`, concluded the
  handshake-free `2026-07-28` path was covered, and wrote that into the docs. Driving the **running sample**
  with curl showed the truth: this server answers `discover` with `-32601`, and the instructions arrive via
  `InitializeResult.Instructions` because the SDK's own client **still handshakes**, negotiating
  `2025-06-18`. Stateless removed the *session header*, not the handshake. So the risk is real but latent: a
  client that speaks `2026-07-28` with per-request `_meta` lists and calls tools fine and gets **no
  instructions**. That makes the redundancy rule load-bearing rather than tidy – **every fact in the
  instructions is also in a tool or parameter `[Description]`**, which travels with `tools/list`. The
  existence of a code path is not the existence of a channel; only the wire says which one is used.
- **The issue's own count was wrong (32, not 27) and its table was right.** 32 is the state after stage 3's
  five `flirty_session_*` tools. Pre-declaring them would have made the bidirectional golden test red on the
  day it was written, and both ways out – a subtraction in the test, a `Planned` sub-class it must skip –
  hide precisely the parity bug the checklist exists for.

Along the way Tier 1 of the error-parity suite grew from three exception paths to six, and one of them is
the point rather than the count: **`DialogPublishedException` now has a real end-to-end witness** (a graph
change on a published dialog via `flirty_question_create`). The filter's catch order depends on that subtype
preceding its base; the compiler enforces the order (CS0160) but not that it is the right one, and until the
graph tools existed the exception was reachable only through the `flirty_test_throw` seam. Tier 2 did **not**
shrink – its three exceptions still need the runtime operations of #128 – so the "H2 only" caveat stands
verbatim.

**Mnemonic:** the absence of a declaration is itself a declaration. An omitted annotation hint, a tool class
missing from a registration chain, a const nobody wrote – none of them produce an error, a warning or a
failing test, and each one means something specific and wrong to whoever reads the wire. What made this
stage cheap was measuring what *absence* looks like on the client side first, and only then deciding what to
write down.

**MCP runtime and test run (EPIC 13 stage 3, #128) done** – the other half of the surface: five
`flirty_session_*` tools in `Tools/FlirtySessionTools.cs`, so a client that authors a dialog can now also
**play it through** and find out whether what it authored works. Surface 27 → **32** tools, eight tool
classes. **No core code, no schema change, no new command, no ADR, no new project** – the same thin `ISender`
adapter as the other 27, mirroring `MapFlirtyEndpoints` file-against-file. Suite 708 → **725**. Five things
that shaped the work:

- **The issue's table names `IFlirtyEngine`; the tools inject `ISender` anyway.** That column names the
  *operation*, not the injection type. The HTTP twin `FlirtyEndpointRouteBuilderExtensions` sends the
  commands over `ISender` too, and the package's whole parity claim is "one tool class per
  `MapXxxEndpoints` counterpart, reviewable file-against-file" – which only stays literally true if the two
  files say the same thing. `IFlirtyEngine` would have made one class of eight differ, and its own
  conventions doc names `ISender` as the dispatch route.
- **`OpenWorld = false` was a fact about the tools that existed, and it had been written down as a fact
  about the server.** Running a dialog publishes engine notifications, and the core's auto-registered
  `WebhookNotificationHandler` posts those to whatever absolute url a trigger names – so the four writing
  session tools reach outside the database. Declaring `false` while the DoD requires documenting "a test run
  delivers real webhooks" is a contradiction on the wire, so they declare `true` and `flirty_session_get`
  (which publishes nothing) stays `false`. This cost the annotation theory a **column**: it asserted
  `Assert.Equal<bool?>(false, …)` as a constant, which would have pinned the wrong answer for five tools
  while looking like a passing test.
- **The `mcp-test-` marker is applied by the server, and only by `flirty_session_start_version`.** Prefixing
  in the *other* start would be wrong, not merely unnecessary: that one is the production path (twin of
  `POST /flirty/sessions`), and prefixing there hands an MCP client and an HTTP client two different
  sessions for the same user. **The sharp edge is the empty string:** prefixing unconditionally turns `""`
  into a non-empty key and thereby *satisfies* the `[Required]` on `StartDialogVersionCommand`, so the 400
  the engine owes the caller never arrives and the run is stored under the bare prefix. A blank key
  therefore stays blank. `string.IsNullOrWhiteSpace` is the exactly right guard rather than an
  approximation: `RequiredAttribute` trims before testing for empty, so the two agree on every input.
- **Tier 2 of the error-parity suite is gone, and that was the point of the stage for that file.** Its three
  exceptions – dialog-not-found, session-not-found, answer-validation – proved *H2 only* because they were
  reachable on the MCP side only through the `flirty_test_throw` seam. All six engine exceptions now arise
  from the real engine on **both** sides. `FlirtyThrowingTestTools` stays regardless: four of its kinds are
  unreachable through any real tool by design, and the six-row mapping-table theory is a different claim
  from "this call path maps correctly". The answer-validation row is the one where the two surfaces answer
  **their own** session – a submitted answer advances the session, so sharing one would leave the second
  call nothing to reject.
- **`FlirtyMcpSurface.Runtime` finally means something, and a test had to invert.**
  `Surface_Runtime_registers_no_admin_tools` asserted that `tools/list` fails with `-32601`; that was the
  SDK's no-tools-no-capability semantics showing through an *empty* surface, not the flag's meaning, and it
  stopped being observable the moment the flag registered anything. It now pins the five session tools, and
  a new mirror pins the other direction — the flag has two meanings and only one was ever tested. The value
  of the split is real: `Admin` is an authoring client that touches nothing but its own database, `Runtime`
  runs dialogs for real and starts unpublished drafts.

**Mnemonic:** an invariant stated as "throughout" is a claim about the code that exists when you write it.
`OpenWorld = false` was true of 27 configuration tools and got recorded as a property of the server; the
first tool that reached outside made it a wrong declaration to every client, and no compiler, warning or
existing test could have said so — the assertion that should have caught it was itself written as the
constant. What generalizes: when a doc sentence and a test both hard-code the same "always", adding the
first exception makes the test *defend* the error.

**MCP database targets (EPIC 13 stage 4, #129) done** – the multi-database parity with the designer, and the
stage with a real design decision in it. A host declares targets by name (`o.AddTarget(...)`), a client picks
one by connecting to `/mcp/{target}`; four `flirty_db_*` tools list, test, read pending migrations and –
only under `o.AllowMigrations()` – apply them. Surface 32 → **36** tools, ten tool classes. **No core code,
no schema change, no new command, no new project**; ADR `docs/adr/0010-mcp-database-targets-by-route.md`.
Suite 725 → **765**. Six things that shaped the work:

- **The issue's own security argument is false, and the package disproves it in its own source.** It says the
  type holding a connection string "stays `internal`, so no tool result *can* serialize it – the guarantee is
  structural, not a review rule." `System.Text.Json` ignores accessibility: every result wrapper in
  `FlirtyToolResults.cs` is `internal` and reaches the client in full. Writing that sentence into ADR 0010
  would have enshrined a guarantee that does not exist, and the next person to add a field would have trusted
  it. The real guarantee is three checkable facts – `FlirtyMcpTarget` in no tool signature, a projection with
  no member that holds one, and a test asserting on the **raw serialized text** of the listing rather than on
  the projection's declaration. Asserting on the record's members would only have restated the record.
- **`ConfigureSessionOptions`, not `IHttpContextAccessor`** – and the difference is what makes AC 7 provable
  rather than careful. The accessor route works, but it needs an endpoint marker (`/mcp` and
  `/flirty/dialogs` are indistinguishable by route values, so the default target would leak into the HTTP
  endpoints), and it silently dies if a host sets `PerSessionExecutionContext = true`, which the SDK
  documents as "prevents you from using IHttpContextAccessor in handlers". The transport's session callback
  fires **only** on an MCP request, so nothing else can ever capture a target – no marker, no accessor, no
  scope factory. Its price is one guard: `MapFlirtyMcp` refuses declared targets on a stateful transport,
  where the callback would fire once per session on a scope long gone by tool time and every target would
  quietly fall back to the host database.
- **A third `FlirtyMcpSurface` flag was *cheaper* than folding the tools into `Admin`.** Not gold-plating:
  `Admin` would have moved `Surface_Admin_registers_no_session_tools` from 27 to 31 and made a database tool
  invisible to a runtime-only client for no reason, whereas `Database = 4` left all three surface tests
  untouched. The cost is that `All` changes from `3` to `7` – source-compatible, recorded in the ADR.
- **A SQLite database that does not exist is not an error for `GetPendingMigrationsAsync`.** The planned AC-5
  test pointed a target at a missing file in `Mode=ReadOnly` and expected a failure; EF answers "nothing
  applied yet" for a database it cannot find, so the call succeeded and reported every migration as pending –
  which is exactly right, and exactly what a caller wants before migrating. Only content EF cannot *read* is
  a real failure, so the test now writes a real file with garbage in it (`Pooling=False`, or the pooled
  connection outlives the host and the cleanup hits a sharing violation). Both behaviours are pinned side by
  side, because they look alike and are not.
- **Gating by absence makes the golden list conditional, and that has to be handled by widening the host, not
  by subtracting.** `flirty_db_migrate` is registered only with `AllowMigrations()`, so a default host serves
  35 of 36. Every test in `FlirtyToolSurfaceTests` now starts from one `StartFullSurfaceAsync()` helper; the
  alternative – "36 minus one" in the expectation – is the exact shape of hiding that the checklist exists to
  prevent. The gate itself is pinned where it belongs, in both directions, in the database tests.
- **The route parameter name is guarded at startup because it has no runtime symptom at all.**
  `MapFlirtyMcp("/mcp/{db}")` would match every request, never be read, and serve the default database while
  the client believed it had selected one. Contrast the *unknown target name*, which is a client input and
  therefore cannot be caught at startup – that one is a 400 whose message enumerates the declared names, and
  it is raised by a second call-tool filter rather than lazily on the first `FlirtyDbContext`, because
  `flirty_db_list_targets` never resolves a context and would otherwise answer happily on `/mcp/typo`.

**Mnemonic:** a guarantee written as prose is not a guarantee, and the more structural it *sounds* the less
anyone re-checks it. "It is `internal`, so it cannot be serialized" reads like a fact about the type system;
it is a fact about nothing, and the counter-example was already in the same folder. Before recording a
safety property, ask what would have to be true for it to fail and whether a test can see that – if the
answer is "the type would need a different modifier", the property is decoration.

**MCP round trip, guide and skill (EPIC 13 stage 5, #130) done** – and thereby **EPIC 13 / M7 completed**.
One test that *is* the EPIC's acceptance criterion, `docs/MCP.md` closed out, the seventh skill
`flirty-mcp`, and the backlog/roadmap entries no stage had owned. **No core code, no schema change, no new
command, no new tool, no ADR** – the surface stayed at 36 tools in ten classes. Suite 765 → **766**, and
that `+1` is the point of the stage rather than a small number: what was missing was never coverage of a
*tool*, it was coverage of the tools **composing**. Five things that shaped the work:

- **Most of the issue's scope was already done, and by the DoD rather than by accident.** Measured before
  writing anything: `docs/MCP.md` existed at ~700 lines with the ADR-0009/0010 reasoning, the error-mapping
  table and the core-record shape; `FlirtyMcpExceptionParityTests` had covered all six engine exceptions
  real-on-both-sides since #128, so the "error-parity theory" AC needed *verifying*, not building; and the
  README, `ARCHITECTURE.md` §3/§4/§9 and the `CLAUDE.md` docs table already carried `Flirty.Mcp` with
  absolute links. Genuinely open were the round trip, the skill, `## Conventions`, the "no E2E" rationale –
  and `docs/BACKLOG.md`/`docs/ROADMAP.md`, which had **no EPIC 13 section and no M7 at all**. That is the
  general lesson about a closing stage: the per-stage DoD makes the *guides* current and leaves exactly the
  cross-cutting indices stale, because no stage owns them. The backlog was missing EPIC 12/M6 too.
- **The round trip is built around the one thing that makes such a test die far from its cause.**
  `CreateDialogVersionCommand` gives every cloned question a **new** id (ADR 0005) and hands out no
  `questionIdMap`. A client – or a test – that carries the ids of the published version into the draft
  addresses elements of a dialog it is not running, and finds out several calls later with a 404 nowhere
  near the mistake. Every id for the runtime half therefore comes out of the clone, matched by `Key`, and
  *that the ids differ* is an assertion rather than a comment. The round trip is also the only place where
  the layout table and the version derivation are visible **together**: the arrangement survives the clone
  with its element references rewritten, which neither ADR 0005's nor ADR 0007's own tests can show.
- **It is one test on purpose, and the counter-check sits in its middle.** The per-area suites answer "does
  this tool work" and are narrow by design; split into seven, the round trip would assert seven
  preconditions it had just built itself. After publishing, `flirty_question_create` is a 409 while
  `flirty_layout_set` still succeeds – ADR 0005 and ADR 0007 in two calls, on a real graph, at the step
  where a client meets them. The focused pair in `FlirtyGraphToolsTests` stays: that one fails pointing at
  the rule, this one at the workflow step that broke it, and duplicated coverage of a *pair of rules* is
  worth its cost.
- **Of the three spike findings the issue asked to record, two were already written down – so this records
  where.** "Enums serialize as names" is in the #126 paragraph above and in `MCP.md § Conventions`; CS0718
  (`WithTools<T>()` cannot take a `static class`) is in the class docs of `FlirtyDialogTools`. Only the
  third had no home: **`CallToolResult.StructuredContent` is a `JsonElement?`**, so every read goes
  `.Value.Deserialize<T>(McpJsonUtilities.DefaultOptions)` – which is why the suite has exactly one
  `Read<T>` helper instead of that expression scattered through 160 assertions. Re-recording the other two
  in a third place would have been the same mistake as a doc that enumerates instead of deriving (#118).
- **`## Conventions` cost a decision about *where the heading goes*, not about what to move.** The guide
  already had the six convention sections; putting the heading immediately before `### Return shapes` makes
  `## Tools` end with the per-area tables and sweeps up return shapes, the three easy-to-get-wrong rules,
  the annotation matrix, the server instructions, the JSON-in-a-string payloads and the enum divergence –
  in that order, with **no text moved** and no in-doc anchor broken. Moving blocks would have produced the
  same structure and a diff nobody can review.

Along the way the doc pass found one real defect of the kind #50's mnemonic names: `FlirtyDialogTools` –
the documentation home for the conventions of *all* the tool classes – still said "one of the **eight** tool
classes" and "the **seven** others". True at #128, wrong since #129 added the two database classes, and
invisible to every test in the repo because a count in prose compiles.

**Mnemonic:** the stage that closes an EPIC finds what no stage owned. Per-issue DoD keeps the guides
honest, because each issue's own guide is in its diff – but the backlog, the roadmap and the milestone
table describe the *whole*, so they are the one thing a stage-shaped process structurally cannot keep
current. Look there first, not at the guide you just edited.
