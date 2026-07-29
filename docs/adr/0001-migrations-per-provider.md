# ADR 0001 – Migrations per provider (separate assemblies)

- **Status:** Accepted
- **Context issue:** #19 – Providers SQLite / PostgreSQL / SQL Server + migrations
- **Affected:** `src/Flirty`, `src/Flirty.Migrations.*`, `tests/Flirty.Tests`

## Context

Flirty supports three EF Core providers (SQLite, PostgreSQL, SQL Server) and ships them in a single
`Flirty` NuGet package; the consumer picks one at runtime. The `FlirtyDbContext` is
provider-agnostic. Migrations are needed to create the database – and migrations in EF Core are
**provider-specific** (the generated DDL differs per provider, e.g. `datetimeoffset` vs.
`timestamp with time zone` vs. `TEXT`).

EF Core assigns migrations to the `DbContext` **independently of the provider** and, when applying,
scans the entire migrations assembly for `[Migration]` types. Multiple provider migration sets must
therefore be cleanly separated, otherwise they collide.

## Decision

Each provider migration lives in its **own assembly**:

- `Flirty.Migrations.Sqlite`
- `Flirty.Migrations.PostgreSql`
- `Flirty.Migrations.SqlServer`

Each project references `Flirty` (context + provider transitively) and
`Microsoft.EntityFrameworkCore.Design`, contains an `internal` `IDesignTimeDbContextFactory` for
`dotnet ef` and picks its own `MigrationsAssembly`. At runtime the call selects the matching set via
`Use…(cs, b => b.MigrationsAssembly("Flirty.Migrations.<Provider>"))`.

## Discarded alternatives

- **One assembly, three folders/namespaces.** EF does not filter migrations by provider or
  namespace; all three `InitialCreate` migrations would be found → duplicate IDs and application of
  provider-foreign SQL. Technically unworkable.
- **One assembly, switch provider via a build flag.** Allows only *one* provider at build time and
  contradicts the "one package, all three providers, choice at runtime" model.

## Consequences

- **Positive:** Cleanly separated migrations, selectable at runtime; matches the official
  EF Core recommendation for multi-provider. Every provider is verified for real via Testcontainers.
- **Negative:** On model changes, **three** migration sets must be generated/maintained.
- **Open:** The migration assemblies are currently `IsPackable=false`. Bundling them into the
  `Flirty` NuGet package and auto-applying them at startup follow in **#20**; the convenient
  provider options API in **#34**.

## Addendum (#51)

The points named under "Open" are done:

- **#20** bundles the three migration DLLs into the `Flirty` package under `lib/<tfm>/` via
  `TargetsForTfmSpecificBuildOutput`; `o.ApplyMigrations()` applies them at startup through the
  `FlirtyMigrationHostedService`. `IsPackable=false` remains **correct** here: the DLLs travel along
  in the core package instead of being three packages of their own – the consumer still installs only
  `Flirty`.
- **#34/#37** replace the hand-written `MigrationsAssembly(...)` with
  `FlirtyDatabaseProvider` + `DbContextOptionsBuilder.UseFlirtyProvider(...)`; the
  provider→MigrationsAssembly mapping thus lives at exactly one place in the core and is reused by the
  designer (multi-DB profiles).

None of this changes the decision itself – it is the precondition for it.

Details: [PERSISTENCE.md](../PERSISTENCE.md).
