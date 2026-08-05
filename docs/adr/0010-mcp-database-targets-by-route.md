# ADR 0010 – MCP database targets are declared by the host and selected by the route

- **Status:** Accepted
- **Context issue:** #129 – MCP database targets – list, test, pending migrations, migrate
- **Affected:** `src/Flirty.Mcp`

## Context

The designer has been multi-database since #37: connection profiles in a server-local, gitignored JSON
file, an active profile per Blazor circuit, and an `IDbContextFactory<FlirtyDbContext>` that builds a
context against it. EPIC 13 (#124) wants the same reach for an MCP client — list the databases, test one,
see and apply its pending migrations, and edit dialogs in it.

**The EPIC's own premise for how to do that is no longer buildable.** It said the active database "must be
held **per MCP session** (a client selects/tests/migrates a target, then edits against it)". Protocol
revision `2026-07-28` removed the `initialize` handshake (SEP-2575) and the `Mcp-Session-Id` header
(SEP-2567) from the wire format; the C# SDK now defaults `HttpServerTransportOptions.Stateless` to `true`,
and a stateful server **refuses** current clients with `-32022 UnsupportedProtocolVersion`. There is no
session to hold a selection in. Even if there were, a `select_target` followed by an `edit` would land on
two different instances behind a load balancer and the edit would go to the wrong database — a data-loss
shape, not an inconvenience.

There is a second constraint that is easy to miss and expensive to get wrong. Whatever selects the
database must not be visible to the rest of the host: `MapFlirtyEndpoints`, `MapFlirtyAdminEndpoints` and
`FlirtyMigrationHostedService` share the same `FlirtyDbContext` registration, and repointing that would
silently move every one of them onto the MCP target.

## Decision

**The host declares the targets by name; the client picks one by connecting to the route that carries the
name.** Nothing else selects a database.

- `FlirtyMcpOptions.AddTarget(name, provider, connectionString, description?)` declares one,
  `UseDefaultTarget(name)` says which one a route without a `{target}` segment serves, and
  `AllowMigrations()` opts into the one tool that writes schema. `MapFlirtyMcp("/mcp/{target}")` maps the
  parameterised route beside the plain one; both are ordinary endpoints, so each carries its own
  `RequireAuthorization()`.
- **There is no `select_target` tool and no `target` tool argument.** Every call is therefore idempotent on
  its own, which is what stateless MCP requires anyway, and the parameter stays off all 36 tool schemas —
  not one tool of stages 2 and 3 changed when this landed.
- **The target is captured in the transport's `ConfigureSessionOptions` callback**, which in stateless mode
  the SDK invokes on *every HTTP request*, resolving the server's services from
  `HttpContext.RequestServices` with `ScopeRequests = false`. The scoped holder captured there is therefore
  the very instance the tool resolves. Because that callback fires **only** on an MCP request, everything
  else in the host structurally never sees a target.
- **Only the `FlirtyDbContext` registration is replaced**, and only when at least one target is declared.
  The `DbContextOptions<FlirtyDbContext>` that `AddFlirty` registered stay untouched and are the fallback.
  Declaring no target registers no replacement at all.
- **An undeclared target name is a validation error** (400, with the declared names in the message), raised
  by a second call-tool filter composed inside the error filter — on a single-database server too. A
  client must never believe it switched database when it did not.
- **No connection string crosses the wire.** `FlirtyMcpTarget`, the type that holds one, appears in no tool
  signature; the projection `FlirtyMcpTargetInfo` carries name, provider, description and `isDefault`. Note
  what this does *not* rest on: `internal` is no serialization barrier — `System.Text.Json` ignores a
  type's accessibility, and every result wrapper in this package is internal and reaches the client in
  full. The guarantee is the absent signature plus a test asserting on the **raw serialized text**.
- **Four tools**, one of them conditional: `flirty_db_list_targets`, `flirty_db_test_connection`,
  `flirty_db_pending_migrations`, and `flirty_db_migrate` only under `AllowMigrations()`. Gated by
  **absence**: an unregistered tool does not appear in `tools/list` at all.

## Discarded alternatives

- **A target held per MCP session** — the EPIC's own text. Not a preference but a protocol fact: revision
  `2026-07-28` has no session to hold it in, and a stateful server refuses those clients outright. The
  load-balancer failure mode would remain even if it were buildable.
- **A `target` argument on every tool.** Session-free and honest, and rejected on two counts: it adds a
  parameter to all 36 schemas — a rewrite of stages 2 and 3 for a value that is constant per client — and it
  reintroduces exactly the confusion the route avoids, because a client connected to `a` could then write
  into `b`. On the four database tools alone it would have been cheap, but a rule that holds for 32 tools
  and not for 4 is a rule nobody can state.
- **`IHttpContextAccessor` plus endpoint metadata.** Works, and was the first design. It needs a marker on
  the endpoint, because `/mcp` and `/flirty/dialogs` are indistinguishable by route values and the default
  target would otherwise leak into the HTTP endpoints. It also depends on
  `HttpServerTransportOptions.PerSessionExecutionContext` staying `false`: the SDK documents that setting
  it "prevents you from using IHttpContextAccessor in handlers", so one unrelated host flag would kill the
  seam silently. `ConfigureSessionOptions` needs no marker, no accessor registration, and cannot be
  switched off from outside. Its price is that it works only on the stateless transport — which is why
  `MapFlirtyMcp` refuses declared targets on a stateful one rather than falling back quietly.
- **Replacing `DbContextOptions<FlirtyDbContext>` instead of `FlirtyDbContext`.** One line shorter and
  wrong in the way that produces no failing test: every consumer of the options — the HTTP endpoints, the
  migration hosted service, a host's own background job — would follow the MCP target along, still working,
  just against a different database.
- **Registering the factory as `IDbContextFactory<FlirtyDbContext>`**, as the issue text suggested and as
  the designer does. Nothing in this package consumes the interface, and `Flirty.Designer` registers its
  own implementation of it — claiming the slot would repoint the designer in a process that hosts both.
- **A designer-style JSON profile store, read at runtime.** Tempting because it already exists, and it
  fails on authority: a file the server rereads means a client could be moved between databases between two
  calls without either end knowing. Host configuration is the honest place for a decision an operator makes
  once.
- **Returning a result record instead of an error for a database failure**, mirroring the designer's
  `MigrationResult` exactly. Kept for `flirty_db_test_connection`, where "not reachable" is the *answer*.
  Rejected for the other two: they cannot answer at all, and `isError` is the channel a model will not read
  past. The cost is one MCP-only branch in the exception filter, placed after the six that mirror HTTP so
  those still read verbatim.
- **A tool that exists and refuses when migrations are disabled.** Costs a model a round trip to learn
  nothing, advertises a capability the server will not honour, and tells an unauthenticated reader that
  migrations are a thing here. Absence is both kinder and quieter.

## Consequences

**Positive**

- Every call is self-contained, so the server load-balances without affinity — the property that made the
  session-based design unbuildable is the one this design gets for free.
- The single-database path is untouched, not merely compatible: with no target declared, no service is
  replaced and the registration graph is what it was before this stage.
- Adding a target changes no tool signature and no schema, so the multi-database work cost stages 2 and 3
  nothing and will cost future tool stages nothing either.

**Negative**

- `FlirtyMcpSurface.All` changes its numeric value from `3` to `7` with the new `Database` flag.
  Source-compatible, visible in a compiled constant; acceptable on a pre-1.0, date-versioned package.
- A host using `AddDbContextPool<FlirtyDbContext>` loses pooling for that context once it declares MCP
  targets, because the pooled lease descriptor is the one being replaced.
- The database target is a fact about the *connection* and so cannot live in any tool or parameter
  description. It is carried by the description of `flirty_db_list_targets`, which keeps the redundancy
  rule of the server instructions satisfied — but by a less obvious tool than usual.
- Targets are configuration, so adding one needs a restart. Deliberate, and the flip side of the authority
  argument above.

**Open**

- **No per-target authorization.** Whoever may call `/mcp` may name every declared target; the routes are
  separate endpoints, so a host *can* attach different policies per route, but the package offers no
  per-target model of its own.
- Declared targets are **not** migrated at startup — `FlirtyMigrationHostedService` resolves one context
  and sees no target. That is what `flirty_db_migrate` is for, and why a fresh target has to be migrated
  before it can be used.

Details: [MCP.md](../MCP.md), [ADR 0009](./0009-mcp-as-its-own-opt-in-package.md),
[DESIGNER.md](../DESIGNER.md).
