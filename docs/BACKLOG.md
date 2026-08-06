# Flirty – Backlog (GitHub issues)

This file is the template for creating the GitHub issues. Every `###` item = one issue.
Order = rough MVP prioritization. Architecture reference: [ARCHITECTURE.md](./ARCHITECTURE.md).

## Labels

| Label | Meaning |
|---|---|
| `type:epic` | Overarching umbrella issue |
| `type:feature` | Functional increment |
| `type:chore` | Infrastructure/setup |
| `type:test` | Test work |
| `type:docs` | Documentation |
| `area:core` | Project `Flirty` |
| `area:api` | Project `Flirty.AspNetCore` |
| `area:designer` | Project `Flirty.Designer` |
| `area:samples` | Project `Flirty.Samples` |

## Milestones

- **M1 – MVP core**: EPIC 0, 1, 2, 3, 5 (+ console sample from 8)
- **M2 – Web & triggers**: EPIC 4, 6, web sample from 8
- **M3 – Designer**: EPIC 7
- **M4 – Quality & release**: EPIC 9, 10
- **M5 – Visual graph designer**: EPIC 11
- **M6 – English repository**: EPIC 12
- **M7 – MCP server**: EPIC 13
- **M8 – Custom question types**: EPIC 14

> **Definition of Done (all issues):** code + XML docs (the build breaks on missing public-API docs, CS1591=Error) + green tests + the relevant `docs/` guide updated.

---

## EPIC 0 – Repo & solution bootstrap `type:epic` `type:chore`

### Repo scaffolding & build conventions
`type:chore`
`git init` + `.gitignore` (VisualStudio/Rider), `.editorconfig`, `Directory.Build.props`
(net10, `Nullable=enable`, `ImplicitUsings=enable`, `TreatWarningsAsErrors`,
`GenerateDocumentationFile=true`, **CS1591 as Error**), `Directory.Packages.props`
(Central Package Management: Mediator (martinothamar), EF Core 10 + provider, DynamicExpresso).
- **AC:** `dotnet build` green; missing public-API docs break the build.

### Project skeletons + solution wiring
`type:chore`
Create 6 projects and reference them in `Flirty.sln`:
`Flirty`, `Flirty.AspNetCore`→Flirty, `Flirty.Designer`→Flirty, `Flirty.Samples`→Flirty(+AspNetCore),
`Flirty.Tests`→Flirty, `Flirty.E2E` (standalone).
- **AC:** `dotnet build Flirty.sln` green; `Flirty` references **no** ASP.NET.

### Mediator setup in the core
`type:chore` `area:core`
Register Mediator (martinothamar) (source generator), base `IPipelineBehavior`
(logging/validation) as a skeleton.
- **AC:** a dummy command runs through the pipeline behavior.

### Prepare NuGet packaging
`type:chore` `area:core` `area:api`
Package metadata for `Flirty` + `Flirty.AspNetCore` (id, authors, license, README, icon),
`IsPackable` only for these two, SourceLink, `IncludeSymbols`/`snupkg`, versioning (MinVer/tag).
- **AC:** `dotnet pack` produces `Flirty.*.nupkg` + `Flirty.AspNetCore.*.nupkg` (incl. `.snupkg`).

### CI pipeline stub
`type:chore`
build + test + `dotnet pack` (GitHub Actions or Azure Pipelines).
- **AC:** pipeline green, artifacts = both `.nupkg`.

---

## EPIC 1 – Domain & persistence `type:epic` `area:core`

### Domain entities + enums
`type:feature` `area:core`
Dialog, Question, AnswerOption, Transition, **LoopDefinition**, TriggerDefinition,
DialogSession, SessionAnswer (incl. `LoopInstanceId`/`IterationIndex`).

### FlirtyDbContext + configurations
`type:feature` `area:core`
DbContext, keys, indexes, JSON columns (Value, ValidationRules, trigger config).

### Provider SQLite / PostgreSQL / SQL Server + migrations
`type:feature` `area:core`
Provider binding + migrations per provider.
- **AC:** the DB is created against each of the three providers.

### Auto-migration hosted service
`type:feature` `area:core`
`FlirtyMigrationHostedService` (active with `o.ApplyMigrations()`).

