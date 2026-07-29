---
name: flirty-ef-migration
description: Create an EF Core migration for Flirty or make a domain/persistence change – synchronized across all three providers (SQLite, PostgreSQL, SQL Server). Use for "new migration", "dotnet ef", "change entity", "add column", "change DbContext/configuration", "adjust schema".
---

# EF migration per provider / domain change

Flirty keeps each provider migration in its **own assembly**
(`src/Flirty.Migrations.{Sqlite,PostgreSql,SqlServer}`), because EF Core assigns migrations to the
context in a provider-independent way and scans the whole assembly when applying them. **After every
model change all three sets must be regenerated with the same name.** Reference: `docs/PERSISTENCE.md`,
`docs/DOMAIN-MODEL.md`, ADR `docs/adr/0001-migrationen-pro-provider.md`.

## Provider mapping

| Provider | Migrations assembly / `--project` |
|---|---|
| SQLite | `src/Flirty.Migrations.Sqlite` |
| PostgreSQL | `src/Flirty.Migrations.PostgreSql` |
| SQL Server | `src/Flirty.Migrations.SqlServer` |

Each migrations project has an `internal sealed IDesignTimeDbContextFactory<FlirtyDbContext>` that sets
the provider together with `MigrationsAssembly` (the connection string is a placeholder – `migrations
add` does not connect).

## Step A – domain/persistence change (if the model is affected)

1. Create/change the entity in `src/Flirty/Domain/` (`sealed`, timestamps as **UTC** `DateTimeOffset`).
2. EF configuration in `src/Flirty/Persistence/Configurations/<Entity>Configuration.cs` (keys, indexes,
   relationships). Also wire a new entity into the `FlirtyDbContext`
   (`src/Flirty/Persistence/FlirtyDbContext.cs`).
3. Mind the **provider pitfalls** (from `docs/PERSISTENCE.md`):
   - Business keys as **text with length 256** (SQL Server does not allow `nvarchar(max)` as an index
     key) – constant in `PersistenceConstants.cs`.
   - JSON (`Value`/`Config`/`ValidationRules`) as **unbounded text columns**, **not** native
     `json`/`jsonb`.
   - Store enums as `int`.
   - **No** unique indexes over `null`-able columns (divergent null semantics).
   - Timestamps UTC-normalized (Npgsql `timestamptz` requires offset == UTC).

## Step B – generate a migration per provider

Once `dotnet tool restore` (dotnet-ef is a local tool, `.config/dotnet-tools.json`). Then **for each
provider** with the **same migration name**:

```pwsh
dotnet ef migrations add <Name> `
  --project src/Flirty.Migrations.Sqlite `
  --startup-project src/Flirty.Migrations.Sqlite `
  --context FlirtyDbContext --output-dir Migrations
# likewise: Flirty.Migrations.PostgreSql and Flirty.Migrations.SqlServer
```

`--project` and `--startup-project` both point at the **same** migrations project (whose design-time
factory supplies the provider + `MigrationsAssembly`).

## Step C – check SQL (without a database)

```pwsh
dotnet ef migrations script `
  --project src/Flirty.Migrations.PostgreSql `
  --startup-project src/Flirty.Migrations.PostgreSql --idempotent
```

Note: **SQLite does not support `--idempotent`** – script there without the flag.

## Step D – tests

`tests/Flirty.Tests/Persistence/` applies `InitialCreate`/migrations per provider via
`Database.Migrate()` and checks an aggregate round-trip (`ProviderMigrationAssertions`,
`TestDialogFactory`). PostgreSQL/SQL Server run via Testcontainers (Docker); without Docker they are
skipped via `[SkippableFact]`. SQLite always runs (in-memory).

## Definition of Done

All **three** migration sets up to date and identically named · English XML docs on new public domain
API · tests green (with Docker also PostgreSQL/SQL Server) ·
`docs/PERSISTENCE.md`/`docs/DOMAIN-MODEL.md` updated · on a fundamental decision possibly a new ADR in
`docs/adr/`.

## Verification

```pwsh
dotnet build Flirty.sln
dotnet test tests/Flirty.Tests
```
