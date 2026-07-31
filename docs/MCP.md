# MCP server (`Flirty.Mcp`)

`Flirty.Mcp` exposes the Flirty engine as a **Model Context Protocol** server over Streamable HTTP, so
that an MCP client can do what an operator does in the Blazor designer. Like `Flirty.AspNetCore` it is a
**thin adapter over the existing Mediator commands** – no engine logic, no new command.

> **Build-out status.** This guide describes what exists today: the host and the **27 admin tools** – the
> ten dialog-level ones (EPIC 13 stage 1, #126) plus the whole configuration graph: questions, answer
> options, transitions, loop markers, triggers and the canvas layout (stage 2, #127). The runtime/test-run
> tools and the multi-database targets follow in the later stages of the EPIC, so
> `FlirtyMcpSurface.Runtime` still registers nothing.

Why it is a package of its own rather than a folder in `Flirty.AspNetCore`:
[ADR 0009](./adr/0009-mcp-as-its-own-opt-in-package.md).

## Setup

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
    o.Surface = FlirtyMcpSurface.Admin;   // Runtime | Admin | All (default), or None
    o.ServerName = "Flirty";
});
```

`FlirtyMcpSurface.Runtime` registers nothing today (its tools are a later stage). Note that an MCP server
with **no** tools at all advertises no tools capability, which makes `tools/list` itself unavailable
(JSON-RPC `-32601`). That is the SDK's semantics, not a Flirty decision.

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

## Tools

Names are `flirty_<area>_<action>`. **27 admin tools in six areas**, and the split into tool classes is not
cosmetic: there is **one tool class per existing `MapXxxEndpoints` counterpart**, which is what makes the
parity claim reviewable file against file instead of by counting.

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

All 27 tools set **all four** annotation hints. That is wider than "annotate the interesting ones", and the
reason is a measurement: the four hints are `bool?` from the attribute all the way to the wire, an *unset*
one is simply **absent**, and the protocol then lets a client assume `destructive: true` and
`openWorld: true`. Omitting is therefore not neutral – unset, every `create` looks to a client like it might
destroy data, and every tool claims it talks to an open world. `openWorld = false` throughout is a fact
about this server: it touches only its own database.

| Group | `readOnlyHint` | `destructiveHint` | `idempotentHint` |
|---|---|---|---|
| `dialog_get`, `dialog_list`, `dialog_count_active_sessions` | `true` | `false` | `true` |
| the six `_create` + `dialog_create_version` | `false` | `false` | `false` |
| the six `_update` | `false` | `false` | `true` |
| the six `_delete` | `false` | **`true`** | `false` |
| `dialog_publish`, `dialog_unpublish` | `false` | `false` | `true` |
| `dialog_abandon_sessions` | `false` | **`true`** | `true` |
| `flirty_layout_set` | `false` | `false` | `true` |
| `flirty_layout_reset` | `false` | **`true`** | `true` |

Three cells are judgement calls worth recording. `dialog_publish` is **not** destructive – retiring the
predecessor version loses no data and is reversible by publishing it again – but its description names that
side effect all the same, because a boolean cannot. `dialog_abandon_sessions` **is** destructive although it
deletes nothing: ending live user sessions is irreversible, and that is exactly what a client should confirm.
And the deletes are *not* idempotent because the repeat is a 404, whereas `flirty_layout_reset` is: it
succeeds on an already empty layout.

The whole matrix is asserted per tool. The one trap there: `Assert.False` accepts a `bool?` and reads `null`
as `false`, so an assertion written that way would pass on exactly the bug this test exists to catch – the
comparisons are `Assert.Equal<bool?>`.

### Server instructions

`AddFlirtyMcp` sets `ServerInstructions` (`FlirtyMcpInstructions.Text`). It explains what a client needs
before it picks a tool at all – the shape of a dialog, the typical build order, that ids are the currency,
the publish lock and its layout exception – and above all the two JSON-in-a-string payloads below.

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
The two JSON-in-a-string payloads are the reason that rule exists.

A host can append its own guidance after `AddFlirtyMcp`, since the SDK's server options are plain `IOptions`:

```csharp
builder.Services.AddFlirtyMcp();
builder.Services.Configure<McpServerOptions>(o => o.ServerInstructions += "\n\nHost note: …");
```

A *replace* knob is deliberately not offered on `FlirtyMcpOptions`: the content is a fact about Flirty's
contract, not a host preference, and dropping it would silently strand every write tool's description that
assumes the two JSON shapes were stated once.

### The two JSON-in-a-string payloads

`validationRules` on a question and `config` on a trigger are `string` on the commands and stay strings here.
Their schema is therefore `"type": "string"`, which tells a model **nothing** – so the shape is written out
in prose, in the parameter `[Description]` and once in the server instructions. Both are camelCase:

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

### Enums are names, not numbers – and that differs from HTTP

The SDK's serializer adds a `JsonStringEnumConverter`, so an enum arrives as its **name** and an enum
parameter gets an `enum: [...]` constraint in the input schema for free. That is a genuine advantage over
the HTTP surface, where an enum is an integer (`Flirty.AspNetCore` configures no JSON options at all), and
it is a **deliberate** divergence – do not "unify" it later.

The names are the C# member names **verbatim, i.e. PascalCase**: the converter is added without a naming
policy. Reading is case-insensitive, so a camelCase argument is accepted too, but PascalCase is what the
schema advertises and therefore what a client will send.

## Error mapping

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

Two branches follow those six, both **MCP-only**, deliberately placed after them so the six read verbatim
like the HTTP filter:

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
table is exactly where a parity bug hides. The text block carries `"{title}: {detail}"` in all eight
branches – one rule, no per-branch prose – because an LLM consumer reads `content[0].text` while a host
branches on `structuredContent`.

Two deliberate differences from the HTTP payload:

- **`type` is not carried across.** `TypedResults.Problem` fills it with a pointer into HTTP *response*
  semantics; over MCP there is no HTTP response, so copying it would be a falsehood in a payload whose
  whole purpose is honesty. A comparison against the HTTP surface must therefore be **field by field**,
  never whole-object.
- **`status` is advisory.** It has no meaning in the MCP protocol. It exists because it is the most compact
  signal of the error class, and because it is the comparison key against the HTTP surface.

All eight branches use `isError: true` and never `throw new McpException(...)`: `isError` is how the
protocol reports a tool *execution* failure the model can react to, whereas a JSON-RPC error reports a
*protocol* failure. It is also the shape the SDK's own fallback produces, so clients already expect it.

One footnote, documented rather than solved: the SDK's authorization guard inserts a filter that composes
**outside** ours. Flirty's tools carry no `[Authorize]` metadata, so it is inert today; if a later stage
adds one, that particular exception would bypass this mapping.

## Tests

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
| `FlirtyMcpTestHost` | the host itself: one TestServer, both HTTP surfaces and `/mcp` over one database |
| `FlirtyMcpToolCalls` | the shared `Read<T>` and the graph builders – extension methods on the host, the MCP counterparts of the private helpers in `MapFlirtyAdminEndpointsTests` |
| `FlirtyToolSurfaceTests` | the surface contract: the golden name list, the checklist in both directions, assembly-vs-wire registration, the name shape, `outputSchema` + description on every tool, the annotation matrix, the instructions, the layout batch schema |
| `FlirtyGraphToolsTests` | the per-area happy paths, with the **same section banners** as `MapFlirtyAdminEndpointsTests`; the ADR-0007 pair; and a dialog built purely over MCP then played through the runtime |
| `MapFlirtyMcpTests` | the wiring: input schemas, return shapes, request scoping, the two `Map`/`Add` prerequisites |
| `FlirtyMcpExceptionParityTests` | HTTP-vs-MCP error parity, six rows, in two honesty tiers (below) |
| `FlirtyMcpExceptionFilterTests` | the MCP-only filter branches and the catch order |
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

### The parity claim, stated honestly

The acceptance criterion hides two logically independent halves in one sentence:

- **H1 – same command, same exception.** Transport-independent: both surfaces send the same `ICommand`
  over the same `ISender` to the same handler.
- **H2 – same exception, same status/title/detail.** The only half the new filter can get wrong, and what
  "mirrors" means.

Only three of the six exceptions are reachable through the 27 admin tools of stages 1 and 2
(`ConfigurationNotFoundException`, `ValidationException`, `InvalidOperationException`); the other three
need the runtime operations, which are a later stage. Those three therefore go through a **test-only**
tool (`FlirtyThrowingTestTools`) on the MCP side and prove **H2 only** – H1 for them is covered by the
existing core handler tests. The HTTP side is the real endpoint in all six. Saying which half a row proves
is the difference between an honest criterion and a quietly reduced one.

Stage 2 did **not** shrink that tier – it added no runtime tool – but it deepened Tier 1 from three exception
paths to six: a nested 404 (an option under an unknown question, whose `detail` differs from the
unknown-dialog one, and `detail` is the field a translation table gets wrong), a `ValidationException` from
the trigger commands' cross-field `IValidatableObject` (a different branch from the `[Required]` one), and a
`DialogPublishedException` from a real graph command. The last matters most: the filter's catch order depends
on that subtype preceding its base, the compiler enforces the *order* but not its correctness, and until the
graph tools existed the exception could only be raised through the test seam.

One more trap the tests pin: over MCP, an **omitted** required argument never reaches the pipeline
validation – the marshaller rejects it first. So the "invalid request" parity row uses an **empty** key
(`[Required]` rejects empty strings too), not a missing one.

## Coverage and release

`Flirty.Mcp` is a measured package: `coverage.runsettings` includes `[Flirty.Mcp]*` alongside the other
two. **Without that entry the package is silently unmeasured** and simply missing from the CI job summary,
with no warning anywhere. The release workflow's *Verify packages* step checks a
`Flirty.Mcp.*.nupkg`/`.snupkg` pair; without it an unpacked package would ship unnoticed. See
[CI.md](./CI.md) and [NUGET-PACKAGING.md](./NUGET-PACKAGING.md).