### IDialogStore repository
`type:feature` `area:core`
Repository over `FlirtyDbContext`. `test` store tests (SQLite in-memory).

---

## EPIC 2 – Expression/condition engine `type:epic` `area:core`

### IExpressionEvaluator + context model
`type:feature` `area:core`
Context: `answers`, loop collections (`CollectionKey`), `iterationIndex`, `now`, `session`.

### DynamicExpresso implementation (sandbox)
`type:feature` `area:core`
Member whitelist, no arbitrary code execution.

### Expression validation / compile check
`type:feature` `area:core`
Usable for the designer (report errors on save).
`test` expressions: operators, AND/OR, error cases, injection defense.

---

## EPIC 3 – Dialog runtime (Mediator commands) `type:epic` `area:core`

### StartDialogCommand + IFlirtyEngine facade
`type:feature` `area:core`
Start + resume of an existing InProgress session.

### SubmitAnswerCommand
`type:feature` `area:core`
Validation → persistence → transition evaluation → next question/completion → notifications.

### ResumeDialogQuery
`type:feature` `area:core`
State + answers so far.

### EditAnswerCommand + path recomputation
`type:feature` `area:core`
Jump back, overwrite, recompute/invalidate downstream answers.

### Loop runtime
`type:feature` `area:core`
Cycle detection, iteration counter, collection per iteration in `CollectionKey`,
break condition, then the normal flow; editing within an iteration.
`test` multiple iterations, breaking question, collection in context, edit in an iteration.

### IAnswerValidator (pipeline behavior)
`type:feature` `area:core`
Type + `ValidationRules`. `test` branching, resume, edit and validation tests.

---

## EPIC 4 – Triggers (notifications + webhooks) `type:epic` `area:core`

### Notification contracts + publication
`type:feature` `area:core`
`DialogStartedNotification`, `AnswerSubmittedNotification`, `QuestionAnsweredNotification`,
`DialogCompletedNotification`; publication from the command handlers.

### Convenience for in-process handlers
`type:feature` `area:core`
Docs + helper to "plug in" your own `INotificationHandler<T>` (console scenario).

### Webhook handler
`type:feature` `area:core`
Built-in `INotificationHandler` (IHttpClientFactory + retry/timeout, `TriggerDefinition`-driven).
`test` dispatch + webhook (mock HttpMessageHandler).

---

## EPIC 5 – DI extensions & options `type:epic` `area:core`

### AddFlirty(...) extension method
`type:feature` `area:core`
Mediator registration, provider choice, `ApplyMigrations`, webhook registration,
`UseExpressionEvaluator`.
`test` registration/resolve tests incl. a pure console setup without ASP.NET.

---

## EPIC 6 – WebAPI endpoints (`Flirty.AspNetCore`) `type:epic` `area:api`

### Project + MapFlirtyEndpoints + DTOs
`type:feature` `area:api`
`FrameworkReference Microsoft.AspNetCore.App`; `MapFlirtyEndpoints` sends Mediator commands;
DTOs for Start/Resume/Answer/Edit.

### Optional admin CRUD endpoints
`type:feature` `area:api`
Dialogs/questions/options/transitions. `test` integration tests (`WebApplicationFactory`).

---

## EPIC 7 – Designer (Blazor) `type:epic` `area:designer`

### Connection-profile management (multi-DB)
`type:feature` `area:designer`
Multiple profiles, test-connection, migrate button, `IDbContextFactory` selection.

### Dialog CRUD UI
`type:feature` `area:designer`

### Question editor
`type:feature` `area:designer`
Type, order, validation, answer options.

### Branching editor
`type:feature` `area:designer`
Transitions + expression builder + live validation via `IExpressionEvaluator`.

### Loop visualization
`type:feature` `area:designer`
Cycle as a loop block, mark the breaking question, edit the `CollectionKey`;
warning on a cycle without a reachable exit condition (infinite loop).

### Trigger editor
`type:feature` `area:designer`

### Test runner
`type:feature` `area:designer`
Play a dialog through in the designer (incl. loop iterations).
- **AC:** a non-technical user can create a dialog including a loop end-to-end.

---

## EPIC 8 – Sample apps `type:epic` `area:samples`

### Console single-project sample
`type:feature` `area:samples`
Core only + a custom `INotificationHandler`; play a dialog through via the facade (no ASP.NET).

