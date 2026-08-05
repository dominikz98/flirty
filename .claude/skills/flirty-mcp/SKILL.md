---
name: flirty-mcp
description: Add or change an MCP tool in Flirty.Mcp – the Model Context Protocol server over the engine (AddFlirtyMcp / MapFlirtyMcp). Use for "MCP tool", "MCP server", "MapFlirtyMcp", "AddFlirtyMcp", "tool annotation", "tools/list", "flirty_ tool name", "database target", "Streamable HTTP", "EPIC 13", issues #126–#130.
---

# Add or change an MCP tool

`src/Flirty.Mcp` serves the engine as a **Model Context Protocol** server over Streamable HTTP. It is the
same kind of thin adapter as `Flirty.AspNetCore`: a tool sends an existing Mediator command via `ISender`
and serializes the **core** result record. No engine logic lives here, and adding a tool needs no core
change. Reference: `docs/MCP.md`, ADR `docs/adr/0009-mcp-as-its-own-opt-in-package.md` (own package) and
`docs/adr/0010-mcp-database-targets-by-route.md` (database targets).

**The organising rule:** one tool class per existing `MapXxxEndpoints` counterpart. That is what makes the
parity claim reviewable file against file instead of by counting, so a new tool goes into the class whose
HTTP twin owns the same commands – a new class only when a new endpoint group exists.

## Prior art (read before writing)

- `src/Flirty.Mcp/Tools/FlirtyDialogTools.cs` – the **documentation home** of the tool-shape conventions of
  all ten classes; the nine others state only what is specific to their area. Read this first.
- `src/Flirty.Mcp/Tools/FlirtyToolNames.cs` – every wire name as a `const`, the single parity checklist.
- `src/Flirty.Mcp/Tools/FlirtyLayoutTools.cs` – the one tool whose parameter is not a scalar, and why that
  is admissible.
- `src/Flirty.Mcp/Tools/FlirtySessionTools.cs` – the runtime tools: `OpenWorld = true`, the `mcp-test-`
  marker, and the JSON `value` contract as a parameter description.
- `src/Flirty.Mcp/FlirtyMcpServiceCollectionExtensions.cs` – the `WithTools<T>()` chain and the surface
  flags; `FlirtyMcpOptions.cs` for `FlirtyMcpSurface`.
- `src/Flirty.Mcp/FlirtyMcpExceptionFilter.cs` – the single error mapping; `FlirtyToolResults.cs` for the
  four wrappers.
- `tests/Flirty.Tests/Mcp/FlirtyToolSurfaceTests.cs` – the golden list and the annotation matrix; the tests
  that fail when a step below is skipped.
- `tests/Flirty.Tests/Mcp/FlirtyMcpToolCalls.cs` – the shared builders a new test should reuse.

## Steps

1. **The wire name** as a `const string` in `FlirtyToolNames.cs`, shape `flirty_<area>_<action>`. The area
   segment follows the **HTTP route segment**, not the class name (`option`, not `answer_option`).

2. **The tool method** in the matching class – `internal static`, returning the core record:
   ```csharp
   [McpServerTool(
       Name = FlirtyToolNames.ThingUpdate,
       UseStructuredContent = true,
       ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
   [Description("What it does, plus every side effect a boolean cannot carry.")]
   internal static async Task<ThingDetail> UpdateThingAsync(
       ISender sender,
       [Description("The id of the dialog the thing belongs to.")] Guid dialogId,
       [Description("…")] string? expression = null,
       CancellationToken cancellationToken = default)
       => await sender.Send(new UpdateThingCommand(dialogId, expression), cancellationToken);
   ```
   - `UseStructuredContent = true` is **not** the SDK default. Without it `structuredContent` stays empty,
     no `outputSchema` is advertised, and a client has to parse prose for an id.
   - Set **all four** annotation hints. An omitted hint is *absent from the wire*, and the protocol then
     lets a client assume `destructive` and `openWorld`.
   - Every optional parameter needs an explicit `= null`; without it, *omitting* the argument is a binder
     failure rather than a `null`.
   - Parameters are primitives, `Guid` and enums. Enums travel as **PascalCase names** and get an
     `enum: [...]` schema constraint for free – a deliberate divergence from HTTP, where they are integers.
   - A JSON-in-a-string parameter (`validationRules`, `config`, `value`) has schema `"string"`, which tells
     a model nothing – write the shape out in the `[Description]`.

