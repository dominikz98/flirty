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

## Solution layout (`Flirty.sln`, 10 projects)

```
src/
├─ Flirty                     Core engine. PURE class library, NO ASP.NET. NuGet package.
│                               Domain, Persistence (EF Core), Runtime (Mediator), Expressions,
│                               Validation, Pipeline, Hosting, DependencyInjection.
├─ Flirty.AspNetCore          OPTIONAL: WebAPI endpoints (thin over the Mediator commands). NuGet package.
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
                                Flirty.AspNetCore); resume/edit/branching/loop/trigger + webhook receiver.
tests/
├─ Flirty.Tests               xUnit unit/integration tests.
└─ Flirty.E2E                 Playwright E2E of the web-sample chat UI (#45/#47) and the designer (#46).
```

**Invariant:** The core (`Flirty`) has **no** ASP.NET dependency and runs unchanged in
console/worker. Web is opt-in via `Flirty.AspNetCore` (`FrameworkReference Microsoft.AspNetCore.App`).

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
- **Packaging:** only `Flirty` + `Flirty.AspNetCore` set `IsPackable=true`; all others inherit
  `false`. The package version is **date-based** `YYYYMM.Revision` (e.g. `202607.1`); never bump it
  manually. Details: `docs/NUGET-PACKAGING.md`.
- **Convention:** new domain/runtime types preferably `sealed record`/`sealed class`, `internal` where
  not part of the public API. Timestamps always **UTC** (`DateTimeOffset`, `UtcNow`).

## Architecture invariants

- **CQRS via Mediator (martinothamar, source generator).** Engine operations = commands/queries,
  triggers = `INotification`, cross-cutting = `IPipelineBehavior`. **The source generator only discovers
  handlers in the core compilation** → all commands/queries/handlers/notification contracts **and**
  the `AddMediator` call live in `Flirty`. Open-generic behaviors are registered **manually**.
  Reason: ADR `docs/adr/0002-mediator-as-in-process-bus.md`.
- **ASP.NET-free in the core** (see above). Reason: ADR `docs/adr/0003-aspnet-free-core.md`.
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
`flirty-nuget-package`, `flirty-designer`.

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
| Answer validation | `docs/VALIDATION.md` |
| NuGet packaging | `docs/NUGET-PACKAGING.md` |
| CI pipeline | `docs/CI.md` |
| Getting Started (Console / WebAPI) | `docs/GETTING-STARTED-Console.md`, `docs/GETTING-STARTED-WebApi.md` |
| Getting Started (Web sample / chat UI) | `docs/GETTING-STARTED-Sample-Web.md` |
| Designer (Blazor) | `docs/DESIGNER.md` |
| Backlog / roadmap | `docs/BACKLOG.md`, `docs/ROADMAP.md` |
| Decisions (ADRs) | `docs/adr/README.md` (index + format), ADRs 0001–0008 |

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
(`Editieren_der_Verzweigungsfrage_wechselt_den_Zweig`) – not a product bug, but a race in the test.
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
smoke tests from #101–#104 (`Testlauf_im_Graphen_hebt_den_gelaufenen_Pfad_hervor` = test run,
`Graph_Gesten_sind_bei_veroeffentlichtem_Dialog_deaktiviert` + `Graph_Knoten_verschieben_ueberlebt_den_Reload`
= read mode). Added were the two that `docs/DESIGNER.md` had named "deliberately open for #105":
`Graph_Anlege_Flow_auf_dem_Canvas_ueberlebt_Veroeffentlichen_und_Reload` (palette drag, **twice
dragging into the void**, a condition with live validation, default, entry question, moving, publishing,
reload incl. positions) and `Graph_Inspector_legt_Trigger_und_Schleife_am_Zyklus_an`. The suite thereby
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

**Repository language switch to English (EPIC 12, #112) – in progress.** The whole repo is being
switched from German to English (README, guides, ADRs, XML docs, comments, UI, test names). **Stage 1
(#113) done** – the language convention itself now prescribes English: this file, the six skills, the PR
template and both workflow files (`ci.yml`/`release.yml`) are English, and the three rules above (§
Conventions, its Definition of Done, § Test conventions) prescribe English comments, XML docs, commit
messages and test names. As long as those said German, every following PR correctly produced *new* German
text – flipping the rule first is what makes the rest a one-way street. **Stage 2 (#114) done** – README,
the 17 `docs/` guides and the 9 ADRs are English; the 8 ADR files were renamed via `git mv` (numbers
unchanged) and every referrer (this file, the skills, `docs/adr/README.md` and the guides) now points at
the new English slugs (`docs/adr/0001-migrations-per-provider.md` … `0008-gestures-on-the-canvas.md`).
**Stage 3 (#115) done** – the two NuGet packages (`Flirty`, `Flirty.AspNetCore`) are English throughout:
all XML docs, comments and German string literals, and both `.csproj` `<Description>`s (what nuget.org
shows under the package title, effective from the next published version, not retroactively). The
umlaut gate `grep -rlE '[äöüßÄÖÜ]' src/Flirty src/Flirty.AspNetCore --include='*.cs' --include='*.csproj'`
now returns nothing. **The engine's validation messages are English from now on** – `AnswerValidator`
produces them and `Flirty.AspNetCore` returns them as the `400` body, so a host that pipes them straight
into its UI sees the language flip; the *contract* (`AnswerValidationResult` shape, status codes) is
unchanged. Delicate points: five test assertions tracked a German package message and moved in the same
commit (`DialogVersioningTests` "multiple versions"/"1 session(s)"/"published",
`MapFlirtyAdminEndpointsTests` "new version"/"1 session(s)") – the DeleteDialog wording is now
`{n} session(s)` (lowercase); and the `ReflectionNotAllowedException` message (#97) was translated while
detection stays **by exception type**, never by matching localized text, and the message deliberately
never names `EnableReflection`. Also swept in this stage: comments/XML docs (not UI string literals) in
Migrations, the Designer service/model layer and the Samples – their UI strings stay German for stage 4,
so those projects are deliberately not umlaut-free yet. `docs/VALIDATION.md` describes behavior and quotes
no message text, so it needed no change. **Open:** stage 4 (#116: designer and sample UIs, incl.
`DisplayCulture` and the English `ReconnectModal` handed off from #118) and stage 5 (#117: the 553 test
names, last so the renames do not collide).