### Web sample (minimal API + chat UI)
`type:feature` `area:samples`
Consumes the endpoints; shows resume/edit/branching/**loop over a list**/triggers;
example handler + webhook receiver.

---

## EPIC 9 – Tests, CI & publish `type:epic` `type:test`

### Playwright E2E designer
`type:test`
Create a dialog → branching → loop → save.

### Playwright E2E web sample
`type:test`
Play branching + loop through, reload→resume, edit a previous answer.

### Coverage report in CI
`type:chore`

### NuGet publish
`type:chore` `area:core` `area:api`
`dotnet pack` + push (feed configurable: NuGet.org or Azure Artifacts).
- **AC:** both packages published incl. symbols.

---

## EPIC 10 – Doc guides & ADRs `type:epic` `type:docs`

### docs/ guides
`type:docs`
`ARCHITECTURE.md`, `GETTING-STARTED-Console.md`, `GETTING-STARTED-WebApi.md`, `DESIGNER.md`,
`BRANCHING-EXPRESSIONS.md`, `LOOPS.md`, `TRIGGERS.md`, `NUGET-PACKAGING.md`.

### ADRs
`type:docs`
`docs/adr/`: Mediator, ASP.NET-free core, expression engine, migrations per provider.

### Root README (quickstart)
`type:docs`
Console + web quickstart, snippets from the samples.

---

## EPIC 11 – Visual graph designer (canvas) `type:epic` `area:designer` `area:core`

