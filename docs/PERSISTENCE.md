# Persistenz: Provider & Migrationen

Wie Flirty EF Core an **SQLite**, **PostgreSQL** und **SQL Server** anbindet und die Migrationen je
Provider verwaltet. Umgesetzt in Issue **#19**. Referenz: [ARCHITECTURE.md](./ARCHITECTURE.md) §8,
Modell-Details in [DOMAIN-MODEL.md](./DOMAIN-MODEL.md).

## Überblick

Der Kern (`src/Flirty`) bleibt **provider-agnostisch**: `FlirtyDbContext` besitzt nur den
Options-Konstruktor und legt keinen Provider fest. Alle drei EF-Core-Provider werden mit dem
`Flirty`-NuGet-Paket ausgeliefert; der Konsument wählt zur Laufzeit einen davon aus.

| Provider | NuGet-Paket | Migrations-Assembly |
|---|---|---|
| SQLite | `Microsoft.EntityFrameworkCore.Sqlite` | `Flirty.Migrations.Sqlite` |
| PostgreSQL | `Npgsql.EntityFrameworkCore.PostgreSQL` | `Flirty.Migrations.PostgreSql` |
| SQL Server | `Microsoft.EntityFrameworkCore.SqlServer` | `Flirty.Migrations.SqlServer` |