3. **Return an object.** If the command returns `Unit`, a scalar or an array, add a wrapper to
   `FlirtyToolResults.cs` (`FlirtyAck` covers the deletes): a **non-object** `structuredContent` is
   protocol-version dependent.

4. **Registration** – only when the tool needs a **new class**: add `WithTools<T>()` in
   `FlirtyMcpServiceCollectionExtensions.cs`, under the right `FlirtyMcpSurface` branch. A class missing
   from the chain compiles and ships invisibly; the registration test is what catches it.

5. **Tests** in `tests/Flirty.Tests/Mcp/`: the happy path in the class matching the area, the name as a
   **literal** in `FlirtyToolSurfaceTests`' golden list (see *Do not*), and a row in the annotation matrix.
   Failure cases go through the real engine, not through `FlirtyThrowingTestTools` – that seam exists only
   for the four exceptions no real tool can raise.

6. **`docs/MCP.md`** – the tool table of its area, and § Conventions if the tool needs an exception to one.

## Do not

- **Do not reference `Flirty.AspNetCore`.** The two web packages sit *beside* each other; `Flirty.Mcp`
  references `Flirty` only, so either can be dropped without touching the other (ADR 0009).
- **Do not add a DTO layer.** The tool parameters *are* the request shape (a call is one flat argument
  object), and the results are the core records. `DialogDetail` therefore stays nested under `dialog` where
  the HTTP `DialogDetailResponse` flattens it.
- **Do not put a connection string in a tool argument or result.** `FlirtyMcpTarget` appears in no tool
  signature and no result projection. Note what does *not* protect this: `internal` is no barrier –
  `System.Text.Json` serializes internal types happily, as every wrapper in `FlirtyToolResults.cs` shows.
- **Do not add a `select_target` tool.** Revision `2026-07-28` left no session to hold a selection in, and
  behind a load balancer a `select` + `edit` pair would edit the wrong database. The target comes from the
  route (ADR 0010).
- **Do not register a gated tool that merely refuses.** Leave it out of `WithTools` (see
  `flirty_db_migrate` under `AllowMigrations()`): a model reasons better about a capability that is not
  there, it costs no round trip to discover, and invisible is the stronger security posture.
- **Do not let the SDK derive a name.** `DeriveName` strips `Async` and snake_cases the method, so a C#
  rename becomes a client-visible breaking change.
- **Do not derive both sides of the golden list from `FlirtyToolNames`.** A renamed const would change
  expectation and actual at once and the test stays green through the very rename it exists to surface.
- **Do not call `AddFlirty()` from `AddFlirtyMcp`.** The provider and connection string are the host's
  decision.
- **Do not wrap a tool body in `try`/`catch`.** The one `AddCallToolFilter` maps every engine exception;
  a per-tool `try` is the duplication that filter exists to avoid.
- **Do not add validation the command already does.** It produces the same 400 by a longer road and gives
  the rule a second home.

## Definition of Done

English XML docs on new public API (CS1591 is an error – `Flirty.Mcp` is packable) · the golden list and
the annotation matrix extended · tests green · `docs/MCP.md` updated · `CLAUDE.md` § *Central entry points*
and § *Solution layout* corrected if the tool or class **count** changed.

> A **new packable project** (not a new tool) touches six places, four of which fail silently – see the
> skill `flirty-nuget-package` and `CLAUDE.md` § *Hard build conventions*.

## Verification

```pwsh
dotnet build Flirty.sln
dotnet test tests/Flirty.Tests --filter "FullyQualifiedName~Flirty.Tests.Mcp"
```

Against the running sample (`src/Flirty.Samples.Web`, MCP at `/mcp`) the wire itself can be read – which is
the only thing that settles a question about what a client actually receives:

```pwsh
dotnet run --project src/Flirty.Samples.Web          # http://localhost:5080, MCP at /mcp
curl -s -X POST http://localhost:5080/mcp -H 'Content-Type: application/json' `
  -H 'Accept: application/json, text/event-stream' `
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'
```
