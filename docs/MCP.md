# MCP server (`Flirty.Mcp`)

`Flirty.Mcp` exposes the Flirty engine as a **Model Context Protocol** server over Streamable HTTP, so
that an MCP client can do what an operator does in the Blazor designer. Like `Flirty.AspNetCore` it is a
**thin adapter over the existing Mediator commands** – no engine logic, no new command.

> **Build-out status: EPIC 13 is complete.** This guide describes the host and its **37 tools** – the 36
> of EPIC 13 plus `flirty_question_type_list`, added by #136 for host-declared question types.
> Twenty-seven of them are the configuration surface – the ten dialog-level ones (stage 1, #126) plus the
> whole graph: questions, answer options, transitions, loop markers, triggers and the canvas layout
> (stage 2, #127). Five are the runtime surface (stage 3, #128), which plays a dialog through. The last four
> are the database targets (stage 4, #129), and one of those is registered only on request. Stage 5 (#130)
> added no tool: it closed the EPIC with the round trip that *is* the acceptance criterion
> ([§ Tests](#tests-126130)) and this guide.

Why it is a package of its own rather than a folder in `Flirty.AspNetCore`:
[ADR 0009](./adr/0009-mcp-as-its-own-opt-in-package.md).

## Setup (#126)

Two calls, both opt-in, mirroring `AddFlirty` / `MapFlirtyEndpoints`:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddFlirty(o => o.UseSqlServer(conn).ApplyMigrations());
builder.Services.AddFlirtyMcp();

var app = builder.Build();
app.MapFlirtyMcp("/mcp").RequireAuthorization();
app.Run();
```

- **`AddFlirtyMcp` deliberately does not call `AddFlirty()`.** The provider and connection string are the
  host's decision; calling it here would silently pick defaults. A registered Flirty stack is a
  prerequisite, exactly as it is for `MapFlirtyEndpoints`.
- **`MapFlirtyMcp` returns the SDK's `IEndpointConventionBuilder`**, so `RequireAuthorization()` chains.
  The tools include write operations – publishing a dialog, deleting one – so securing the endpoint is
  recommended as loudly as it is for `MapFlirtyAdminEndpoints`. Mapping without `AddFlirtyMcp` throws an
  `InvalidOperationException` at startup rather than serving a broken endpoint.
- **`AddFlirtyMcp` returns the SDK's `IMcpServerBuilder`**, so a host can add its own tools, prompts and
  filters to the same server. Flirty's error mapping is registered as the first call-tool filter and
  therefore composes *outermost* – it wraps whatever the host adds too.

`FlirtyMcpOptions` scopes the surface:

```csharp
builder.Services.AddFlirtyMcp(o =>
{
    o.Surface = FlirtyMcpSurface.Admin;   // Runtime | Admin | Database | All (default), or None
    o.ServerName = "Flirty";
});
```

The three surfaces are worth choosing between rather than defaulting past. `Admin` is configuration only and
touches nothing but its own database; `Runtime` **runs dialogs for real** – it writes sessions, delivers
configured webhooks, and one of its tools starts an unpublished draft (see *A test run is a real run*
below); `Database` is the target administration described in the next section. A host that wants an
authoring client and nothing else registers `Admin`. Note also that a server
with **no** tools at all advertises no tools capability, which makes `tools/list` itself unavailable
(JSON-RPC `-32601`) – that is the SDK's semantics, not a Flirty decision.

The sample serves the endpoint at `/mcp` (`src/Flirty.Samples.Web/WebSampleApp.cs`) – deliberately
**without** `RequireAuthorization()`, as the admin endpoints there are.

## `Stateless = true`, and it is set explicitly

The transport is registered as `WithHttpTransport(t => t.Stateless = true)`, even though that is the
SDK's current default. Two reasons, both worth knowing:

- Protocol revision `2026-07-28` removed the `initialize` handshake (SEP-2575) and the `Mcp-Session-Id`
  header (SEP-2567) from the wire format. A **stateful** server refuses those clients with
  `-32022 UnsupportedProtocolVersion`. Stateless is not a tuning choice; it is what makes current clients
  work at all.
- In stateless mode the SDK sets the tool call's service provider to the **ASP.NET request scope**. So a
  tool resolves `ISender` and the `FlirtyDbContext` with byte-for-byte the lifetime story of a minimal-API
  endpoint. **That is why `Flirty.Mcp` needs no gateway and no `IServiceScopeFactory`** – the designer's
  `DesignerGateway.ExecuteInScopeAsync` exists only because a Blazor circuit scope lives for the whole
  connection and would pin the `DbContext` to the first-used connection profile. The transport already hands
  us the right scope.

The default has moved once already, hence the explicit setting.

## Database targets (#129)

A host can serve several databases from one MCP server. It **declares** them by name; a client **picks**
one by connecting to the route that carries the name. Nothing else selects a database, and in particular
no tool argument does.

```csharp
builder.Services.AddFlirty(o => o.UseSqlite(conn));      // the host's own database
builder.Services.AddFlirtyMcp(o =>
{
    o.AddTarget("staging", FlirtyDatabaseProvider.PostgreSql, stagingConn, "nightly restore");
    o.AddTarget("local", FlirtyDatabaseProvider.Sqlite, "Data Source=local.db");
    o.UseDefaultTarget("staging");   // optional: what /mcp serves
    o.AllowMigrations();             // default: off
});

var app = builder.Build();
app.MapFlirtyMcp("/mcp").RequireAuthorization();           // the default target
app.MapFlirtyMcp("/mcp/{target}").RequireAuthorization();  // a named target
```

Both routes are ordinary endpoints, so each needs its own `RequireAuthorization()`. Reasoning for the whole
arrangement: [ADR 0010](./adr/0010-mcp-database-targets-by-route.md). Four consequences are worth having in
front of you before reading the tools.

**Why the route and not a tool argument, and why there is no `select_target` tool.** Revision `2026-07-28`
removed sessions from the wire, so there is nowhere to hold a selection between two calls; behind a load
balancer a `select` followed by an `edit` would reach two different processes and the edit would land in
the wrong database. The route decides per connection, which is idempotent by construction – and it keeps a
`target` parameter off all 37 tool schemas, so not one tool of stages 2 and 3 changed when this landed.

**No connection string ever crosses the wire.** `flirty_db_list_targets` reports name, provider,
description and `isDefault`, and nothing else. The type that holds a connection string, `FlirtyMcpTarget`,
appears in no tool signature. Note what does *not* guarantee this, because it is an easy assumption:
`internal` is no barrier – `System.Text.Json` serializes internal types happily, as every result wrapper in
`FlirtyToolResults.cs` demonstrates. The guarantee is the two facts above plus a test that reads the **raw
serialized text** of the listing and asserts there is no `Data Source` in it.

**Declaring a target does not repoint anything else.** Only the `FlirtyDbContext` registration is replaced,
and only when at least one target is declared; the `DbContextOptions<FlirtyDbContext>` that `AddFlirty`
registered stay in place as the fallback. So `MapFlirtyEndpoints`, `MapFlirtyAdminEndpoints` and
`FlirtyMigrationHostedService` keep talking to the host's own database, and a scope opened outside a
request does too. That is structural rather than careful: the target is captured in the transport's
per-request session callback, which fires **only** on an MCP request, so nothing else can ever see one.
Declaring no target at all registers no replacement, and the single-database path is untouched.

**Naming a target that is not declared is a validation error**, never a silent fallback – on a
multi-database server *and* on a single-database one. The message enumerates the declared names, so the
error carries what the client came for. It is raised by a second call-tool filter composed inside the error
filter, which is why it applies to every tool, `flirty_db_list_targets` included.

Two smaller rules the compiler cannot state: a target name must be routable (ASCII letters, digits, `.`,
`_`, `-`), and `MapFlirtyMcp` refuses a pattern whose route parameter is not called `target` – `{db}` would
simply never be read, and the client would work against the default database without any symptom. Lookup is
case-insensitive, because route values are.

## Tools (#126–#129)

Names are `flirty_<area>_<action>`. **37 tools in eleven tool classes** – 28 of configuration, 5 of runtime
and 4 of database administration – and the split is not cosmetic: for 32 of them there is **one tool
class per existing `MapXxxEndpoints` counterpart**, which is what makes the parity claim reviewable file
against file instead of by counting. `FlirtySessionTools` is the eighth, and it mirrors
`MapFlirtyEndpoints`, the runtime route group. The three classes **without** such a counterpart are
`FlirtyDatabaseTools`, `FlirtyDatabaseMigrationTools` (#129) and `FlirtyQuestionTypeTools` (#136) – each
because the engine has no route for what it reports: the first two mirror the designer's connection
profiles, the third reads the registry `AddFlirty` built from the host's `AddQuestionType` calls.

The two database classes are the exception to that rule, and the exception is honest rather than
awkward: the engine has **no command** for "is this database reachable?", so they have no endpoint group
to mirror. What they mirror instead is the designer's `ConnectionProfileOperations`, and the comparison
lives in one file, `FlirtyMcpDatabaseOperations.cs`. They are two classes rather than one only because
`WithTools<T>()` takes a class as its unit and `flirty_db_migrate` is registered conditionally.

Every name lives as a `const string` in `Tools/FlirtyToolNames.cs` – **the single parity checklist** – and
every `[McpServerTool]` takes its `Name` from there. Never let the SDK derive one: `DeriveName` strips an
`Async` suffix and snake_cases the method name, so a C# rename, a refactoring that touches no contract,
would silently rename a tool for every client. The checklist is not a copy of a list either: the golden test
reflects over the literal fields of that class and compares them with `tools/list` **in both directions**,
so a const without a tool fails as loudly as a tool without a const.

### Dialogs

| Tool | Command / query | Returns |
|---|---|---|
| `flirty_dialog_create` | `CreateDialogCommand` | `DialogSummary` |
| `flirty_dialog_list` | `ListDialogsQuery` | `FlirtyDialogList` |
| `flirty_dialog_get` | `GetDialogQuery` | `DialogDetail` |
| `flirty_dialog_update` | `UpdateDialogCommand` | `DialogSummary` |
| `flirty_dialog_delete` | `DeleteDialogCommand` | `FlirtyAck` |
| `flirty_dialog_publish` | `PublishDialogCommand` | `DialogSummary` |
| `flirty_dialog_unpublish` | `UnpublishDialogCommand` | `DialogSummary` |
| `flirty_dialog_create_version` | `CreateDialogVersionCommand` | `DialogDetail` |
| `flirty_dialog_abandon_sessions` | `AbandonDialogSessionsCommand` | `AbandonSessionsResult` |
| `flirty_dialog_count_active_sessions` | `CountActiveSessionsQuery` | `FlirtyActiveSessionCount` |

`CountActiveSessionsQuery` deliberately has **no HTTP endpoint** (it is an operating aid, not part of the
runtime or CRUD surface), so MCP is its first transport beside the designer.

The publish rules are unchanged and come from the engine, not from this layer: a published version's graph
is locked ([ADR 0005](./adr/0005-immutable-published-dialog-version.md)), so a graph change on it is a
409, and `flirty_dialog_create_version` is the way forward. Deleting a dialog with running sessions is
refused until `flirty_dialog_abandon_sessions` has ended them.

### Questions and answer options

| Tool | Command | Returns |
|---|---|---|
| `flirty_question_create` | `CreateQuestionCommand` | `QuestionDetail` |
| `flirty_question_update` | `UpdateQuestionCommand` | `QuestionDetail` |
| `flirty_question_delete` | `DeleteQuestionCommand` | `FlirtyAck` |
| `flirty_question_type_list` | *(none – reads the host's registry)* | `FlirtyQuestionTypeList` |
| `flirty_placeholder_list` | *(none – reads the host's registry)* | `FlirtyPlaceholderList` |
| `flirty_option_create` | `CreateAnswerOptionCommand` | `AnswerOptionDetail` |
| `flirty_option_update` | `UpdateAnswerOptionCommand` | `AnswerOptionDetail` |
| `flirty_option_delete` | `DeleteAnswerOptionCommand` | `FlirtyAck` |

The area segment is `option`, not `answer_option`: it follows the HTTP route segment `.../options`, even
though the class mirroring `MapAnswerOptionEndpoints` is `FlirtyAnswerOptionTools`. A tool name is typed by
a model, a class name is read by a maintainer.

Two things a client has to be told, because neither is visible in a schema. **An update overwrites every
field** – so an omitted `validationRules` *clears* the stored rules rather than leaving them alone, which is
a data loss with no error anywhere. And **`flirty_question_delete` cascades**: it removes the answer
options, the transitions where the question is source or target, the loop markers whose entry or breaking
question it is, the `AfterQuestion` triggers on it and its canvas position, and it clears the dialog's entry
question if that pointed there. The cascade lives in `DeleteQuestionCommand`; the tool does not
re-implement any of it and only names it in its description.

For answer options the distinction that already cost this repo a bug (#47) is worth repeating: **the label
is displayed, the value is stored.** `AnswerValidator` checks a `SingleChoice`/`MultiChoice` answer against
the option *values*, and a branching expression compares the value.

Two of the tools above read a host **registry** rather than a route: `flirty_question_type_list` (#136) and
`flirty_placeholder_list` (#140), so a client can author a valid `customTypeKey` or a valid `{{key}}` marker
instead of guessing. Both are authoring tools on the `Admin` surface, and both return a projection – the
validator/filler CLR type is server-side only and never reaches the wire. See
[PLACEHOLDERS.md](./PLACEHOLDERS.md).

### Transitions, loops and triggers

| Tool | Command | Returns |
|---|---|---|
| `flirty_transition_create` | `CreateTransitionCommand` | `TransitionDetail` |
| `flirty_transition_update` | `UpdateTransitionCommand` | `TransitionDetail` |
| `flirty_transition_delete` | `DeleteTransitionCommand` | `FlirtyAck` |
| `flirty_loop_create` | `CreateLoopCommand` | `LoopDetail` |
| `flirty_loop_update` | `UpdateLoopCommand` | `LoopDetail` |
| `flirty_loop_delete` | `DeleteLoopCommand` | `FlirtyAck` |
| `flirty_trigger_create` | `CreateTriggerCommand` | `TriggerDetail` |
| `flirty_trigger_update` | `UpdateTriggerCommand` | `TriggerDetail` |
| `flirty_trigger_delete` | `DeleteTriggerCommand` | `FlirtyAck` |

Transitions and triggers are the two areas with **no unique key**, so a repeated create adds a second edge
or a second trigger (and with it a second webhook delivery) instead of reporting a conflict – see the
annotation matrix below, where they are the reason `idempotentHint` is `false` on those creates.

A **loop marker does not create the loop**: the cycle is ordinary transitions, and the runtime has no
special path for loops at all ([LOOPS.md](./LOOPS.md)). Build the cycle with `flirty_transition_create`
first, then mark it. The two question references of a marker are stored unchecked – deliberately not foreign
keys – so only `collectionKey` is enforced, and only for uniqueness within the dialog.

Trigger delivery is **best-effort** by design ([TRIGGERS.md](./TRIGGERS.md)): configuration, expression and
delivery errors are logged and never thrown, because a trigger must not break a start, a submit or an edit.
So a successful create is not evidence that anything will arrive. `InProcess` deliberately delivers nothing
on its own – it raises a Mediator notification the *host application* handles.

### Canvas layout

| Tool | Command | Returns |
|---|---|---|
| `flirty_layout_set` | `SetDialogLayoutCommand` | `FlirtyDialogLayout` |
| `flirty_layout_reset` | `ResetDialogLayoutCommand` | `FlirtyAck` |

### Layout is the one place the publish lock does not apply

`Set`/`ResetDialogLayoutCommand` run deliberately without `DialogEditGuard`: canvas positions live in their
own table and touch no session semantics, so a published dialog must stay arrangeable
([ADR 0007](./adr/0007-layout-as-its-own-table.md)) – and a published dialog is the one opened most often.
Both tool descriptions say so, because otherwise it reads later like a missing guard rather than the edge of
the scope. It is pinned by **one** test with both halves (layout on a published dialog succeeds *and* a
graph change on the same dialog is a 409), because that pair *is* the ADR.

`flirty_layout_set` is a **batch upsert**, and it is the one tool in the package whose parameter is not a
primitive:

```json
{"dialogId": "…", "entries": [{"elementKind": "Question", "elementId": "…", "x": 120, "y": 40}]}
```

An element named in `entries` is placed or moved, an element not named keeps its position, and the result is
the **complete** layout, not only the rows that were set. The batch shape is deliberate: a model that has
just authored a twelve-question graph arranges it in one call, where one-element-per-call would be twelve
transactions each answering with the whole layout. It is admissible against the "primitives, `Guid` and
enums only" rule below because the generated schema is *inline* (no `$defs`), camelCase, and the element
kind is a name-constrained string like every other enum here – so a model sees the entry shape rather than an
opaque blob. A test asserts exactly that, because if the SDK stopped generating it that way the exception
would stop being defensible.

Neither tool guards its input: an empty batch, a duplicate element and a negative coordinate are all
rejected by the command's own validation as a 400. Catching them in the tool would produce the same 400 by a
longer road and duplicate a rule that has one home.

### Sessions – the runtime and test run (#128)

| Tool | Command / query | Returns | HTTP twin |
|---|---|---|---|
| `flirty_session_start` | `StartDialogCommand` | `StartDialogResult` | `POST /flirty/sessions` |
| `flirty_session_start_version` | `StartDialogVersionCommand` | `StartDialogResult` | **none – deliberately** |
| `flirty_session_get` | `ResumeDialogQuery` | `ResumeDialogResult` | `GET /flirty/sessions/{id}` |
| `flirty_session_submit_answer` | `SubmitAnswerCommand` | `SubmitAnswerResult` | `POST …/answers` |
| `flirty_session_edit_answer` | `EditAnswerCommand` | `EditAnswerResult` | `PUT …/answers/{questionId}` |

The order is: start, then answer whatever `flirty_session_get` reports as `currentQuestion` until
`isCompleted`. Editing an earlier answer discards every answer given after it – `invalidatedAnswers` says
how many – because a different answer can lead down a different branch; a completed session reopens if the
new path has a follow-up question. Inside a loop each pass is one answer with its own `iterationIndex`, and
`flirty_session_edit_answer` takes that index to say which pass it means (omit it and the *earliest* answer
is corrected, which is what you want outside a loop).

The results are the **runtime** core records. `QuestionView` is deliberately leaner than the admin
`QuestionDetail` – it carries what a client needs to *render* a question, not what it needs to *edit* one –
and the MCP surface keeps them apart exactly as the engine does.

### A test run is a real run

`flirty_session_start_version` starts one dialog version **regardless of publication status**. That is why
it is the only tool with no HTTP counterpart: over HTTP the publish status stays the production barrier.
It exists because otherwise a draft is untestable and the only way to try one out is to publish it briefly
– which arms it for real users in the meantime. The designer's test runner (#43) uses the same facade
operation for the same reason.

The caveat the designer carries applies here word for word: **a test run writes real sessions and delivers
real webhooks.** The engine's notifications are published as usual, so a trigger of kind `Webhook` really
posts to its configured url. Two consequences the surface makes visible rather than leaves to the reader:

- Sessions started by `flirty_session_start_version` are stored with the external user key **prefixed
  `mcp-test-`**, alongside the designer's own `designer-test-`, so a test run is identifiable afterwards.
  The server applies the prefix, so it does not depend on a client remembering to. `flirty_session_start`
  does **not** prefix – it is the ordinary production path, and prefixing there would hand an MCP client
  and an HTTP client two different sessions for the same user. A *blank* key stays blank, deliberately:
  prefixing it would turn `""` into a non-empty string and silently satisfy the `[Required]` the engine
  owes the caller a 400 for.
- The four writing session tools declare `openWorldHint: true` (see the annotation table below). A host
  that wants none of this registers `FlirtyMcpSurface.Admin`.

### The answer `value` is JSON whose shape depends on the question type

This is the third payload that is JSON inside a string, and the one most likely to be got wrong, because
its schema is `"string"` and getting it wrong is not always an error. The rule of thumb, from the sample
chat UI's own bug (#47): **the label is displayed, the value is stored.**

| `QuestionType` | `value` | |
|---|---|---|
| `FreeText`, `Date` | `"hello"`, `"2026-07-31"` | JSON string; dates ISO-8601 |
| `SingleChoice` | `"dev"` | the option's **`value`**, not its `label` |
| `MultiChoice` | `["a","b"]` | JSON array of strings – note `MultiChoice`, not `MultipleChoice` |
| `Number` | `42`, `3.14` | bare JSON number, dot as the decimal separator |
| `Boolean` | `true` / `false` | bare literal – see below |
| `Json` | `{"city":"Berlin"}`, `"#ff0000"` | any well-formed JSON document – see below |

`Json` (#136) is the one open-shaped type: the engine checks well-formedness and nothing else, so the
shape is the question's own business. If the question carries a `customTypeKey`, the host declared that
type with `o.AddQuestionType(...)` and its own validator adds the semantics – call
`flirty_question_type_list` for the declared keys and a **sample answer** per key rather than guessing.
A key that is not on the list is not an error: the answer is then validated as well-formed JSON only.
The mistake to expect is the unquoted scalar – `#ff0000` is not JSON, `"#ff0000"` is.

`Boolean` is the trap worth naming, and **not for the reason #128's issue text gives**. There is no MCP
input that silently flips a boolean: `AnswerValidator.IsBoolean` accepts the bare literal and the quoted
`"true"`/`"false"`, and rejects everything else with a 400 – the #47 flip happened inside the sample chat
UI's own JS codec, before a value ever reached the engine, so it is a fact about that client and not about
this surface. What *is* silent here is a type change: the quoted form passes validation and is stored, but
`ParseJsonValue` binds a JSON string as a `string` where the bare literal binds as a `bool`, so a branching
condition comparing that answer to a boolean simply stops matching. Nothing is rejected along the way. Send
the bare literal.

`SingleChoice` by contrast fails loudly on a label-instead-of-value – and that 400 is the only error in the
package carrying structured field errors, under `errors.value`. The designer has `AnswerValueCodec` as the
single source of this contract; an MCP client has only these descriptions, which is why the table is
repeated in the parameter description of both tools that take a `value`.

### Databases (#129)

| Tool | Operation | Returns |
|---|---|---|
| `flirty_db_list_targets` | the declared targets, from configuration | `FlirtyTargetList` |
| `flirty_db_test_connection` | `Database.CanConnectAsync` | `FlirtyConnectionTest` |
| `flirty_db_pending_migrations` | `Database.GetPendingMigrationsAsync` | `FlirtyPendingMigrations` |
| `flirty_db_migrate` | pending captured, then `Database.MigrateAsync` | `FlirtyMigrationsApplied` |

The only tools that do not go through `ISender`: the engine has no command for any of this. They mirror the
designer's `ConnectionProfileOperations` instead, and `FlirtyMcpDatabaseOperations` is where the two can be
read side by side.

**`flirty_db_migrate` is gated by absence.** Without `o.AllowMigrations()` it is not registered, so it
never appears in `tools/list`. That is deliberately not the same as a tool that exists and refuses: a model
reasons better about a capability that is simply not there, it costs no round trip to discover, and
invisible is the stronger security posture. Two tests pin it, one with the flag off and one with it on.

**The error handling differs per tool, and the split is the interesting part.**
`flirty_db_test_connection` reports an unreachable database as its **result** (`succeeded: false`) and never
fails – "no" is the answer it was asked for, exactly as in the designer. The other two **cannot** answer
when the database is silent, so they fail with `isError` and a 500 `Database error`. This also closes the
one designer operation whose error handling was never exercised:
`ConnectionProfileOperations.GetPendingMigrationsAsync` has no try/catch and no UI caller.

Two behaviours that look alike and are not: a database that does **not exist yet** is no error at all –
`flirty_db_pending_migrations` answers "everything is pending", which is exactly what a caller wants before
migrating. Only content EF cannot read is a failure. And on SQLite `flirty_db_test_connection` only reports
success once the file exists, so a fresh SQLite target is **migrated first and tested afterwards**, not the
other way round; that sentence is in the tool's own description, where the person hitting it is.

A host that declares targets across providers and builds from source must reference the
`Flirty.Migrations.*` projects itself – `Flirty.Mcp` deliberately does not, because a `ProjectReference`
from a packable to a non-packable project yields neither a dependency nor a bundled DLL. NuGet consumers
get all three inside the `Flirty` package. A missing one is translated into a message naming it, read out
of the exception rather than derived from the provider, so the provider-to-assembly mapping stays in the
single place that owns it (`UseFlirtyProvider`).

## Conventions

Six rules that hold across all 37 tools. Each of them is here rather than in a tool's own documentation
because breaking one has **no local symptom**: the package still compiles, the tool still answers, and only
a client notices – which is the definition of a rule that has to be written down.

### Return shapes: the core records, directly

The tools serialize the **core** `Flirty.Runtime[.Admin]` records. `Flirty.AspNetCore`'s DTO layer is
deliberately not rebuilt: half of it are `…Request` records that exist only because HTTP splits its input
across route and body – a tool call is one flat argument object, so **the tool method parameters *are* the
request shape** – and the other half would be a field-for-field copy of records that are already public
and documented.

One visible consequence: `DialogDetail` keeps its metadata **nested** under `dialog`, where
`DialogDetailResponse` flattens it. That is more informative, not less – it makes visible that
`flirty_dialog_create` returns the same block that sits under `dialog` in `flirty_dialog_get`.

Four small `internal` wrappers cover what the core has no shape for – `Mediator.Unit` (where HTTP answers
`204`) and the non-object returns: `FlirtyAck` (the six deletes **and** `flirty_layout_reset`),
`FlirtyDialogList`, `FlirtyActiveSessionCount` and `FlirtyDialogLayout` (the array result of
`SetDialogLayoutCommand`). They exist because a **non-object** `structuredContent` is protocol-version
dependent (wrapped as `{"result": …}` for clients before SEP-2106, bare afterwards), so every payload this
package emits is an object. Stage 2 added seventeen tools and no wrapper – the count is still four.

### Three conventions that are easy to get wrong

- **Every tool name is a `FlirtyToolNames` const, never derived.** See § Tools above: the SDK's `DeriveName`
  would turn a C# rename into a client-visible breaking change.
- **Every tool sets `UseStructuredContent = true`.** This is *not* the SDK default. Without it the result
  is serialized into the text block only, `structuredContent` stays empty and the tool advertises no
  `outputSchema` – a client would have to parse prose to get at a dialog id. Forgetting it on a new tool has
  no other symptom, which is why a sweep test asserts an `outputSchema` on every tool.
- **Every optional parameter needs an explicit `= null`.** Without a default, *omitting* the argument is an
  argument-binding failure in the SDK's marshaller rather than a `null`.

Also worth knowing: **any type registered in the host container is silently excluded from the input
schema** (the SDK injects it instead). That is how `ISender` reaches a tool without appearing in the
schema – and why a real tool parameter must never be a type a host might register. Keep them to
primitives, `Guid` and enums – with the single, measured exception of `flirty_layout_set`'s batch, documented
above.

### Annotations, and why they are set explicitly

All 37 tools set **all four** annotation hints. That is wider than "annotate the interesting ones", and the
reason is a measurement: the four hints are `bool?` from the attribute all the way to the wire, an *unset*
one is simply **absent**, and the protocol then lets a client assume `destructive: true` and
`openWorld: true`. Omitting is therefore not neutral – unset, every `create` looks to a client like it might
destroy data, and every tool claims it talks to an open world.

| Group | `readOnlyHint` | `destructiveHint` | `idempotentHint` | `openWorldHint` |
|---|---|---|---|---|
| `dialog_get`, `dialog_list`, `dialog_count_active_sessions` | `true` | `false` | `true` | `false` |
| the six `_create` + `dialog_create_version` | `false` | `false` | `false` | `false` |
| the six `_update` | `false` | `false` | `true` | `false` |
| the six `_delete` | `false` | **`true`** | `false` | `false` |
| `dialog_publish`, `dialog_unpublish` | `false` | `false` | `true` | `false` |
| `dialog_abandon_sessions` | `false` | **`true`** | `true` | `false` |
| `flirty_layout_set` | `false` | `false` | `true` | `false` |
| `flirty_layout_reset` | `false` | **`true`** | `true` | `false` |
| `session_start`, `session_start_version` | `false` | `false` | `true` | **`true`** |
| `session_get` | `true` | `false` | `true` | `false` |
| `session_submit_answer` | `false` | `false` | **`false`** | **`true`** |
| `session_edit_answer` | `false` | **`true`** | `true` | **`true`** |
| `db_list_targets`, `db_test_connection`, `db_pending_migrations` | `true` | `false` | `true` | `false` |
| `flirty_db_migrate` | `false` | **`true`** | `true` | `false` |

Five cells are judgement calls worth recording.

`dialog_publish` is **not** destructive – retiring the predecessor version loses no data and is reversible
by publishing it again – but its description names that side effect all the same, because a boolean cannot.
`dialog_abandon_sessions` **is** destructive although it deletes nothing: ending live user sessions is
irreversible, and that is exactly what a client should confirm. The deletes are *not* idempotent because the
repeat is a 404, whereas `flirty_layout_reset` is: it succeeds on an already empty layout. `session_start`
and `session_start_version` **are** idempotent, which surprises: a repeat resumes the caller's running
session (`isResumed: true`) rather than opening a second one, while `session_submit_answer` is not, because
its repeat answers a question that is no longer open and is refused with a 409.

The fourth is `openWorldHint`, and it is a **correction** rather than an addition. Through #127 the value
was `false` on all 27 tools and this guide called that "a fact about this server: it touches only its own
database". It was a fact about the *configuration* tools. Running a dialog publishes engine notifications,
and the core's `WebhookNotificationHandler` turns those into outbound HTTP calls to whatever absolute url a
trigger names – so the four writing session tools reach outside, and declaring otherwise while the
description says "delivers configured webhook triggers" would be a contradiction on the wire.
`session_get` publishes nothing and stays `false`.

The whole matrix is asserted per tool, and `openWorld` had to *become* a column of that theory: it was a
hard-coded `Assert.Equal<bool?>(false, …)`, which would now have pinned the wrong answer for five tools. The
other trap there: `Assert.False` accepts a `bool?` and reads `null` as `false`, so an assertion written that
way would pass on exactly the bug this test exists to catch – the comparisons are `Assert.Equal<bool?>`.

### Server instructions

`AddFlirtyMcp` sets `ServerInstructions` (`FlirtyMcpInstructions.Text`). It explains what a client needs
before it picks a tool at all – the shape of a dialog, the typical build order, that ids are the currency,
the publish lock and its layout exception, the order a dialog is played in – and above all the three
JSON-in-a-string payloads below.

**How they actually travel, measured rather than assumed – and it is not what the setup section suggests.**
They arrive in `InitializeResult.Instructions`, because the SDK's own client **still performs the
`initialize` handshake** and negotiates `2025-06-18` even against this stateless server. Stateless removed
the *session header* requirement (see above), not the handshake. A test pins that `McpClient` receives them.

The consequence is worth stating plainly, because it bounds what instructions can be used for: a client that
speaks `2026-07-28` with per-request `_meta` instead of handshaking gets **no instructions at all**. Such a
client works fine otherwise – `tools/list` and `tools/call` both answer it – and while the SDK *can* carry
instructions in `DiscoverResult.Instructions`, this server does not expose the `discover` method (it answers
`-32601`). So the redundancy rule is load-bearing, not belt-and-braces: **every fact in the instructions is
also in a tool or parameter `[Description]`**, and those travel with `tools/list`, which every client reads.
The three JSON-in-a-string payloads are the reason that rule exists.

A host can append its own guidance after `AddFlirtyMcp`, since the SDK's server options are plain `IOptions`:

```csharp
builder.Services.AddFlirtyMcp();
builder.Services.Configure<McpServerOptions>(o => o.ServerInstructions += "\n\nHost note: …");
```

A *replace* knob is deliberately not offered on `FlirtyMcpOptions`: the content is a fact about Flirty's
contract, not a host preference, and dropping it would silently strand every write tool's description that
assumes the three JSON shapes were stated once.

### The three JSON-in-a-string payloads

`validationRules` on a question, `config` on a trigger and `value` on an answer are `string` on the commands
and stay strings here. Their schema is therefore `"type": "string"`, which tells a model **nothing** – so
the shape is written out in prose, in the parameter `[Description]` and once in the server instructions:

- **`validationRules`**, type-scoped, every field optional
  ([VALIDATION.md](./VALIDATION.md)). `FreeText`: `{"minLength":3,"maxLength":50,"pattern":"^[a-z]+$"}` –
  `pattern` is a .NET regex matched *partially*, so anchor it for a full match. `Number`:
  `{"min":0,"max":10}`. The other four types have no rules. Omitting the argument on an update **clears**
  what was stored.
- **`config`**, `{"url":"https://host.example/hook","name":"order-created"}`
  ([TRIGGERS.md](./TRIGGERS.md)). `url` is required for kind `Webhook` and must be an absolute http/https
  address; `name` is optional and is delivered as the `X-Flirty-Trigger` header. For kind `InProcess` pass
  `{}` – an empty string is rejected by `[Required]`. Only these two fields survive a write: `TriggerConfig`
  writes exclusively what it declares, so **unknown fields do not survive a read/write cycle**. That caveat
  is part of the contract, not a defect.
- **`value`** on `flirty_session_submit_answer` / `_edit_answer`, whose shape follows the question's type –
  `"hello"`, `"dev"`, `["a","b"]`, `42`, `true`. Written out in full under
  [*The answer `value`…*](#the-answer-value-is-json-whose-shape-depends-on-the-question-type) above,
  because it is the only one of the three that is a *table* rather than a shape.

### Enums are names, not numbers – and that differs from HTTP

The SDK's serializer adds a `JsonStringEnumConverter`, so an enum arrives as its **name** and an enum
parameter gets an `enum: [...]` constraint in the input schema for free. That is a genuine advantage over
the HTTP surface, where an enum is an integer (`Flirty.AspNetCore` configures no JSON options at all), and
it is a **deliberate** divergence – do not "unify" it later.

The names are the C# member names **verbatim, i.e. PascalCase**: the converter is added without a naming
policy. Reading is case-insensitive, so a camelCase argument is accepted too, but PascalCase is what the
schema advertises and therefore what a client will send.

## Error mapping (#126)

One `AddCallToolFilter` (`FlirtyMcpExceptionFilter`) maps the engine's exceptions. It is the structural
analogue of `group.AddEndpointFilter<FlirtyExceptionEndpointFilter>()` on the two HTTP route groups: **one
`try`, one registration**, so "mirrors the HTTP filter" is true by construction rather than by one `try`
per tool.

It exists because the SDK swallows exception messages: anything not deriving from `McpException` reaches
the client as a generic `"An error occurred invoking 'x'."`. A call-tool filter is composed **inside** the
SDK's own try/catch and therefore sees the original exception first.

| Exception | `status` | `title` |
|---|---|---|
| `DialogNotFoundException` | 404 | Dialog not found |
| `SessionNotFoundException` | 404 | Session not found |
| `ConfigurationNotFoundException` | 404 | Not found |
| `AnswerValidationException` | 400 | Invalid answer (+ `errors: {"value": [...]}`) |
| `ValidationException` | 400 | Invalid request |
| `InvalidOperationException` | 409 | Conflict |

The order is copied verbatim from the HTTP filter and is load-bearing: `AnswerValidationException`
derives from `ValidationException`, and `DialogPublishedException` from `InvalidOperationException`. It is
enforced by the **compiler** – a wrong order is CS0160, not a warning.

Three branches follow those six, all **MCP-only**, deliberately placed after them so the six read verbatim
like the HTTP filter:

- **A database failure → 500 `Database error`.** Raised by `flirty_db_pending_migrations` and
  `flirty_db_migrate` when the target does not answer or its `Flirty.Migrations.*` assembly is missing. No
  HTTP twin exists because the endpoints expose no such operation. `FlirtyMcpDatabaseException` derives from
  `Exception` and deliberately **not** from `InvalidOperationException` – the latter would make CS0160 force
  it above the 409 branch and split the verbatim six – and 409 would be the wrong answer anyway: nothing
  about the request is in conflict. Unlike the catch-all it is **not** logged, because it does not hide its
  message from the client, and hiding is what the catch-all logs for.
- **An argument-binding failure → 400.** Over HTTP the `{id:guid}` route constraint rejects an unbindable
  value at routing, so the HTTP filter never sees this class of failure. It covers both an unbindable value
  (`"dialogId": "not-a-guid"`, a `JsonException`) and a *missing* required argument (an `ArgumentException`
  whose `ParamName` is `"arguments"`). The `ParamName` check is the discriminator, not decoration: a
  handler's own `ArgumentNullException.ThrowIfNull(command)` carries `ParamName == "command"` and stays a
  500, as it is over HTTP.
- **A catch-all → 500** with a **generic** detail plus a server-side log, reproducing what ASP.NET Core
  does with an unhandled exception. Without it that class of failure would carry no status at all, because
  the SDK's own fallback has none.

`OperationCanceledException` **with a cancelled request token**, `McpException` (including
`McpProtocolException` and its subtypes) and `InputRequiredException` are rethrown – the SDK owns that
control flow. Note the `McpException` clause is wider than the SDK's own rethrow set on purpose: for an
`McpException` the SDK already preserves the message, so the problem this filter solves does not arise
there, and it gives a host's own tools a documented way out of Flirty's mapping. Note also the
*cancelled-token* half: an `OperationCanceledException` **without** a cancelled token is a genuine bug and
falls through to the 500 branch, exactly as the SDK treats it.

### The error payload

```json
{"isError": true,
 "content": [{"type": "text", "text": "Not found: No dialog with the id 'a1b2…' found."}],
 "structuredContent": {"status": 404, "title": "Not found",
                       "detail": "No dialog with the id 'a1b2…' found."}}
```

The member names are those of the HTTP `ProblemDetails` (RFC 9457) on purpose: same names means the parity
test between the two surfaces is three field comparisons instead of a translation table, and a translation
table is exactly where a parity bug hides. The text block carries `"{title}: {detail}"` in all nine
branches – one rule, no per-branch prose – because an LLM consumer reads `content[0].text` while a host
branches on `structuredContent`.

Two deliberate differences from the HTTP payload:

- **`type` is not carried across.** `TypedResults.Problem` fills it with a pointer into HTTP *response*
  semantics; over MCP there is no HTTP response, so copying it would be a falsehood in a payload whose
  whole purpose is honesty. A comparison against the HTTP surface must therefore be **field by field**,
  never whole-object.
- **`status` is advisory.** It has no meaning in the MCP protocol. It exists because it is the most compact
  signal of the error class, and because it is the comparison key against the HTTP surface.

All nine branches use `isError: true` and never `throw new McpException(...)`: `isError` is how the
protocol reports a tool *execution* failure the model can react to, whereas a JSON-RPC error reports a
*protocol* failure. It is also the shape the SDK's own fallback produces, so clients already expect it.

One footnote, documented rather than solved: the SDK's authorization guard inserts a filter that composes
**outside** ours. Flirty's tools carry no `[Authorize]` metadata, so it is inert today; if a later stage
adds one, that particular exception would bypass this mapping.

## Tests (#126–#130)

`tests/Flirty.Tests/Mcp/` – a real `McpClient` over `HttpClientTransport` against an in-process
`TestServer` (Docker-free). `FlirtyMcpTestHost` is a **sibling** of `AspNetCore/FlirtyTestHost`, not an
overload, because it does one thing more: it serves `MapFlirtyEndpoints`, `MapFlirtyAdminEndpoints` **and**
`MapFlirtyMcp` over the **same** SQLite in-memory database, which is what makes the parity comparison
literal rather than two hosts sharing a connection string.

Driven through a real client rather than by calling the filter delegate, on purpose: the riskiest
assumption in the whole design is that the SDK hands a call-tool filter the original exception at all. A
unit test of the mapping table would stay green if that ever changed, while the package silently reverted
to the SDK's generic message. (It really did behave that way once – SDK issue #820, fixed in #844.)

The files, and what each is responsible for:

| File | Proves |
|---|---|
| `FlirtyMcpTestHost` | the host itself: one TestServer, both HTTP surfaces, `/mcp` and `/mcp/{target}`; the host database plus one in-memory database per declared target, each with its own keep-alive connection |
| `FlirtyMcpToolCalls` | the shared `Read<T>`, the graph builders and the layout batch entry – extension methods on the host, the MCP counterparts of the private helpers in `MapFlirtyAdminEndpointsTests` |
| `FlirtyToolSurfaceTests` | the surface contract: the golden name list, the checklist in both directions, assembly-vs-wire registration, the name shape, `outputSchema` + description on every tool, the annotation matrix, the instructions, the layout batch schema |
| `FlirtyGraphToolsTests` | the per-area happy paths, with the **same section banners** as `MapFlirtyAdminEndpointsTests`; the ADR-0007 pair; and a dialog built purely over MCP then replayed over the **HTTP** runtime – deliberately not over MCP, which is the round trip below |
| `FlirtySessionToolsTests` | the runtime tools: a dialog played through, resume-on-restart, the draft/published pair, the `mcp-test-` marker, two loop iterations and the edits that discard downstream answers |
| `FlirtyMcpRoundTripTests` | the whole workflow in one test – the acceptance criterion of EPIC 13 (below) |
| `MapFlirtyMcpTests` | the wiring: input schemas, return shapes, request scoping, surface scoping in both directions, the two `Map`/`Add` prerequisites |
| `FlirtyMcpExceptionParityTests` | HTTP-vs-MCP error parity, six rows, all real on both sides (below) |
| `FlirtyMcpExceptionFilterTests` | the MCP-only filter branches and the catch order |
| `FlirtyDatabaseToolsTests` | the database targets: write isolation between two targets, the unknown-target and no-targets rejections, the raw-text check that no connection string is serialized, the migrate gate in both directions, and the failure-vs-answer split of the three operations |
| `FlirtyMcpTargetRegistrationTests` | the negative half, without an MCP client: that the HTTP surfaces and an out-of-request scope still see the host database, that the replacement works in either registration order and not at all without targets, and the two `MapFlirtyMcp` route guards |
| `FlirtyThrowingTestTools` | the injection seam for the exceptions no real tool can raise |

Two traps in these tests are worth carrying forward, because both produce a **green** test on the very bug
it was written for:

- **A golden list must be literal on one side.** Derive both the expectation and the actual value from
  `FlirtyToolNames` and a renamed const changes both at once, so the comparison stays green. With literals in
  the test, a rename forces a visible three-place edit: attribute, const, list.
- **`Assert.True`/`Assert.False` accept a `bool?` and read `null` as `false`.** Every annotation assertion
  must compare as `bool?`, or an *unset* hint – the failure mode the "set, not defaulted" rule exists for –
  passes.

What no test can see, and it is said rather than assumed: a tool writing its name as a string literal
instead of referencing the const emits an identical wire name. That one is a review concern; the tests close
the *completeness* of the checklist, not whether it is referenced.

The golden list carries more weight than its size suggests, and it is worth naming what: **a renamed tool is
a breaking change for every client.** A tool name is not an implementation detail a refactoring may move –
it is the identifier a model was prompted with and a host has configured. That is the whole reason
`FlirtyToolNames` exists, the reason the SDK's `DeriveName` is never used, and the reason the checklist is
compared in **both** directions rather than as a subset. No second guard is added on top of it: another test
asserting the same thing in prose would restate the claim without deepening it.

### The round trip is the acceptance criterion (#130)

`FlirtyMcpRoundTripTests` is one `[Fact]` that does what EPIC 13 promises, over MCP and nothing else:
author a dialog with questions, options, two branching questions with a conditional edge each, a loop
marker, a trigger and a canvas layout → set the entry question → publish → **counter-check** → derive the
next version → start that *draft* → play both branches with two loop iterations → correct one iteration →
resume → finish. Four things about its shape are deliberate.

**It is one test, not seven.** The per-area suites answer "does this tool work" and are narrow on purpose.
What none of them can show is that the tools *compose* into the workflow, and that claim is one sequence –
split up, each piece would assert a precondition it had just built itself.

**The counter-check sits in the middle, where a client meets it.** After publishing, `flirty_question_create`
is a 409 while `flirty_layout_set` still succeeds: ADR 0005 and ADR 0007 in two calls, on a real graph. The
focused pair in `FlirtyGraphToolsTests` stays, and the overlap is wanted – that one fails pointing at the
rule, this one fails pointing at the workflow step that broke it.

**The test is built around one trap: the clone renames everything.** `CreateDialogVersionCommand` gives
every cloned question a new id, so a client carrying ids from the published version into the draft
addresses elements of a dialog it is not running, and finds out several calls later with a 404 nowhere near
the mistake. Every id for the runtime half therefore comes out of the clone, matched by `Key`, and *that the
ids differ* is asserted rather than remarked on. The round trip is also the only place where the layout
table and the version derivation are visible together: the arrangement survives the clone with its element
references rewritten.

**Every assertion reads a server-produced quantity** – a status, a question key, an `iterationIndex`, an
`invalidatedAnswers` count, an `isCompleted` flag – never a wait. On this surface a discarded call looks
exactly like one that did nothing, so nothing may rest on a call having "probably" taken effect.

The single trigger is `InProcess` on purpose. A `Webhook` trigger really posts (see *A test run is a real
run* above), and a test that needs a listening endpoint to stay green would be a worse test of the same
thing.

### There is deliberately no `tests/Flirty.E2E` coverage

The first question a reviewer asks, so it is answered here rather than left to inference. `Flirty.E2E`
exists for the two **browser** surfaces – the designer and the sample chat UI – where the thing under test
is a rendered page and a gesture. MCP has no browser surface at all: its client is a program, its wire is
JSON-RPC over HTTP, and that wire is already driven end to end here by a real `McpClient` against a real
`TestServer`. A Playwright run would add a Kestrel port, a browser process and a class of flakiness
(`InteractWhenReadyAsync`, `data-canvas-ready`) in exchange for **no** additional coverage of anything.

What would change that: a *host* UI over MCP, or an MCP client whose behaviour depends on a browser. Neither
exists. Note the boundary the other way too – the round trip is not a substitute for the designer's E2E
suite, because a graph authored over MCP and one authored by gesture are different code paths on the way
in, and both are covered where they live.

### The parity claim, stated honestly

The acceptance criterion hides two logically independent halves in one sentence:

- **H1 – same command, same exception.** Transport-independent: both surfaces send the same `ICommand`
  over the same `ISender` to the same handler.
- **H2 – same exception, same status/title/detail.** The only half the new filter can get wrong, and what
  "mirrors" means.

**Since #128 every row proves both halves**, and the history is worth keeping because it is what the
honesty was for. Stage 1 could reach only three of the six exceptions through real tools
(`ConfigurationNotFoundException`, `ValidationException`, `InvalidOperationException`); the other three –
dialog-not-found, session-not-found and answer-validation – all need the runtime operations, so on the MCP
side they went through a **test-only** tool (`FlirtyThrowingTestTools`) and proved **H2 only**, with H1
covered by the core handler tests. The file said which half each row proved, which is the difference between
an honest criterion and a quietly reduced one. Stage 2 added no runtime tool and so could not shrink that
set; the runtime tools close it, and the tier split is gone from the file.

Stage 2 did deepen the real-on-both-sides set from three exception paths to six: a nested 404 (an option
under an unknown question, whose `detail` differs from the unknown-dialog one, and `detail` is the field a
translation table gets wrong), a `ValidationException` from the trigger commands' cross-field
`IValidatableObject` (a different branch from the `[Required]` one), and a `DialogPublishedException` from a
real graph command. The last matters most: the filter's catch order depends on that subtype preceding its
base, the compiler enforces the *order* but not its correctness, and until the graph tools existed the
exception could only be raised through the test seam.

`FlirtyThrowingTestTools` stays regardless. Four of its kinds – an `McpException`, a cancellation, an
`ArgumentNullException` and an unexpected one – are unreachable through any real tool *by design*, and they
are what `FlirtyMcpExceptionFilterTests` drives. The six engine kinds stay beside them because the mapping
table is also asserted as a **table**, one row per exception, which is a different claim from "this call
path maps correctly".

One more trap the tests pin: over MCP, an **omitted** required argument never reaches the pipeline
validation – the marshaller rejects it first. So the "invalid request" parity row uses an **empty** key
(`[Required]` rejects empty strings too), not a missing one. The same fact is why an empty
`externalUserKey` is left unprefixed by `flirty_session_start_version`: prefixing would make it non-empty
and the `[Required]` behind it would never fire.

The answer-validation row is the one place the two surfaces answer **their own** session rather than
sharing one – a submitted answer advances the session, so the first call would leave the second nothing to
reject. Same dialog, same database, same question, which is all the comparison needs.

## Coverage and release

`Flirty.Mcp` is a measured package: `coverage.runsettings` includes `[Flirty.Mcp]*` alongside the other
two. **Without that entry the package is silently unmeasured** and simply missing from the CI job summary,
with no warning anywhere. The release workflow's *Verify packages* step checks a
`Flirty.Mcp.*.nupkg`/`.snupkg` pair; without it an unpacked package would ship unnoticed. See
[CI.md](./CI.md) and [NUGET-PACKAGING.md](./NUGET-PACKAGING.md).