Die Provider-`Use…`-Methoden (`UseSqlite`, `UseNpgsql`, `UseSqlServer`) stammen aus den
EF-Core-Provider-Paketen selbst. Die komfortable Options-API `AddFlirty(o => o.UseSqlite(…))`
(Provider-Wahl inkl. `FlirtyDbContext`-Registrierung) ist seit **#34** verfügbar; die Auto-Migration
darüber (`o.ApplyMigrations()` → `FlirtyMigrationHostedService`) kam in **#20**. Siehe
[Provider-Wahl über AddFlirty](#provider-wahl-über-addflirty-34).

## Warum getrennte Migrations-Assemblies?

EF Core ordnet Migrationen dem `FlirtyDbContext` **provider-unabhängig** zu und scannt beim
Anwenden das gesamte Migrations-Assembly. Lägen die `InitialCreate`-Migrationen aller drei Provider
im selben Assembly, käme es zu doppelten Migrations-IDs, und `Database.Migrate()` würde versuchen,
provider-fremdes SQL (z. B. SQLite-DDL gegen PostgreSQL) anzuwenden.

Deshalb liegt **jede Provider-Migration in einem eigenen Assembly** (`src/Flirty.Migrations.<Provider>`).
Zur Laufzeit wählt der Aufruf die passende Migrations-Assembly:

```csharp
new DbContextOptionsBuilder<FlirtyDbContext>()
    .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly("Flirty.Migrations.PostgreSql"))
    .Options;
```

Diese Projekte sind `IsPackable=false` (nur In-Repo, für `dotnet ef` und Tests). Ihre DLLs werden
jedoch beim Packen ins `Flirty`-NuGet-Paket **mitgebündelt** (siehe [Auto-Migration](#auto-migration-beim-start-20)),
damit auch reine Paket-Konsumenten migrieren können.

## Projekt-Layout

```
src/
├─ Flirty                     Core: FlirtyDbContext + Konfigurationen, referenziert alle 3 Provider
├─ Flirty.Migrations.Sqlite       InitialCreate + SqliteDesignTimeDbContextFactory
├─ Flirty.Migrations.PostgreSql   InitialCreate + PostgreSqlDesignTimeDbContextFactory
└─ Flirty.Migrations.SqlServer    InitialCreate + SqlServerDesignTimeDbContextFactory
```

Jedes Migrations-Projekt referenziert `Flirty` (bringt Context + Provider transitiv) und
`Microsoft.EntityFrameworkCore.Design` (`PrivateAssets=all`). Eine `internal sealed`
`IDesignTimeDbContextFactory<FlirtyDbContext>` konfiguriert den jeweiligen Provider samt
`MigrationsAssembly`, damit `dotnet ef` den Context ohne laufende App bauen kann (der
Connection-String darin ist ein Platzhalter – `migrations add`/`script` verbinden nicht).

## Migrationen erzeugen

`dotnet ef` ist als lokales Tool gepinnt (`.config/dotnet-tools.json`); einmalig
`dotnet tool restore` genügt. Eine neue/aktualisierte Migration wird **für jeden Provider einzeln**
erzeugt (gleicher Name, damit die Sets synchron bleiben):

```bash
dotnet ef migrations add InitialCreate \
  --project src/Flirty.Migrations.Sqlite \
  --startup-project src/Flirty.Migrations.Sqlite \
  --context FlirtyDbContext --output-dir Migrations
# analog für Flirty.Migrations.PostgreSql und Flirty.Migrations.SqlServer
```

Nach jeder Modelländerung müssen **alle drei** Sets neu erzeugt werden. Das SQL je Provider lässt
sich ohne Datenbank prüfen (SQLite unterstützt kein `--idempotent`):

```bash
dotnet ef migrations script \
  --project src/Flirty.Migrations.PostgreSql \
  --startup-project src/Flirty.Migrations.PostgreSql --idempotent
```

## Provider-Wahl über AddFlirty (#34)

Seit **#34** registriert `AddFlirty` den `FlirtyDbContext` auf Wunsch selbst – inklusive Provider und
passender `MigrationsAssembly`. Der Aufrufer muss `AddDbContext` dann **nicht** mehr manuell aufrufen:

```csharp
services.AddFlirty(o => o.UseSqlite("Data Source=flirty.db"));       // oder:
services.AddFlirty(o => o.UsePostgreSql(connectionString));
services.AddFlirty(o => o.UseSqlServer(connectionString).ApplyMigrations());
```

Jede `Use…`-Methode setzt intern die zum Provider gehörende Migrations-Assembly
(`Flirty.Migrations.Sqlite`/`PostgreSql`/`SqlServer`, siehe Tabelle oben). Der Kontext wird als
`Scoped` registriert – dieselbe Lebensdauer wie `IDialogStore`/`IFlirtyEngine`. Ein erneuter
`Use…`-Aufruf überschreibt die vorige Provider-Wahl.

Der manuelle Weg über `AddDbContext<FlirtyDbContext>(…)` (siehe [Auto-Migration](#auto-migration-beim-start-20))
bleibt weiterhin gültig – z. B. wenn der Kontext feiner konfiguriert werden soll – und ist nun
**optional**. Zusätzlich stellt `AddFlirty` seit #34 den austauschbaren `o.UseExpressionEvaluator<T>()`
und die Webhook-Registrierung `o.AddWebhook(name, url)` (Stub, aktive Auslieferung in EPIC 4/M2) bereit.

### Provider als Wert wählen (#37)

Seit **#37** lässt sich der Provider auch **als Wert** wählen – nötig, wenn er erst **zur Laufzeit**
feststeht (z. B. die Multi-DB-Connection-Profile des [Designers](./DESIGNER.md)). Dafür gibt es:

- das öffentliche Enum **`FlirtyDatabaseProvider`** (`Sqlite`/`PostgreSql`/`SqlServer`) und
- die Extension **`DbContextOptionsBuilder.UseFlirtyProvider(provider, connectionString)`**, die den
  passenden EF-Core-Provider **und** die korrekte `MigrationsAssembly` in einem Schritt setzt.

```csharp
// Optionen für ein beliebiges Profil zur Laufzeit bauen:
var options = new DbContextOptionsBuilder<FlirtyDbContext>()
    .UseFlirtyProvider(FlirtyDatabaseProvider.PostgreSql, connectionString)
    .Options;
using var context = new FlirtyDbContext(options);

// oder über die Options-API:
services.AddFlirty(o => o.UseProvider(FlirtyDatabaseProvider.SqlServer, connectionString));
```

`UseFlirtyProvider` ist die **einzige** Stelle, an der die drei Migrations-Assembly-Namen verankert sind;
die typspezifischen `o.UseSqlite/UsePostgreSql/UseSqlServer` delegieren seit #37 auf `o.UseProvider(...)`
und damit auf dieselbe Abbildung (kein dupliziertes Mapping mehr).

## Auto-Migration beim Start (#20)

Statt `Database.Migrate()` manuell aufzurufen, kann Flirty die ausstehenden Migrationen beim
**Host-Start** automatisch anwenden. Aktiviert wird das über die Options-API:

```csharp
services.AddDbContext<FlirtyDbContext>(o =>
    o.UseSqlite(connectionString, sqlite => sqlite.MigrationsAssembly("Flirty.Migrations.Sqlite")));
services.AddFlirty(o => o.ApplyMigrations());
```

`o.ApplyMigrations()` registriert den `FlirtyMigrationHostedService` – ein `IHostedService`, das in
`StartAsync` einen eigenen DI-Scope öffnet, den `FlirtyDbContext` auflöst und `Database.MigrateAsync()`
ausführt. Bewusst `IHostedService` (nicht `BackgroundService`): der Host **awaited** alle `StartAsync`,
bevor er als gestartet gilt (bei ASP.NET Core, bevor Kestrel Requests annimmt). So ist das Schema vor
dem ersten Request migriert, und ein Migrationsfehler bricht den Start fail-fast ab. Der DbContext wird
per `IServiceScopeFactory` aufgelöst (nicht injiziert), weil der Hosted Service Singleton, der Context
aber scoped ist.

> `o.ApplyMigrations()` setzt einen registrierten `FlirtyDbContext` inkl. Provider und
> `MigrationsAssembly` voraus. Seit **#34** registriert die Provider-Wahl
> `o.UseSqlite/UsePostgreSql/UseSqlServer` den Context selbst (siehe
> [Provider-Wahl über AddFlirty](#provider-wahl-über-addflirty-34)); der manuelle `AddDbContext`-Weg
> wie oben bleibt optional gültig.

### Bündelung der Migrations-DLLs ins NuGet-Paket

Damit ein Konsument des `Flirty`-Pakets auto-migrieren kann, müssen die drei Migrations-Assemblies mit
ausgeliefert werden. `Flirty` kann sie aber **nicht** per `ProjectReference` einbinden: die
Migrations-Projekte referenzieren bereits `Flirty`, ein Rückverweis (auch mit
`ReferenceOutputAssembly=false`) wäre ein Build-Graph-Zyklus. Deshalb baut ein Pack-Target in
`Flirty.csproj` die drei Projekte per `<MSBuild>`-Task (nicht Teil des statischen Build-Graphen) und legt
ihre DLLs über `TargetsForTfmSpecificBuildOutput`/`BuildOutputInPackage` nach `lib/net10.0/`. Zur Laufzeit
lädt EF Core die per Name gewählte Migrations-Assembly (`MigrationsAssembly("Flirty.Migrations.<Provider>")`)
aus dem Probing-Pfad des Konsumenten. Details zum Packaging: [NUGET-PACKAGING.md](./NUGET-PACKAGING.md).

## IDialogStore (Repository) (#21)

Über dem `FlirtyDbContext` liegt das Repository `IDialogStore` (Implementierung `DialogStore`, beide
`internal` – konsumiert werden sie von der Runtime-Schicht im selben Assembly, nicht von Host-Apps).
Es kapselt die Lade-/Speicheroperationen, die Start/Resume/Submit/Edit (#25) brauchen, und hält den
EF-Core-Kontext aus den Mediator-Handlern heraus.

| Methode | Zweck | Tracking |
|---|---|---|
| `GetPublishedDialogAsync(key)` | höchste **veröffentlichte** Version zu `key`, voller Graph | ungetrackt |
| `GetDialogAsync(dialogId)` | exakte, von einer Session **gepinnte** Version per Id (ohne `IsPublished`-Filter) | ungetrackt |
| `GetSessionAsync(sessionId)` | Session inkl. Antworten | **getrackt** |
| `FindActiveSessionAsync(dialogId, externalUserKey)` | neueste **laufende** Session eines Anwenders | **getrackt** |
| `AddSession(session)` | neue Session (inkl. erster Antworten) tracken | – |
| `SaveChangesAsync()` | Unit-of-Work-Naht: alle Änderungen gebündelt speichern | – |

Wesentliche Entscheidungen:

- **Dialog-Graph ungetrackt + Split-Query.** Der Konfigurationsgraph (Fragen/Optionen, Übergänge,
  Schleifen, Trigger) ist zur Laufzeit unveränderlich; `AsNoTracking()` spart Overhead. Wegen der vier
  Geschwister-Collections wird per `AsSplitQuery()` geladen, um ein kartesisches Produkt zu vermeiden.
- **Session getrackt.** Submit/Edit mutieren die geladene Session – daher **kein** `AsNoTracking`, sonst
  gingen die Änderungen bei `SaveChangesAsync` still verloren.
- **Getrennte Loads über die Aggregatgrenze.** `DialogSession.DialogId` ist kein Fremdschlüssel; eine
  Session lädt ihren Dialog nicht automatisch. Resume/Submit/Edit sind daher zwei Loads
  (`GetSessionAsync` + `GetDialogAsync(session.DialogId)`).
- **Aktiv-Session client-seitig sortiert.** `FindActiveSessionAsync` sortiert die Kandidaten in-memory
  nach `StartedAt`, weil SQLite `DateTimeOffset` (als TEXT gespeichert) nicht in `ORDER BY` übersetzt.
  Pro (Dialog, Anwender) wird höchstens eine laufende Session erwartet.
- **Neue Kinder an geladenen Aggregaten.** Beim Anhängen eines `SessionAnswer` an eine bereits
  **getrackte** Session die `Id` nicht vorbelegen – der Guid-Key ist store-generiert (EF-Konvention);
  EF vergibt ihn beim `SaveChanges`. Eine vorbelegte Id an einem Kind eines getrackten Aggregats würde
  als Update statt Insert interpretiert.

Registriert wird `IDialogStore` seit #21 in `AddFlirty()` als `Scoped` (gleiche Lebensdauer wie der
`FlirtyDbContext`). Aufgelöst werden kann es, sobald ein `FlirtyDbContext` registriert ist (per
Provider-Wahl `o.UseSqlite/…` seit #34 oder manuell per `AddDbContext`).

## Test-Strategie

Akzeptanzkriterium: *„DB wird gegen jeden der drei Provider erzeugt."* Die Tests (`tests/Flirty.Tests`,
Ordner `Persistence/`) wenden je Provider die `InitialCreate`-Migration via `Database.Migrate()` an
und prüfen einen vollständigen Aggregat-Round-Trip (`ProviderMigrationAssertions`,
Beispieldaten aus `TestDialogFactory`):

- **SQLite** – reale in-memory-DB über eine offen gehaltene `SqliteConnection` (keine externe
  Abhängigkeit, läuft überall).
- **PostgreSQL / SQL Server** – reale Datenbanken über **Testcontainers** (`Testcontainers.PostgreSql`,
  `Testcontainers.MsSql`). Diese benötigen ein laufendes **Docker**. Fehlt Docker (lokaler Lauf ohne
  Docker), werden die beiden Tests via `[SkippableFact]` + `Skip.IfNot(DockerAvailability.IsAvailable, …)`
  sauber **übersprungen** statt zu scheitern. Auf CI (`ubuntu-latest`) ist Docker vorhanden, sodass beide
  Provider dort real getestet werden.

Das `IDialogStore`-Repository (#21) wird zusätzlich in `DialogStoreTests` gegen dieselbe
SQLite-in-memory-Datenbank geprüft (offene `SqliteConnection` + `EnsureCreated()`): veröffentlicht-
vs. gepinnt-Laden, die Tracking-Verträge (Dialog ungetrackt, Session getrackt), der Aktiv-Session-Filter
sowie die Unit-of-Work-Naht (`AddSession` + `SaveChangesAsync`).

## Provider-spezifische Fallstricke

- **Zeitstempel UTC.** Npgsql mappt `DateTimeOffset` auf `timestamptz` und verlangt Offset == UTC.
  Timestamps daher stets UTC-normalisiert ablegen (siehe `TestDialogFactory.SampleTime`).
- **Index-Schlüssellänge 256.** Fachliche Schlüssel (`Dialog.Key`, `Question.Key`, …) sind auf 256
  Zeichen begrenzt, weil SQL Server `nvarchar(max)` nicht als Indexschlüssel zulässt.
- **JSON als Textspalten.** `Value`/`Config`/`ValidationRules` werden als unbegrenzte Textspalten
  gespeichert – bewusst **ohne** provider-native `json`/`jsonb`-Typen, damit die Konfiguration der
  kleinste gemeinsame Nenner aller Provider bleibt.
- **Keine Unique-Indizes über `null`-fähige Spalten** – divergente Null-Semantik zwischen SQL Server
  und SQLite/PostgreSQL.
- **`DateTimeOffset`-Speicherung** unterscheidet sich je Provider: SQL Server `datetimeoffset`,
  PostgreSQL `timestamp with time zone`, SQLite `TEXT`. Der obige UTC-Grundsatz hält das konsistent.

## Paketversionen (Central Package Management)

Alle Versionen sind zentral in `Directory.Packages.props` gepinnt: die drei EF-Core-10-Provider
(`10.0.9` bzw. Npgsql `10.0.3`), `Microsoft.EntityFrameworkCore.Design` (`10.0.9`) sowie die
Test-Abhängigkeiten `Testcontainers.PostgreSql`/`Testcontainers.MsSql` und `Xunit.SkippableFact`.
`TreatWarningsAsErrors=true` gilt repo-weit – neue transitive Pakete dürfen keine
Security-Advisories (NU1903) einschleppen.

## Abgrenzung

- **Auto-Migration** (`o.ApplyMigrations()` → `FlirtyMigrationHostedService`) und das **Bündeln** der
  Migrations-Assemblies ins NuGet-Paket: **#20** – umgesetzt (siehe oben). Das minimale
  `FlirtyOptions` mit `ApplyMigrations()` entstand hier; #34 erweitert es additiv.
- **Options-API** `AddFlirty(o => o.UseSqlite/UsePostgreSql/UseSqlServer)` (Provider-Wahl inkl.
  `FlirtyDbContext`-Registrierung, `UseExpressionEvaluator`, Webhook-Registrierung): **#34** –
  umgesetzt (siehe [Provider-Wahl über AddFlirty](#provider-wahl-über-addflirty-34)). Die aktive
  Webhook-Auslieferung kam mit **#33** dazu (EPIC 4), siehe [TRIGGERS.md](./TRIGGERS.md#outbound-webhooks).
- **`IDialogStore`** (Repository über `FlirtyDbContext`, inkl. DI-Registrierung in `AddFlirty()`):
  **#21** – umgesetzt (siehe oben). Die konsumierenden Commands/Queries (Start/Resume/Submit/Edit) kamen
  mit **#25**–**#28**, siehe [RUNTIME.md](./RUNTIME.md). Das **Admin-CRUD** (#36, erweitert um Schleifen
  in #41 und Trigger in #42) hängt bewusst an einem eigenen Repository `IDialogAdminStore`: Der
  Laufzeit-`IDialogStore` **liest** den Konfigurationsgraphen und schreibt nur Session-Zustand, das
  Admin-Gegenstück schreibt den Graphen selbst (generisches `Add`/`Remove`/`RemoveRange` plus die
  Schlüssel- und Verweis-Abfragen der CRUD-Commands).
- Entscheidungsgrundlage: [ADR 0001 – Migrationen pro Provider](./adr/0001-migrationen-pro-provider.md).
