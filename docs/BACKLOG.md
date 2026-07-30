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
