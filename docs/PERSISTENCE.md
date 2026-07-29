# Persistence: Providers & Migrations

How Flirty binds EF Core to **SQLite**, **PostgreSQL** and **SQL Server** and manages the migrations per
provider. Implemented in issue **#19**. Reference: [ARCHITECTURE.md](./ARCHITECTURE.md) §8,
model details in [DOMAIN-MODEL.md](./DOMAIN-MODEL.md).

## Overview

The core (`src/Flirty`) stays **provider-agnostic**: `FlirtyDbContext` has only the
options constructor and does not fix a provider. All three EF Core providers ship with the
`Flirty` NuGet package; the consumer picks one of them at runtime.

| Provider | NuGet package | Migrations assembly |
|---|---|---|
| SQLite | `Microsoft.EntityFrameworkCore.Sqlite` | `Flirty.Migrations.Sqlite` |
| PostgreSQL | `Npgsql.EntityFrameworkCore.PostgreSQL` | `Flirty.Migrations.PostgreSql` |
| SQL Server | `Microsoft.EntityFrameworkCore.SqlServer` | `Flirty.Migrations.SqlServer` |

The provider `Use…` methods (`UseSqlite`, `UseNpgsql`, `UseSqlServer`) come from the
EF Core provider packages themselves. The convenient options API `AddFlirty(o => o.UseSqlite(…))`
(provider selection incl. `FlirtyDbContext` registration) has been available since **#34**; the auto-migration
on top of it (`o.ApplyMigrations()` → `FlirtyMigrationHostedService`) came in **#20**. See
[Provider selection via AddFlirty](#provider-selection-via-addflirty-34).

## Why separate migrations assemblies?

EF Core associates migrations with the `FlirtyDbContext` **provider-independently** and scans the
entire migrations assembly when applying them. If the `InitialCreate` migrations of all three providers
lived in the same assembly, there would be duplicate migration IDs, and `Database.Migrate()` would try to
apply provider-foreign SQL (e.g. SQLite DDL against PostgreSQL).

That is why **each provider migration lives in its own assembly** (`src/Flirty.Migrations.<Provider>`).
At runtime the call picks the matching migrations assembly:

```csharp
new DbContextOptionsBuilder<FlirtyDbContext>()
    .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly("Flirty.Migrations.PostgreSql"))
    .Options;
```

These projects are `IsPackable=false` (in-repo only, for `dotnet ef` and tests). Their DLLs are
nonetheless **bundled** into the `Flirty` NuGet package when packing (see [Auto-migration](#auto-migration-at-startup-20)),
so that pure package consumers can migrate too.

## Project layout

```
src/
├─ Flirty                     Core: FlirtyDbContext + configurations, references all 3 providers
├─ Flirty.Migrations.Sqlite       Migrations + SqliteDesignTimeDbContextFactory
├─ Flirty.Migrations.PostgreSql   Migrations + PostgreSqlDesignTimeDbContextFactory
└─ Flirty.Migrations.SqlServer    Migrations + SqlServerDesignTimeDbContextFactory
```

Each set contains the same state, migration for migration with identical names: `InitialCreate` (#19) and
`AddDialogLayout` (#102, table `DialogLayout` for the designer's canvas positions – see
[ADR 0007](./adr/0007-layout-as-its-own-table.md)).

Each migrations project references `Flirty` (which brings the context + providers transitively) and
`Microsoft.EntityFrameworkCore.Design` (`PrivateAssets=all`). An `internal sealed`
`IDesignTimeDbContextFactory<FlirtyDbContext>` configures the respective provider including
`MigrationsAssembly`, so that `dotnet ef` can build the context without a running app (the
connection string in it is a placeholder – `migrations add`/`script` do not connect).

## Creating migrations

`dotnet ef` is pinned as a local tool (`.config/dotnet-tools.json`); a one-time
`dotnet tool restore` is enough. A new/updated migration is created **for each provider individually**
(same name, so the sets stay in sync):

```bash
dotnet ef migrations add InitialCreate \
  --project src/Flirty.Migrations.Sqlite \
  --startup-project src/Flirty.Migrations.Sqlite \
  --context FlirtyDbContext --output-dir Migrations
# likewise for Flirty.Migrations.PostgreSql and Flirty.Migrations.SqlServer
```

After every model change **all three** sets must be regenerated. The SQL per provider can be
checked without a database (SQLite does not support `--idempotent`):

```bash
dotnet ef migrations script \
  --project src/Flirty.Migrations.PostgreSql \
  --startup-project src/Flirty.Migrations.PostgreSql --idempotent
```

## Provider selection via AddFlirty (#34)

Since **#34** `AddFlirty` registers the `FlirtyDbContext` itself on request – including the provider and
the matching `MigrationsAssembly`. The caller then does **not** have to call `AddDbContext` manually anymore:

```csharp
services.AddFlirty(o => o.UseSqlite("Data Source=flirty.db"));       // or:
services.AddFlirty(o => o.UsePostgreSql(connectionString));
services.AddFlirty(o => o.UseSqlServer(connectionString).ApplyMigrations());
```

Each `Use…` method internally sets the migrations assembly belonging to the provider
(`Flirty.Migrations.Sqlite`/`PostgreSql`/`SqlServer`, see the table above). The context is registered as
`Scoped` – the same lifetime as `IDialogStore`/`IFlirtyEngine`. A repeated
`Use…` call overwrites the previous provider selection.

The manual path via `AddDbContext<FlirtyDbContext>(…)` (see [Auto-migration](#auto-migration-at-startup-20))
remains valid – e.g. when the context should be configured more finely – and is now
**optional**. In addition, since #34 `AddFlirty` provides the swappable `o.UseExpressionEvaluator<T>()`
and the webhook registration `o.AddWebhook(name, url)` (stub, active delivery in EPIC 4/M2).

### Selecting the provider as a value (#37)

Since **#37** the provider can also be chosen **as a value** – necessary when it is only known **at runtime**
(e.g. the multi-DB connection profiles of the [Designer](./DESIGNER.md)). For this there is:

- the public enum **`FlirtyDatabaseProvider`** (`Sqlite`/`PostgreSql`/`SqlServer`) and
- the extension **`DbContextOptionsBuilder.UseFlirtyProvider(provider, connectionString)`**, which sets the
  matching EF Core provider **and** the correct `MigrationsAssembly` in one step.

```csharp
// Build options for an arbitrary profile at runtime:
var options = new DbContextOptionsBuilder<FlirtyDbContext>()
    .UseFlirtyProvider(FlirtyDatabaseProvider.PostgreSql, connectionString)
    .Options;
using var context = new FlirtyDbContext(options);

// or via the options API:
services.AddFlirty(o => o.UseProvider(FlirtyDatabaseProvider.SqlServer, connectionString));
```

`UseFlirtyProvider` is the **only** place where the three migrations-assembly names are anchored;
the type-specific `o.UseSqlite/UsePostgreSql/UseSqlServer` have delegated to `o.UseProvider(...)` since #37
and thus to the same mapping (no duplicated mapping anymore).

## Auto-migration at startup (#20)

Instead of calling `Database.Migrate()` manually, Flirty can apply the pending migrations automatically at
**host startup**. This is enabled via the options API:

```csharp
services.AddDbContext<FlirtyDbContext>(o =>
    o.UseSqlite(connectionString, sqlite => sqlite.MigrationsAssembly("Flirty.Migrations.Sqlite")));
services.AddFlirty(o => o.ApplyMigrations());
```

`o.ApplyMigrations()` registers the `FlirtyMigrationHostedService` – an `IHostedService` that in
`StartAsync` opens its own DI scope, resolves the `FlirtyDbContext` and runs `Database.MigrateAsync()`.
Deliberately an `IHostedService` (not a `BackgroundService`): the host **awaits** all `StartAsync`
before it counts as started (with ASP.NET Core, before Kestrel accepts requests). This way the schema is
migrated before the first request, and a migration error aborts the startup fail-fast. The DbContext is
resolved via `IServiceScopeFactory` (not injected), because the hosted service is a singleton while the
context is scoped.

> `o.ApplyMigrations()` presupposes a registered `FlirtyDbContext` including a provider and
> `MigrationsAssembly`. Since **#34** the provider selection
> `o.UseSqlite/UsePostgreSql/UseSqlServer` registers the context itself (see
> [Provider selection via AddFlirty](#provider-selection-via-addflirty-34)); the manual `AddDbContext` path
> as above stays optionally valid.

### Bundling the migration DLLs into the NuGet package

So that a consumer of the `Flirty` package can auto-migrate, the three migrations assemblies must ship
along. But `Flirty` **cannot** include them via `ProjectReference`: the
migration projects already reference `Flirty`, and a back-reference (even with
`ReferenceOutputAssembly=false`) would be a build-graph cycle. That is why a pack target in
`Flirty.csproj` builds the three projects via an `<MSBuild>` task (not part of the static build graph) and
places their DLLs into `lib/net10.0/` via `TargetsForTfmSpecificBuildOutput`/`BuildOutputInPackage`. At runtime
EF Core loads the migrations assembly chosen by name (`MigrationsAssembly("Flirty.Migrations.<Provider>")`)
from the consumer's probing path. Packaging details: [NUGET-PACKAGING.md](./NUGET-PACKAGING.md).

## IDialogStore (repository) (#21)

On top of the `FlirtyDbContext` sits the repository `IDialogStore` (implementation `DialogStore`, both
`internal` – they are consumed by the runtime layer in the same assembly, not by host apps).
It encapsulates the load/save operations that Start/Resume/Submit/Edit (#25) need, and keeps the
EF Core context out of the Mediator handlers.

| Method | Purpose | Tracking |
|---|---|---|
| `GetPublishedDialogAsync(key)` | highest **published** version for `key`, full graph | untracked |
| `GetDialogAsync(dialogId)` | exact version **pinned** by a session, by id (without the `IsPublished` filter) | untracked |
| `GetSessionAsync(sessionId)` | session incl. answers | **tracked** |
| `FindActiveSessionAsync(dialogId, externalUserKey)` | newest **running** session of a user | **tracked** |
| `AddSession(session)` | track a new session (incl. first answers) | – |
| `SaveChangesAsync()` | unit-of-work seam: save all changes in one batch | – |

Key decisions:

- **Dialog graph untracked + split query.** The configuration graph (questions/options, transitions,
  loops, triggers) is immutable at runtime; `AsNoTracking()` saves overhead. Because of the four
  sibling collections it is loaded via `AsSplitQuery()` to avoid a cartesian product.
- **`Dialog.Layout` is deliberately not loaded here.** Canvas positions (#102) are display data of the
  designer; the runtime has no use for them. Only `IDialogAdminStore.GetDialogGraphAsync`
  takes them along – that is the source of the graph view.
- **Session tracked.** Submit/Edit mutate the loaded session – therefore **no** `AsNoTracking`, otherwise
  the changes would be silently lost at `SaveChangesAsync`.
- **Separate loads across the aggregate boundary.** `DialogSession.DialogId` is not a foreign key; a
  session does not load its dialog automatically. Resume/Submit/Edit are therefore two loads
  (`GetSessionAsync` + `GetDialogAsync(session.DialogId)`).
- **Active session sorted client-side.** `FindActiveSessionAsync` sorts the candidates in-memory
  by `StartedAt`, because SQLite does not translate `DateTimeOffset` (stored as TEXT) in `ORDER BY`.
  At most one running session is expected per (dialog, user).
- **New children on loaded aggregates.** When attaching a `SessionAnswer` to an already
  **tracked** session, do not pre-set the `Id` – the Guid key is store-generated (EF convention);
  EF assigns it at `SaveChanges`. A pre-set id on a child of a tracked aggregate would be
  interpreted as an update instead of an insert.

`IDialogStore` has been registered in `AddFlirty()` as `Scoped` since #21 (the same lifetime as the
`FlirtyDbContext`). It can be resolved as soon as a `FlirtyDbContext` is registered (via the
provider selection `o.UseSqlite/…` since #34 or manually via `AddDbContext`).

## Test strategy

Acceptance criterion: *"the DB is created against each of the three providers."* The tests (`tests/Flirty.Tests`,
folder `Persistence/`) apply **all** migrations via `Database.Migrate()` per provider and check
a full aggregate round-trip (`ProviderMigrationAssertions`, sample data from
`TestDialogFactory`). The assertion lists the expected migration names individually and requires
`GetPendingMigrations()` to be empty – a forgotten provider set is thus caught, instead of staying silently
green:

- **SQLite** – a real in-memory DB over a held-open `SqliteConnection` (no external
  dependency, runs everywhere).
- **PostgreSQL / SQL Server** – real databases over **Testcontainers** (`Testcontainers.PostgreSql`,
  `Testcontainers.MsSql`). These need a running **Docker**. If Docker is missing (a local run without
  Docker), the two tests are cleanly **skipped** via `[SkippableFact]` + `Skip.IfNot(DockerAvailability.IsAvailable, …)`
  instead of failing. On CI (`ubuntu-latest`) Docker is present, so both
  providers are tested for real there.

The `IDialogStore` repository (#21) is additionally checked in `DialogStoreTests` against the same
SQLite in-memory database (an open `SqliteConnection` + `EnsureCreated()`): published-
vs. pinned-loading, the tracking contracts (dialog untracked, session tracked), the active-session filter
as well as the unit-of-work seam (`AddSession` + `SaveChangesAsync`).

## Provider-specific pitfalls

- **Timestamps UTC.** Npgsql maps `DateTimeOffset` to `timestamptz` and requires offset == UTC.
  Store timestamps therefore always UTC-normalized (see `TestDialogFactory.SampleTime`).
- **Index key length 256.** Domain keys (`Dialog.Key`, `Question.Key`, …) are limited to 256
  characters, because SQL Server does not allow `nvarchar(max)` as an index key.
- **JSON as text columns.** `Value`/`Config`/`ValidationRules` are stored as unbounded text columns
  – deliberately **without** provider-native `json`/`jsonb` types, so that the configuration stays the
  smallest common denominator of all providers.
- **No unique indexes over `null`-able columns** – divergent null semantics between SQL Server
  and SQLite/PostgreSQL.
- **`DateTimeOffset` storage** differs per provider: SQL Server `datetimeoffset`,
  PostgreSQL `timestamp with time zone`, SQLite `TEXT`. The UTC principle above keeps that consistent.

## Package versions (Central Package Management)

All versions are pinned centrally in `Directory.Packages.props`: the three EF Core 10 providers
(`10.0.9` resp. Npgsql `10.0.3`), `Microsoft.EntityFrameworkCore.Design` (`10.0.9`) as well as the
test dependencies `Testcontainers.PostgreSql`/`Testcontainers.MsSql` and `Xunit.SkippableFact`.
`TreatWarningsAsErrors=true` applies repo-wide – new transitive packages must not drag in
security advisories (NU1903).

## Scope / delineation

- **Auto-migration** (`o.ApplyMigrations()` → `FlirtyMigrationHostedService`) and the **bundling** of the
  migrations assemblies into the NuGet package: **#20** – implemented (see above). The minimal
  `FlirtyOptions` with `ApplyMigrations()` arose here; #34 extends it additively.
- **Options API** `AddFlirty(o => o.UseSqlite/UsePostgreSql/UseSqlServer)` (provider selection incl.
  `FlirtyDbContext` registration, `UseExpressionEvaluator`, webhook registration): **#34** –
  implemented (see [Provider selection via AddFlirty](#provider-selection-via-addflirty-34)). The active
  webhook delivery was added with **#33** (EPIC 4), see [TRIGGERS.md](./TRIGGERS.md#outbound-webhooks).
- **`IDialogStore`** (repository over `FlirtyDbContext`, incl. DI registration in `AddFlirty()`):
  **#21** – implemented (see above). The consuming commands/queries (Start/Resume/Submit/Edit) came
  with **#25**–**#28**, see [RUNTIME.md](./RUNTIME.md). The **admin CRUD** (#36, extended by loops
  in #41 and triggers in #42) hangs deliberately off its own repository `IDialogAdminStore`: the
  runtime `IDialogStore` **reads** the configuration graph and writes only session state, the
  admin counterpart writes the graph itself (generic `Add`/`Remove`/`RemoveRange` plus the
  key and reference queries of the CRUD commands).
- Decision basis: [ADR 0001 – Migrations per provider](./adr/0001-migrations-per-provider.md)
  (incl. addendum: the points #20 and #34/#37 still open there are done). Overview of all
  decisions: [docs/adr/](./adr/README.md).
