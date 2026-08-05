# Flirty – Architecture

> Chatbot/dialog engine for .NET. The host only builds the UI; Flirty handles
> persistence, answer parsing/validation, branching, loops, resume, editable answers
> and triggers (back channels into the host app).

## 1. Goal & Motivation

Anyone building a chatbot dialog today reimplements the same things over and over: persisting
questions/answers, parsing answers, branching logic, resume and back channels into their own
app. That is repetitive and error-prone.

**Flirty** encapsulates this logic as a reusable engine. Dialogs are configured via a
**Blazor designer** (by non-technical users too). Integration into foreign apps happens through
**DI extension methods** and optionally provided **WebAPI endpoints**. A supplied
**DB connection** is migrated automatically.

## 2. Feature overview

| Feature | Implementation |
|---|---|
| Resume within a dialog | `DialogSession` + `CurrentQuestionId`, `ResumeDialogQuery` |
| Questions re-editable | `EditAnswerCommand` + path recomputation |
| Branching (multiple branches) | `Transition` + expression engine (`IExpressionEvaluator`) |
| Loops (list until breaking question) | branching cycle + `LoopDefinition` marker, iteration collection |
| Trigger after answer / completion | Mediator notifications (in-process) + outbound webhooks |
| Simple DI registration | `services.AddFlirty(o => …)` extension methods |
| DB connection + auto-migration | `o.UseSqlServer/UsePostgreSql/UseSqlite` + `o.ApplyMigrations()` |
| Optional WebAPI endpoints | package `Flirty.AspNetCore`, `app.MapFlirtyEndpoints()` |
| Designer, multi-DB | Blazor Web App, connection profile + `IDbContextFactory` |
| Designer CRUD | dialogs / questions / answers / branching / loops / triggers |
| Play through drafts | test runner in the designer + `StartDialogVersionCommand` (without publishing) |
| See the flow | read-only graph view in the designer (SVG canvas, auto-layout, inspector) |

## 3. Fundamental decisions

| Topic | Decision |
|---|---|
| Target framework | **.NET 10** (all projects) |
| DB provider | **SQLite + PostgreSQL + SQL Server**, core provider-agnostic via EF Core – [ADR 0001](./adr/0001-migrations-per-provider.md) |
| Designer hosting | **Blazor Web App, server-interactive** |
| Branching | **expression/script engine**, sandboxed (default: DynamicExpresso) – [ADR 0004](./adr/0004-sandboxed-expression-engine.md) |
| Triggers | **in-process (Mediator notifications) + outbound webhooks** |
| Mediator | **Mediator (martinothamar)** – source generator, MIT – [ADR 0002](./adr/0002-mediator-as-in-process-bus.md) |
| Endpoints | **optional**, own project `Flirty.AspNetCore`; core stays **ASP.NET-free** – [ADR 0003](./adr/0003-aspnet-free-core.md) |
| MCP server | **optional**, own project `Flirty.Mcp`; the web package stays **MCP-free** – [ADR 0009](./adr/0009-mcp-as-its-own-opt-in-package.md) |
| NuGet | `Flirty` + `Flirty.AspNetCore` + `Flirty.Mcp` as **publishable packages** |
| Documentation | XML docs (CS1591 as error) + `docs/` guides + ADRs, part of every DoD |

## 4. Solution structure

```
Flirty.sln
├─ src/
│  ├─ Flirty              Core engine (PURE class library, NO ASP.NET):
│  │                        Domain, Runtime, Persistence (EF Core),
│  │                        Mediator commands/queries/notifications,
│  │                        expression engine, triggers, DI extensions
│  ├─ Flirty.AspNetCore   OPTIONAL: WebAPI endpoint mapping (MapFlirtyEndpoints),
│  │                        thin layer over the Mediator commands
│  ├─ Flirty.Mcp          OPTIONAL: MCP server over Streamable HTTP (MapFlirtyMcp),
│  │                        the same thin layer as tools; references Flirty only
│  ├─ Flirty.Designer     Blazor Web App (server-interactive): dialog/question/answer/
│  │                        branching/loop/trigger configuration, test runner, multi-DB,
│  │                        graph canvas (SVG) with editing gestures
│  ├─ Flirty.Migrations.Sqlite       \
│  ├─ Flirty.Migrations.PostgreSql    } EF migrations per provider (IsPackable=false,
│  ├─ Flirty.Migrations.SqlServer    /    DLLs are bundled into the Flirty package)
│  ├─ Flirty.Samples      console single-project (core only + own handler)
│  └─ Flirty.Samples.Web  minimal API + static chat UI (uses Flirty.AspNetCore
│                          and Flirty.Mcp): resume/edit/branching/loop/trigger,
│                          webhook receiver, MCP server at /mcp
└─ tests/
   ├─ Flirty.Tests        xUnit unit/integration tests (EF Core SQLite in-memory)
   └─ Flirty.E2E          Playwright E2E (designer + web sample)
```

