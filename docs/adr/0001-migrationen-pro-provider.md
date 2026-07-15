# ADR 0001 – Migrationen pro Provider (getrennte Assemblies)

- **Status:** Akzeptiert
- **Kontext-Issue:** #19 – Provider SQLite / PostgreSQL / SQL Server + Migrationen
- **Betroffen:** `src/Flirty`, `src/Flirty.Migrations.*`, `tests/Flirty.Tests`

## Kontext

Flirty unterstützt drei EF-Core-Provider (SQLite, PostgreSQL, SQL Server) und liefert sie in einem
einzigen `Flirty`-NuGet-Paket aus; der Konsument wählt einen zur Laufzeit. Der `FlirtyDbContext` ist
provider-agnostisch. Für die DB-Erzeugung sind Migrationen nötig – und Migrationen sind in EF Core
**provider-spezifisch** (das erzeugte DDL unterscheidet sich je Provider, z. B. `datetimeoffset` vs.
`timestamp with time zone` vs. `TEXT`).

EF Core ordnet Migrationen dem `DbContext` **unabhängig vom Provider** zu und scannt beim Anwenden
das gesamte Migrations-Assembly nach `[Migration]`-Typen. Mehrere Provider-Migrationssets müssen also
sauber getrennt werden, sonst kollidieren sie.

## Entscheidung

Jede Provider-Migration liegt in einem **eigenen Assembly**:

- `Flirty.Migrations.Sqlite`
- `Flirty.Migrations.PostgreSql`
- `Flirty.Migrations.SqlServer`

Jedes Projekt referenziert `Flirty` (Context + Provider transitiv) und
`Microsoft.EntityFrameworkCore.Design`, enthält eine `internal`-`IDesignTimeDbContextFactory` für
`dotnet ef` und wählt seine eigene `MigrationsAssembly`. Zur Laufzeit selektiert der Aufruf über
`Use…(cs, b => b.MigrationsAssembly("Flirty.Migrations.<Provider>"))` das passende Set.

## Verworfene Alternativen

- **Ein Assembly, drei Ordner/Namespaces.** EF filtert Migrationen nicht nach Provider oder
  Namespace; alle drei `InitialCreate`-Migrationen würden gefunden → doppelte IDs bzw. Anwendung
  provider-fremden SQL. Technisch nicht tragfähig.
- **Ein Assembly, Provider per Build-Flag umschalten.** Erlaubt zur Bauzeit nur *einen* Provider und
  widerspricht dem „ein Paket, alle drei Provider, Wahl zur Laufzeit"-Modell.

## Konsequenzen

- **Positiv:** Sauber getrennte, zur Laufzeit selektierbare Migrationen; entspricht der offiziellen
  EF-Core-Empfehlung für Multi-Provider. Jeder Provider wird via Testcontainers real verifiziert.
- **Negativ:** Bei Modelländerungen müssen **drei** Migrationssets erzeugt/gepflegt werden.
- **Offen:** Die Migrations-Assemblies sind derzeit `IsPackable=false`. Das Bündeln ins
  `Flirty`-NuGet-Paket und die Auto-Anwendung beim Start folgen in **#20**; die komfortable
  Provider-Options-API in **#34**.

Details: [PERSISTENCE.md](../PERSISTENCE.md).
