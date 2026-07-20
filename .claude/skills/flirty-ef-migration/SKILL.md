---
name: flirty-ef-migration
description: EF-Core-Migration für Flirty erzeugen oder eine Domain-/Persistenz-Änderung durchführen – über alle drei Provider (SQLite, PostgreSQL, SQL Server) synchron. Verwenden bei "neue Migration", "dotnet ef", "Entity ändern", "Spalte hinzufügen", "DbContext/Konfiguration ändern", "Schema anpassen".
---

# EF-Migration pro Provider / Domain-Änderung

Flirty hält jede Provider-Migration in einem **eigenen Assembly**
(`src/Flirty.Migrations.{Sqlite,PostgreSql,SqlServer}`), weil EF Core Migrationen provider-unabhängig
dem Context zuordnet und beim Anwenden das ganze Assembly scannt. **Nach jeder Modelländerung müssen
alle drei Sets mit gleichem Namen neu erzeugt werden.** Referenz: `docs/PERSISTENCE.md`,
`docs/DOMAIN-MODEL.md`, ADR `docs/adr/0001-migrationen-pro-provider.md`.

## Provider-Zuordnung

| Provider | Migrations-Assembly / `--project` |
|---|---|
| SQLite | `src/Flirty.Migrations.Sqlite` |
| PostgreSQL | `src/Flirty.Migrations.PostgreSql` |
| SQL Server | `src/Flirty.Migrations.SqlServer` |

Jedes Migrations-Projekt hat eine `internal sealed IDesignTimeDbContextFactory<FlirtyDbContext>`, die den
Provider samt `MigrationsAssembly` setzt (Connection-String ist ein Platzhalter – `migrations add`
verbindet nicht).

## Schritt A – Domain-/Persistenz-Änderung (falls Modell betroffen)

1. Entity in `src/Flirty/Domain/` anlegen/ändern (`sealed`, Timestamps als **UTC** `DateTimeOffset`).
2. EF-Konfiguration in `src/Flirty/Persistence/Configurations/<Entity>Configuration.cs` (Keys, Indizes,
   Beziehungen). Neue Entity zusätzlich im `FlirtyDbContext` (`src/Flirty/Persistence/FlirtyDbContext.cs`)
   verdrahten.
3. **Provider-Fallstricke** (aus `docs/PERSISTENCE.md`) beachten:
   - Fachliche Schlüssel als **Text mit Länge 256** (SQL Server erlaubt `nvarchar(max)` nicht als
     Indexschlüssel) – Konstante in `PersistenceConstants.cs`.
   - JSON (`Value`/`Config`/`ValidationRules`) als **unbegrenzte Textspalten**, **nicht** native
     `json`/`jsonb`.
   - Enums als `int` speichern.
   - **Keine** Unique-Indizes über `null`-fähige Spalten (divergente Null-Semantik).
   - Timestamps UTC-normalisiert (Npgsql `timestamptz` verlangt Offset == UTC).

## Schritt B – Migration je Provider erzeugen

Einmalig `dotnet tool restore` (dotnet-ef ist lokales Tool, `.config/dotnet-tools.json`). Dann **für
jeden Provider** mit **demselben Migrationsnamen**:

```pwsh
dotnet ef migrations add <Name> `
  --project src/Flirty.Migrations.Sqlite `
  --startup-project src/Flirty.Migrations.Sqlite `
  --context FlirtyDbContext --output-dir Migrations
# analog: Flirty.Migrations.PostgreSql und Flirty.Migrations.SqlServer
```

`--project` und `--startup-project` zeigen beide auf **dasselbe** Migrations-Projekt (dessen
Design-Time-Factory liefert Provider + `MigrationsAssembly`).

## Schritt C – SQL prüfen (ohne Datenbank)

```pwsh
dotnet ef migrations script `
  --project src/Flirty.Migrations.PostgreSql `
  --startup-project src/Flirty.Migrations.PostgreSql --idempotent
```

Hinweis: **SQLite unterstützt `--idempotent` nicht** – dort ohne das Flag scripten.

## Schritt D – Tests

`tests/Flirty.Tests/Persistence/` wendet je Provider `InitialCreate`/Migrationen via
`Database.Migrate()` an und prüft einen Aggregat-Round-Trip (`ProviderMigrationAssertions`,
`TestDialogFactory`). PostgreSQL/SQL Server laufen über Testcontainers (Docker); ohne Docker per
`[SkippableFact]` übersprungen. SQLite läuft immer (in-memory).

## Definition of Done

Alle **drei** Migrations-Sets aktuell und namensgleich · deutsche XML-Docs auf neuer public Domain-API ·
Tests grün (mit Docker auch PostgreSQL/SQL Server) · `docs/PERSISTENCE.md`/`docs/DOMAIN-MODEL.md`
aktualisiert · bei Grundsatzentscheidung ggf. neuer ADR in `docs/adr/`.

## Verifikation

```pwsh
dotnet build Flirty.sln
dotnet test tests/Flirty.Tests
```
