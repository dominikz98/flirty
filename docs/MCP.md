# MCP server (`Flirty.Mcp`)

`Flirty.Mcp` exposes the Flirty engine as a **Model Context Protocol** server over Streamable HTTP, so
that an MCP client can do what an operator does in the Blazor designer. Like `Flirty.AspNetCore` it is a
**thin adapter over the existing Mediator commands** – no engine logic, no new command.

> **Build-out status.** This guide describes what exists today: the host and the ten dialog-level tools
> (EPIC 13 stage 1, #126). The remaining graph tools (questions, options, transitions, loops, triggers,
> layout), the runtime/test-run tools and the multi-database targets follow in the later stages of the EPIC.

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

Names are `flirty_<area>_<action>`. Ten dialog-level tools today:

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
`204`) and the non-object returns: `FlirtyAck`, `FlirtyDialogList`, `FlirtyActiveSessionCount`,
`FlirtyDialogLayout`. They exist because a **non-object** `structuredContent` is protocol-version
dependent (wrapped as `{"result": …}` for clients before SEP-2106, bare afterwards), so every payload this
package emits is an object.

### Two conventions that are easy to get wrong

- **Every tool sets `UseStructuredContent = true`.** This is *not* the SDK default. Without it the result
  is serialized into the text block only, `structuredContent` stays empty and the tool advertises no
  `outputSchema` – a client would have to parse prose to get at a dialog id.
- **Every optional parameter needs an explicit `= null`.** Without a default, *omitting* the argument is an
  argument-binding failure in the SDK's marshaller rather than a `null`.

Also worth knowing: **any type registered in the host container is silently excluded from the input
schema** (the SDK injects it instead). That is how `ISender` reaches a tool without appearing in the
schema – and why a real tool parameter must never be a type a host might register. Keep them to
primitives, `Guid` and enums.

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

### The parity claim, stated honestly

The acceptance criterion hides two logically independent halves in one sentence:

- **H1 – same command, same exception.** Transport-independent: both surfaces send the same `ICommand`
  over the same `ISender` to the same handler.
- **H2 – same exception, same status/title/detail.** The only half the new filter can get wrong, and what
  "mirrors" means.

Only three of the six exceptions are reachable through the ten dialog tools of this stage
(`ConfigurationNotFoundException`, `ValidationException`, `InvalidOperationException`); the other three
need the runtime operations, which are a later stage. Those three therefore go through a **test-only**
tool (`FlirtyThrowingTestTools`) on the MCP side and prove **H2 only** – H1 for them is covered by the
existing core handler tests. The HTTP side is the real endpoint in all six. Saying which half a row proves
is the difference between an honest criterion and a quietly reduced one.

One more trap the tests pin: over MCP, an **omitted** required argument never reaches the pipeline
validation – the marshaller rejects it first. So the "invalid request" parity row uses an **empty** key
(`[Required]` rejects empty strings too), not a missing one.

## Coverage and release

`Flirty.Mcp` is a measured package: `coverage.runsettings` includes `[Flirty.Mcp]*` alongside the other
two. **Without that entry the package is silently unmeasured** and simply missing from the CI job summary,
with no warning anywhere. The release workflow's *Verify packages* step checks a
`Flirty.Mcp.*.nupkg`/`.snupkg` pair; without it an unpacked package would ship unnoticed. See
[CI.md](./CI.md) and [NUGET-PACKAGING.md](./NUGET-PACKAGING.md).