The designer configures a **graph** but shows it as a stack of forms – the flow of a
dialog is not readable from that. The goal is a canvas view with the existing editors as the
inspector. The form and list paths remain **fully intact**; the canvas is additional,
not a replacement. Cut into six stages each deliverable on its own (#99).

### Spike: canvas technology
`type:spike` `area:designer`
In-house SVG against `Z.Blazor.Diagrams` on the same yardstick (30 nodes / 45 edges, a throttled
circuit, license, build under `TreatWarningsAsErrors`). The result is an ADR, not code.

### Graph view of the dialog (reading)
`type:feature` `area:designer`
`/dialogs/{id}/graph`: questions as nodes, all transitions as labeled edges, entry,
completion and unreachable questions marked, loops as a range over the `LoopAnalyzer` body,
triggers as chips, warnings at the affected element. Deterministic auto-layout, pan/zoom,
inspector panel. No new core code.

### Layout persistence + moving nodes
`type:feature` `area:core` `area:designer`
Table `DialogLayout` including a migration for all three providers, `SetDialogLayoutCommand` /
`ResetDialogLayoutCommand` (**without** `DialogEditGuard` – coordinates do not touch the session semantics),
`Layout` in `DialogDetail` + admin endpoints, clone branch in `CreateDialogVersionCommand`,
cleanup branch in `DeleteQuestionCommand`. Its own ADR.

### Editing on the canvas
`type:feature` `area:designer`
Drag a question type from a palette, connect from a port to a node, delete (confirmed), toggle the
default, priority by order, create a loop from a cycle, triggers via a context menu.
A published dialog ⇒ read mode (moving stays allowed, ADR 0005).

### Test run in the graph
`type:feature` `area:designer`
The test runner (#43) highlights the taken path, shows the iteration count on the loop frame and
lets triggers from `DesignerTriggerLog` flash at the triggering node.

### Playwright E2E of the canvas
`type:test` `area:designer`
Creation flow, test run and read mode in the browser – analogous to #46. The canvas waits for `data-canvas-ready`
instead of the retry pattern `InteractWhenReadyAsync` (gestures are not idempotent).

---

## EPIC 12 – Translate the repository to English `type:epic` `area:core` `area:api` `area:designer` `area:samples`

Flirty ships as two public NuGet packages, but every piece of prose in it was German – the README
(which is the description page of **both** packages), the guides, the ADRs, the XML docs a consumer
sees in IntelliSense, the designer UI and the engine's validation messages, which reach end users
over HTTP. That made the packages effectively usable by German-speaking developers only.

**No breaking change:** every identifier in `src` was already English (`QuestionType`,
`SetDialogLayoutCommand`, `IFlirtyEngine.SubmitAnswerAsync`) – only the prose was German. So no public
type or member is renamed, no consumer has to touch their code, and no EF migration is involved. Cut
into five stages (#112); multi-language support (`.resx`, `IStringLocalizer`) is explicitly **not** a
goal – this is a switch, not a localization feature.

### Switch the language convention
`type:chore`
`CLAUDE.md`, the six skills, the PR template and both workflow files. Goes **first**: as long as the
convention prescribes German, every following PR correctly produces *new* German text.

### README, docs/ guides, ADRs
`type:docs`
The README, the 17 guides and the 9 ADRs; the ADR files renamed via `git mv` (numbers unchanged) with
every referrer pointed at the new slugs.

### XML docs, comments and engine messages in the packages
`type:chore` `area:core` `area:api`
`Flirty` + `Flirty.AspNetCore` throughout, including both `.csproj` `<Description>`s. The engine's
validation messages become English and go out as the HTTP `400` body – the *contract*
(`AnswerValidationResult` shape, status codes) is unchanged.

### Designer and sample UIs
`type:chore` `area:designer` `area:samples`
The designer UI, the chat UI and the console sample, plus `DisplayCulture`. Has to move in **one**
commit: the graph warning wordings are a contract asserted verbatim by the unit and E2E suites.

### Test names
`type:test`
All test methods, comments, local variables and test data. Goes **last**, so the renames do not
collide with any stage still in flight. The proof is an unchanged test count, measured from the TRX
`total` and diffed per class.

---

## EPIC 13 – MCP server exposing all designer actions `type:epic` `area:core` `area:api` `area:designer`

Everything an operator can do in the Blazor designer, drivable by an **MCP client**: dialogs, questions,
answer options, branching, loops, triggers, layout, publishing/versioning, a test run and
selecting/testing/migrating a target database. A **thin adapter over the existing Mediator commands**,
exactly as `Flirty.AspNetCore` already is – no new engine logic, no schema change, no new command in any
of the five stages. The designer is **not** replaced: MCP is an alternative client, both stay valid.

Two premises of the original issue text were overturned by measuring, and both are recorded as ADRs
rather than edited away:

- **"No new project is introduced" → `src/Flirty.Mcp` is its own package.** Inside `Flirty.AspNetCore`
  the MCP SDK plus `Microsoft.Extensions.AI.Abstractions` would become hard dependencies of an
  already published package – so someone who wants four HTTP routes over a dialog engine would restore
  an AI SDK. That is ADR 0003's argument one layer out: web is opt-in over the core, so MCP is opt-in
  over web. ADR 0009.
- **"The active DB is held per MCP session" → the host declares named targets, the client picks one per
  route.** Not a preference but a protocol fact: revision `2026-07-28` removed the `initialize`
  handshake and the `Mcp-Session-Id` header, so there is no session to hold a selection in – and behind
  a load balancer a `select` + `edit` pair would edit the wrong database. ADR 0010.

### MCP host scaffolding
`type:feature` `area:api`
The packable project, `AddFlirtyMcp()`/`MapFlirtyMcp()`, the `AddCallToolFilter` error mapping (mirrors
`FlirtyExceptionEndpointFilter`), the ten dialog tools as a smoke surface, sample wiring, the in-process
`McpClient` test host, ADR 0009 and the six infrastructure edits a new packable project needs – four of
which fail **silently** when forgotten.

### Tools for the dialog configuration graph
`type:feature` `area:api`
Questions, answer options, transitions, loop markers, triggers, layout – 17 tools, **one class per
existing `MapXxxEndpoints` counterpart**, so the parity claim is reviewable file against file instead of
by counting. Plus `FlirtyToolNames` as the single checklist and the server instructions.

### Tools for the dialog runtime and test run
`type:feature` `area:api`
The five `IFlirtyEngine` operations including `StartDialogVersionAsync`, which deliberately has **no**
HTTP endpoint: over HTTP the publish status stays the production barrier, but without it a draft is
untestable. Sessions it writes carry the `mcp-test-` prefix. Independent of the graph stage.

### Database targets
`type:feature` `area:api`
Host-declared named targets (`o.AddTarget`), a client picks one by connecting to `/mcp/{target}`; list /
test connection / pending migrations / migrate, the last one gated by `o.AllowMigrations()` and left out
of the registration entirely when off. ADR 0010. Lands **after** the two tool stages deliberately:
because the target comes from the route, no tool method mentions it and neither stage needs a rewrite.

### Round-trip test, docs/MCP.md and the flirty-mcp skill
`type:test` `type:docs`
Goes **last**. One test that *is* the EPIC's acceptance criterion – author, publish, counter-check the
publish lock against the layout exception, derive a version, play the draft through both branches with
two loop iterations, correct one, resume, finish – plus the HTTP-vs-MCP error-parity theory over all six
engine exceptions, the guide, the skill and the context sync. Deliberately **no** `tests/Flirty.E2E`
coverage: MCP has no browser surface.

---

## EPIC 14 – Custom question types declared by the host `type:epic` `area:core` `area:api` `area:designer` `area:samples`

An embedding application can define its **own question type** – with its own validation logic, authorable
over HTTP, MCP and the designer – without forking the engine. `QuestionType` is a closed `public enum`
inside a published NuGet package, so before this a host had three bad options: fork, replace
`IAnswerValidator` wholesale and reimplement all six built-in types, or disguise the type as `FreeText`
and guess the control from the question key.

**The measurement that shaped the whole EPIC**: the skill `flirty-question-type` promised that a new
question type is three steps. Counted, `QuestionType.<Member>` appears **98 times across 15 files** in
`src` – the three steps are what the *core* costs, the other twelve files are what a type costs to
become usable. An extension point has to be **cheaper** than the built-in path, so the enum is not
widened per type: one open-shaped built-in (`QuestionType.Json = 6`) is added and host-declared types
hang off it via `Question.CustomTypeKey`. ADR 0011.

Two premises of the issue text were overturned by measuring, and both are recorded rather than edited
away:

- **"A JSON Schema in `ValidationRules`" → dropped.** The amendment scoped it in on the premise that
  `JsonSchema.Net` is MIT. It is, up to 8.0.5; from 9.0.0 the binary release carries the Open Source
  Maintenance Fee EULA with `requireLicenseAcceptance=true` and a fee above roughly US$10k revenue –
  which would flow transitively to **every** consumer of `Flirty`. Freezing on 8.0.5, adding a fourth
  opt-in package, or passing the fee on are each more expensive than the feature. Structure stays where
  the original text put it: in the custom type's own validator. Recorded as a discarded alternative in
  ADR 0011.
- **"A misconfiguration is a 500" → it is a 409.** `FlirtyExceptionEndpointFilter` maps
  `InvalidOperationException` to `409 Conflict`, and the MCP filter agrees.

### Core: the open base type, the registry and the DI decorator
`type:feature` `area:core`
`QuestionType.Json`, the `Question.CustomTypeKey` column (the repo's first `AddColumn`, in all three
provider sets), `o.AddQuestionType(...)` with `IQuestionTypeValidator`, and the scoped
`CustomQuestionTypeAnswerValidator` that decorates `IAnswerValidator` – registered **only** on an actual
declaration, so a host that does not use the feature keeps the plain singleton. An unknown key
**degrades** to the plain JSON check plus one warning, because a published dialog cannot be repaired
(ADR 0005). ADR 0011.

### HTTP and MCP: the key on the wire, and a way to discover the types
`type:feature` `area:api`
`CustomTypeKey` through the DTOs, both route groups and the two MCP question tools, plus the new
read-only `flirty_question_type_list` in its own tool class – it has no `MapXxxEndpoints` counterpart
because its source is the host's registry rather than a route. Surface 36 → 37 tools.

### Designer: JSON questions read-only, custom types fully authorable
`type:feature` `area:designer`
No input control for a `Json` question and no answering it in the test runner – a **documented limit**,
because a test run writes a real session and the designer does not know the host's registry. What it
does get is complete authoring: `CustomTypeKey` as a plain text field in both surfaces. Along the way
the measurement that no single expression sample shape works for a JSON answer, so the save check
accepts either. Richer designer support is #137.

### Samples, docs and the skill
`type:feature` `type:docs` `area:samples`
The web sample declares **two** types of different shape – scalar `color` and composite `address` – and
plays them through the chat UI, which picks its control from the `customTypeKey`. That is what shows the
value stays opaque to the engine. Plus the guides, and the rewrite of the skill this EPIC measured as
materially incomplete: two paths, the host path first.
