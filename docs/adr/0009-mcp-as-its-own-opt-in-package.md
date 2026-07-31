# ADR 0009 – MCP server as its own opt-in package

- **Status:** Accepted
- **Context issue:** #126 – MCP host scaffolding – the Flirty.Mcp package and MapFlirtyMcp()
- **Affected:** `src/Flirty.Mcp`, `src/Flirty.AspNetCore`, `src/Flirty.Samples.Web`, `Flirty.sln`,
  `Directory.Packages.props`, `coverage.runsettings`, `.github/workflows/release.yml`

## Context

EPIC 13 (#124) adds a **Model Context Protocol server** so that everything an operator can do in the
Blazor designer can also be driven by an MCP client. Like `Flirty.AspNetCore`, it is a thin adapter
over the existing Mediator commands – no engine logic.

The EPIC's own text said the server would be hosted **inside `Flirty.AspNetCore`**, so that "no new
project is introduced". Measuring the dependency before implementing contradicted that. The MCP SDK
(`ModelContextProtocol.AspNetCore`) drags in `Microsoft.Extensions.AI.Abstractions` and
`Microsoft.Extensions.Caching.Abstractions` with it. Put in `Flirty.AspNetCore`, all three become
**hard dependencies of an already published package**: a consumer who only wants
`MapFlirtyEndpoints` – four HTTP routes over a dialog engine – would restore an AI SDK they never
asked for, and inherit its advisory surface and its version line.

That is precisely the argument of [ADR 0003](./0003-aspnet-free-core.md), one layer further out. There,
the *core* is kept free of ASP.NET because a console consumer should not pay for HTTP. Here, the *web*
package is kept free of MCP because an HTTP consumer should not pay for MCP.

## Decision

The MCP server lives in its own **packable, opt-in package `src/Flirty.Mcp`**:

- `PackageId=Flirty.Mcp`, `FrameworkReference Microsoft.AspNetCore.App` (the transport is Streamable
  HTTP), and a `ProjectReference` to **`Flirty` only**. Not to `Flirty.AspNetCore`: its DTOs, mappings
  and exception filter are all `internal`, so there is nothing there to reuse. `ModelContextProtocol.AspNetCore`
  is its single package dependency beyond the core.
- Wired up like the web package, including the deliberate namespace trick:
  `AddFlirtyMcp(Action<FlirtyMcpOptions>?)` in `Microsoft.Extensions.DependencyInjection` and
  `MapFlirtyMcp(string pattern = "/mcp")` in `Microsoft.AspNetCore.Builder`. `MapFlirtyMcp` returns the
  SDK's `IEndpointConventionBuilder` unchanged so `RequireAuthorization()` chains – the tools include
  write operations, so securing them is recommended as loudly as `MapFlirtyAdminEndpoints` recommends it.
- `AddFlirtyMcp` deliberately does **not** call `AddFlirty()`: the provider and connection string are the
  host's decision, and calling it here would silently pick defaults. It returns the SDK's
  `IMcpServerBuilder`, so a host can add its own tools to the same server.
- The tools serialize the **core** `Flirty.Runtime[.Admin]` records directly; `Flirty.AspNetCore`'s DTO
  layer is not rebuilt. Errors are mapped by **one** `AddCallToolFilter`
  (`FlirtyMcpExceptionFilter`), whose six engine branches are copied verbatim from
  `FlirtyExceptionEndpointFilter`.
- `FlirtyMcpSurface` (`Runtime` / `Admin` / `All`) scopes the registration, so a host that only wants a
  test-run client does not register the configuration tools.

## Discarded alternatives

- **A folder in `Flirty.AspNetCore`** – what the EPIC originally said. Cheapest by far: no solution
  entry, no coverage filter, no release-verification pair, no project count to keep current. It falls on
  the dependency, not on the effort: three packages become mandatory for every existing consumer of a
  published package, and one of them is an AI SDK on an independent version line. Six infrastructure
  edits are a one-time cost; a dependency is forever, and it is paid by people who never asked for the
  feature.
- **The core (`Flirty`)** – ruled out immediately by ADR 0003: the transport needs ASP.NET, and the core
  runs in a console.
- **A conditional reference in `Flirty.AspNetCore`** (`#if`, a second target framework, or
  `PrivateAssets` on the SDK) – the same three arguments ADR 0003 gives for not doing this with ASP.NET:
  the consumer would choose the variant at *build* time, the test matrix doubles, and the public API of
  the package would differ depending on the build. `PrivateAssets` in particular solves nothing, because
  SDK types appear in public signatures (`IMcpServerBuilder`).
- **A repository of its own.** Would isolate the dependency perfectly, and break the one property that
  makes the adapter cheap: it lives in lockstep with the commands it forwards. A command signature change
  would become a cross-repository release dance instead of one PR.
- **Rebuilding `Flirty.AspNetCore`'s DTO layer inside `Flirty.Mcp`.** Half of it are `…Request` records
  that exist only because HTTP splits its input across route and body – a tool call is one flat argument
  object, so the tool parameters *are* the request shape. The other half would be a field-for-field copy
  of records that are already public and documented. Rejected as a second truth with no reader.
- **A `try`/`catch` in each tool method** instead of one call-tool filter. Thirty-two copies of a catch
  chain whose order is load-bearing; the filter makes "mirrors the HTTP filter" true by construction, in
  exactly the way `AddEndpointFilter` on the two route groups does.

## Consequences

**Positive**

- No existing consumer pays for MCP. The invariant is structural, not a convention: `Flirty.AspNetCore`
  has no reference to the SDK, so it cannot drift into one by accident.
- The dependency direction stays a straight line `Flirty ← Flirty.Mcp`, with `Flirty.AspNetCore` beside
  it rather than beneath it. Either web package can be dropped without touching the other.
- The two surfaces can be hosted side by side on one engine and one database – which is what makes the
  error-parity test a literal comparison rather than an argument.

**Negative**

- Six infrastructure places have to be kept current, and each one fails **silently** when forgotten:
  `Flirty.sln`, `Directory.Packages.props`, `coverage.runsettings` (an unlisted package is simply
  unmeasured), `.github/workflows/release.yml`'s verification pairs (an unlisted package ships
  unpacked), `tests/Flirty.Tests/Flirty.Tests.csproj`, and the project count in `CLAUDE.md`.
- A third package to version, pack and publish, on the same date-based version
  (see [NUGET-PACKAGING.md](../NUGET-PACKAGING.md)), and a third README consumer page.
- `Flirty.Mcp`'s public API surfaces SDK types (`IMcpServerBuilder`, `IEndpointConventionBuilder`), so an
  SDK major version is a breaking change for this package. Accepted: the SDK is a hard dependency of this
  package by construction, and hiding it behind a facade would cost the host the ability to add its own
  tools.

**Open**

- Stages 2–4 of EPIC 13 add tools to this host. Stage 4 resolves the database target from the **route**
  (`/mcp/{target}`), which will be its own ADR; nothing in this decision prejudges it.

Details: [MCP.md](../MCP.md), [ARCHITECTURE.md](../ARCHITECTURE.md),
[NUGET-PACKAGING.md](../NUGET-PACKAGING.md).