**Important:** `Flirty` has **no** ASP.NET dependency and can be plugged unchanged into a pure
console/worker app. `Flirty.AspNetCore` (`FrameworkReference
Microsoft.AspNetCore.App`) is referenced only when web/endpoints are wanted.

The same holds one layer out: `Flirty.Mcp` is its own package so that the MCP SDK does not become a hard
dependency of `Flirty.AspNetCore`. The two web packages sit **beside** each other, not on top of each
other – `Flirty.Mcp` references `Flirty` only, and either can be dropped without touching the other
([ADR 0009](./adr/0009-mcp-as-its-own-opt-in-package.md)).

## 5. Domain model (configuration)

- **Dialog** – `Id`, `Key`, `Name`, `Description`, `Version`, `IsPublished`, `StartQuestionId`, timestamps.
- **Question** – `Id`, `DialogId`, `Key`, `Text`, `Type` (SingleChoice, MultiChoice, FreeText, Number, Date, Boolean), `Order`, `IsRequired`, `ValidationRules` (JSON).
- **AnswerOption** – `Id`, `QuestionId`, `Key`, `Label`, `Value`, `Order`.
- **Transition** – `Id`, `DialogId`, `FromQuestionId`, `Expression`, `TargetQuestionId`, `Priority`, `IsDefault`. Ordered list of conditional transitions per question; the first matching one wins, otherwise the default. A `TargetQuestionId` pointing at an **earlier** question forms a **loop cycle**.
- **LoopDefinition** – `Id`, `DialogId`, `CollectionKey`, `EntryQuestionId`, `BreakingQuestionId`. Metadata/marker layer over the branching for runtime collection and designer visualization. The exit is **not** a property of its own but runs through the normal `Transition` mechanics (the breaking question's exit transition).
- **TriggerDefinition** – `Id`, `DialogId`, `Scope` (OnDialogStarted/AfterAnswer/AfterQuestion/OnDialogCompleted), `QuestionId?`, `Kind` (InProcess|Webhook), `Config` (JSON), `Expression?`.

## 6. Runtime/session state

- **DialogSession** – `Id`, `DialogId`, `DialogVersion`, `ExternalUserKey`, `Status` (InProgress/Completed/Abandoned), `CurrentQuestionId`, `StartedAt`, `CompletedAt`. → **resume**.
- **SessionAnswer** – `Id`, `SessionId`, `QuestionId`, `Value` (JSON), `AnsweredAt`, `Sequence`, `LoopInstanceId?`, `IterationIndex?`. → editable answers; loop iterations allow multiple answers per `QuestionId` (one entry per iteration).

## 7. Core services (in-process API via Mediator)

All engine operations are **Mediator commands/queries**; in-process triggers are
**Mediator notifications**. Host apps use either the facade `IFlirtyEngine` or send
commands directly via `ISender` (facade + first command implemented in #25, see [RUNTIME.md](./RUNTIME.md)).

**Commands/queries**
- `StartDialogCommand(dialogKey, externalUserKey)` → session + first question (or resume). Facade:
  `IFlirtyEngine.StartDialogAsync`. Implemented in #25, details in [RUNTIME.md](./RUNTIME.md).
  *(Deliberately without a seed parameter: to this day, start values would have no storage location in the model – the
  expression context feeds exclusively from `SessionAnswer`.)*
- `StartDialogVersionCommand(dialogId, externalUserKey)` → as above, but against a **specific
  dialog version regardless of publication status**. Facade: `IFlirtyEngine.StartDialogVersionAsync`.
  Implemented in #43 for the [test runner of the designer](./DESIGNER.md#test-runner-43) – without it a
  draft would not be playable, and "publish briefly to test" would have armed it for real users.
  **Deliberately without an HTTP endpoint**: over HTTP the publish status stays the production barrier.
  Details in [RUNTIME.md](./RUNTIME.md#startdialogversioncommand-43).
- `ResumeDialogQuery(sessionId)` → session status + current question + previous answers (read-only).
  Facade: `IFlirtyEngine.ResumeDialogAsync`. Implemented in #27, details in [RUNTIME.md](./RUNTIME.md).
  *(The resume-or-new path per user lives in `StartDialogCommand`; there is deliberately no separate
  `externalUserKey` lookup.)*
- `SubmitAnswerCommand(sessionId, questionId, value)` → validates → persists → transition evaluation → next question/completion. Facade: `IFlirtyEngine.SubmitAnswerAsync`. Implemented in #26, details in [RUNTIME.md](./RUNTIME.md). *(Publishes `AnswerSubmitted`/`QuestionAnswered`/`DialogCompleted` since #31.)*
- `EditAnswerCommand(sessionId, questionId, value)` → overwrite an earlier answer, recompute/invalidate the downstream path (may reopen a completed session). Facade: `IFlirtyEngine.EditAnswerAsync`. Implemented in #28, details in [RUNTIME.md](./RUNTIME.md). *(Publishes `DialogCompleted` on completion since #31.)*

**Notifications (= in-process triggers)** – `DialogStartedNotification`, `AnswerSubmittedNotification`, `QuestionAnsweredNotification`, `DialogCompletedNotification`. The user "plugs their handlers in" via `INotificationHandler<T>` (works 1:1 in a console app). Contracts + publication from the command handlers implemented in #31, details in [TRIGGERS.md](./TRIGGERS.md).

**Further services**
- `IExpressionEvaluator` (`Flirty.Expressions`) – expression engine `bool Evaluate(string expression, ExpressionContext context)`. Default `DynamicExpressoExpressionEvaluator` (#23). The immutable `ExpressionContext` bundles: `Answers` (by `Question.Key`), `Collections` (loop answers per iteration by `CollectionKey`), `IterationIndex`, `Now`, `Session`; values are raw JSON text (typing only in the engine). Interface + context model implemented in #22, details in [BRANCHING-EXPRESSIONS.md](./BRANCHING-EXPRESSIONS.md). Registered as a default singleton in `AddFlirty()` since #26 (first runtime consumer: transition evaluation of `SubmitAnswerCommand`); the swappable `o.UseExpressionEvaluator<T>()` overload has been available since #34.
- `IAnswerValidator` – typed, rule-based answer validation (type + `ValidationRules`), as a Mediator `IPipelineBehavior` (`AnswerValidationPipelineBehavior`) before submit/edit. Implemented in #30, details in [VALIDATION.md](./VALIDATION.md).
- Webhook `INotificationHandler` – outbound HTTP `POST` (`IHttpClientFactory` + standard resilience: retry/timeout), implemented in #33. Targets come from two additive sources: registered in code via `o.AddWebhook(scope, url, expression?)` (registration as a stub since #34) **and since #42** from the `TriggerDefinition`s configured on the dialog (`Kind = Webhook`, configuration as JSON following the `TriggerConfig` schema). The built-in `WebhookNotificationHandler` (auto-registered) filters by `TriggerScope` (for `AfterQuestion` additionally by the question), evaluates optional conditions via `IExpressionEvaluator` and delivers best-effort. Details in [TRIGGERS.md](./TRIGGERS.md#outbound-webhooks).
- `IDialogStore` – repository over `FlirtyDbContext` (implemented in #21, details in [PERSISTENCE.md](./PERSISTENCE.md#idialogstore-repository-21)).

## 8. Persistence & migrations

- **`FlirtyDbContext`** (EF Core 10), provider choice via options.
- **Migrations per provider** (EF requirement): separate migration assemblies
  `Flirty.Migrations.{Sqlite,PostgreSql,SqlServer}`; selected at runtime via `MigrationsAssembly`
  (implemented in #19, see [PERSISTENCE.md](./PERSISTENCE.md) incl. [ADR 0001](./adr/0001-migrations-per-provider.md)).
- **Auto-apply** via `o.ApplyMigrations()` → `FlirtyMigrationHostedService` (`IHostedService`) calls `Database.MigrateAsync()` on start; the migration assemblies are bundled into the `Flirty` NuGet package (implemented in #20, see [PERSISTENCE.md](./PERSISTENCE.md)).
- **Multi-DB in the designer**: connection profiles (provider + connection string) managed locally, `IDbContextFactory` opens against the chosen profile at runtime (implemented in #37; provider choice as a value via `FlirtyDatabaseProvider` + `UseFlirtyProvider`, see [DESIGNER.md](./DESIGNER.md) and [PERSISTENCE.md](./PERSISTENCE.md)).

## 9. Integration API

```csharp
// Core – enough for a pure console single-project app
services.AddFlirty(o => {
    o.UseSqlServer(conn);                 // or UsePostgreSql / UseSqlite
    o.ApplyMigrations();                  // optional: auto-migration on start
    o.UseExpressionEvaluator<MyEval>();    // expression engine swappable
    o.AddWebhook(TriggerScope.OnDialogCompleted, url);  // outbound webhook (delivery since #33)
});
// In-process trigger = Mediator notification handler:
services.AddScoped<INotificationHandler<DialogCompletedNotification>, MyDoneHandler>();

// ONLY for web/endpoints (package Flirty.AspNetCore):
app.MapFlirtyEndpoints("/flirty");

// ONLY for an MCP client (package Flirty.Mcp) – independent of the above:
services.AddFlirtyMcp();                  // does deliberately NOT call AddFlirty()
app.MapFlirtyMcp("/mcp").RequireAuthorization();
```

**Endpoints** (`Flirty.AspNetCore`): `POST /flirty/sessions`, `GET /flirty/sessions/{id}`,
`POST /flirty/sessions/{id}/answers`, `PUT /flirty/sessions/{id}/answers/{questionId}`.
`MapFlirtyEndpoints` sends the runtime commands directly via `ISender` and maps
them onto request/response DTOs; engine exceptions are mapped onto `ProblemDetails` (404/400/409).
Implemented in #35, details in [GETTING-STARTED-WebApi.md](./GETTING-STARTED-WebApi.md). The optional
**admin CRUD** (`app.MapFlirtyAdminEndpoints("/flirty/admin")`, opt-in, securable via `RequireAuthorization()`)
manages the configuration graph – dialogs (`/dialogs`, incl. `publish`/`unpublish`),
questions (`.../questions`), options (`.../options`), transitions (`.../transitions`), loop markers
(`.../loops`) and triggers (`.../triggers`) – over the same Mediator/DTO/filter mechanics. Implemented in
#36, the loop endpoints in #41, the trigger endpoints in #42; details ibid.

**MCP tools** (`Flirty.Mcp`): `services.AddFlirtyMcp()` + `app.MapFlirtyMcp("/mcp")` expose the same
engine operations as MCP tools over Streamable HTTP, so a Model Context Protocol client can configure
dialogs where the designer is the human path. It is the *same* kind of thin adapter – tools send the
Mediator commands via `ISender`, and one call-tool filter mirrors `FlirtyExceptionEndpointFilter`'s
status/title mapping – but it serializes the **core** records instead of rebuilding the DTO layer, because
a tool call is one flat argument object. Opt-in and securable via `RequireAuthorization()` for the same
reason the admin CRUD is. The transport runs **stateless** (protocol revision `2026-07-28` removed the
session header), which is also why the tools need no scope handling of their own. Implemented in #126
(host + the dialog tools), #127 (the whole configuration graph – questions, answer options, transitions,
loop markers, triggers, canvas layout – as one tool class per `MapXxxEndpoints` counterpart), #128 (the
runtime: starting, playing, reading and correcting a session, including the start-a-specific-version
operation that deliberately has no HTTP endpoint) and #129 (**database targets**: the host declares them by
name, a client picks one by connecting to `/mcp/{target}`, and only the `FlirtyDbContext` registration is
replaced – so declaring a target cannot repoint `MapFlirtyEndpoints`); details in
[MCP.md](./MCP.md), rationale for the separate package in
[ADR 0009](./adr/0009-mcp-as-its-own-opt-in-package.md), for the targets in
[ADR 0010](./adr/0010-mcp-database-targets-by-route.md).

## 10. Loops

Loops arise **over the existing branching**: a transition points at an earlier
question (cycle). The `LoopDefinition` marker achieves two things:
1. **Runtime** collects the entry question's answer per iteration under `CollectionKey` (instead of overwriting) — `SessionAnswer.LoopInstanceId`/`IterationIndex` make multiple answers per question possible. Implemented in **#29** (details in [LOOPS.md](./LOOPS.md)).
2. **Designer** visualizes the cycle as a loop block with a marked **breaking question** and warns about
   a missing or unreachable exit (infinite loop) as well as overlapping ranges. Implemented in
   **#41** (details in [DESIGNER.md](./DESIGNER.md#loop-editor-41)).

The **breaking question** is the question whose exit transition leaves the cycle; afterwards
the dialog continues normally. Break conditions and downstream branching see the
collected collection in the expression context (e.g. `positions.Count > 0`).

## 11. Design notes

1. **Mediator (martinothamar)**: source generator (no reflection overhead), MIT. Engine ops = commands/queries, triggers = notifications. Cross-cutting via `IPipelineBehavior` (logging, validation, transactions). **Implemented in #14:** the `AddFlirty()` stub wires up the Mediator (`ServiceLifetime.Scoped`) and registers the open-generic base behaviors `LoggingPipelineBehavior<,>` and `ValidationPipelineBehavior<,>` (manual registration – martinothamar's rule). The source generator runs in the core, so the `AddMediator` call must live in the core. Details see [MEDIATOR.md](./MEDIATOR.md), discarded alternatives in [ADR 0002](./adr/0002-mediator-as-in-process-bus.md).
2. **ASP.NET-free in the core**: pure console/worker usage is possible. Discarded alternatives (one package with an ASP.NET reference, `#if` variants) in [ADR 0003](./adr/0003-aspnet-free-core.md).
3. **Expression security**: no raw C# `eval`. DynamicExpresso is sandboxed (member whitelist); expressions are compiled/validated in the designer on save. Swappable via `IExpressionEvaluator` (alternative: NCalc). Discarded alternatives (Roslyn scripting, a custom grammar) in [ADR 0004](./adr/0004-sandboxed-expression-engine.md).
4. **Dialog versioning**: sessions pin their dialog version. For that to hold, a **published** version is immutable (graph changes → `DialogPublishedException`/409) and evolution runs via `CreateDialogVersionCommand` (clone as a draft, next version number); `PublishDialogCommand` retires the previously productive version. Discarded alternatives in [ADR 0005](./adr/0005-immutable-published-dialog-version.md).
5. **Loops = branching + marker**: no separate runtime special path.
6. **NuGet packaging**: `Flirty` + `Flirty.AspNetCore` with complete metadata (MIT license, icon, README), SourceLink and symbol packages (`snupkg`); the remaining projects `IsPackable=false`. Package version **date-based** (`YYYYMM.Revision`, e.g. `202604.1`), assembly version decoupled from it (`Year.Month.Revision`, UInt16 bound). Details: [NUGET-PACKAGING.md](./NUGET-PACKAGING.md).

## 12. Documentation ("everything documented")

Docs are the **definition of done of every issue**:
- XML doc comments on all public types/members; `GenerateDocumentationFile` + **CS1591 as error** (centralized in `Directory.Build.props`).
- `docs/` guides: `ARCHITECTURE.md`, `DOMAIN-MODEL.md`, `MEDIATOR.md`, `PERSISTENCE.md`, `RUNTIME.md`,
  `BRANCHING-EXPRESSIONS.md`, `LOOPS.md`, `VALIDATION.md`, `TRIGGERS.md`, `DESIGNER.md`, `MCP.md`,
  `GETTING-STARTED-Console.md`, `GETTING-STARTED-WebApi.md`, `GETTING-STARTED-Sample-Web.md`,
  `NUGET-PACKAGING.md`, `CI.md`, `ROADMAP.md`, `BACKLOG.md`. The guide with one line per guide stands
  in the `CLAUDE.md` in the repo root.
- One skill per recurring extension path under [`.claude/skills/`](../.claude/skills/) –
  `flirty-runtime-command`, `flirty-ef-migration`, `flirty-trigger-notification`, `flirty-question-type`,
  `flirty-nuget-package`, `flirty-designer`, `flirty-mcp`. They carry the *steps*; the guides carry the
  *how*, the ADRs the *why*.
- ADRs under [`docs/adr/`](./adr/README.md) – the decisions together with their **discarded alternatives**:
  [0001 migrations per provider](./adr/0001-migrations-per-provider.md),
  [0002 Mediator](./adr/0002-mediator-as-in-process-bus.md),
  [0003 ASP.NET-free core](./adr/0003-aspnet-free-core.md),
  [0004 expression engine](./adr/0004-sandboxed-expression-engine.md),
  [0005 immutable dialog version](./adr/0005-immutable-published-dialog-version.md),
  [0006 canvas technology in the designer](./adr/0006-canvas-technology-in-the-designer.md),
  [0007 layout as its own table](./adr/0007-layout-as-its-own-table.md),
  [0008 gestures on the canvas](./adr/0008-gestures-on-the-canvas.md),
  [0009 MCP as its own opt-in package](./adr/0009-mcp-as-its-own-opt-in-package.md),
  [0010 MCP database targets by route](./adr/0010-mcp-database-targets-by-route.md). Delineation: guides
  describe **how** something works and grow with the code; ADRs describe **why** it is
  the way it is, and are not rewritten (addendum or supersession instead of rewriting).
- Root `README.md` with a quickstart (console + web); code examples from the compilable samples (no
  doc drift). It is at the same time the **package page of all three NuGet packages** (`PackageReadmeFile`) – hence only
  absolute links and images from the nuget.org allowlist, recorded in
  [NUGET-PACKAGING.md](./NUGET-PACKAGING.md#the-root-readme-is-the-package-page-52) and secured by
  `tests/Flirty.Tests/Docs/PackageReadmeTests.cs`.

## 13. Verification

- **Build/test**: `dotnet build Flirty.sln`, `dotnet test` after every epic.
- **Core runtime**: unit tests for branching, loops, resume, edit path, triggers (in-process + webhook mock) against SQLite in-memory.
- **Provider**: migration + smoke CRUD against SQLite (optionally PostgreSQL/SQL Server via container).
- **Console usage**: play through the console sample without an ASP.NET reference.
- **Loops**: capture several list entries, breaking question ends it, collection in the context.
- **Web E2E**: web sample + designer via Playwright (branching, loop, resume after reload, edit) – #45/#47
  and #46; the designer's graph canvas additionally over #101–#105 (gestures, layout persistence, read mode,
  test run in the graph).
- **MCP**: a real `McpClient` over an in-process `TestServer`, both HTTP surfaces and `/mcp` on the **same**
  database – error parity per exception and one round trip that authors, publishes, versions and plays a
  dialog through over MCP alone (#130). Deliberately **no** Playwright suite: MCP has no browser surface,
  see [MCP.md § Tests](./MCP.md#tests-126130).
- **Coverage**: CI measures `Flirty` + `Flirty.AspNetCore` + `Flirty.Mcp` (coverlet + ReportGenerator,
  without a threshold gate), see [CI.md § Coverage](./CI.md#coverage).
- **NuGet**: `dotnet pack` produces both `.nupkg` (+ `.snupkg`); publishing happens via the separate,
  manually triggered workflow behind an approval gate, see
  [NUGET-PACKAGING.md § Publishing](./NUGET-PACKAGING.md#publishing-49).

---

> Backlog / issue list see [BACKLOG.md](./BACKLOG.md). Decision history under
> [`docs/adr/`](./adr/README.md).
