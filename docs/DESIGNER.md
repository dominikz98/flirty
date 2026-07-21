# Designer (Blazor)

Der **Flirty.Designer** ist eine Blazor Web App (Server-interaktiv, .NET 10) zum Anlegen und Bearbeiten
von Dialogen und zum Verwalten der Datenbank-Verbindungen. Er ist Teil von **EPIC 7** (Issues #37–#43,
Milestone „M3 – Designer"). Referenz: [ARCHITECTURE.md](./ARCHITECTURE.md) §4/§8, [PERSISTENCE.md](./PERSISTENCE.md).

> **Stand:** Bisher ist die **Connection-Profil-Verwaltung (Multi-DB, #37)** umgesetzt. Die eigentlichen
> Editoren (Dialog-CRUD #38, Frage-Editor #39, Branching #40, Loops #41, Trigger #42, Test-Runner #43)
> folgen. Der Designer arbeitet – wo möglich – über die Admin-Commands der Engine (via `ISender`), nicht
> direkt am `FlirtyDbContext` vorbei.

## Starten

```pwsh
dotnet run --project src/Flirty.Designer
```

Standard-Ports: `http://localhost:5016` / `https://localhost:7173` (`Properties/launchSettings.json`).
Einstieg ist die Startseite; über die Navigation gelangt man zu **Verbindungen** (`/connections`).

## Connection-Profil-Verwaltung (Multi-DB, #37)

Der Designer kann gegen **mehrere Datenbanken** arbeiten. Ein *Connection-Profil* bündelt einen
Provider (`FlirtyDatabaseProvider`: SQLite / PostgreSQL / SQL Server) und die Verbindungszeichenfolge.
Auf der Seite **Verbindungen** (`/connections`) lassen sich Profile:

- **anlegen/bearbeiten/löschen** (Name, Provider-Auswahl, Verbindungszeichenfolge),
- **testen** („Testen" → `Database.CanConnectAsync()`),
- **migrieren** („Migrieren" → wendet ausstehende Migrationen via `Database.MigrateAsync()` an und meldet,
  welche angewendet wurden),
- **aktivieren** – das aktive Profil bestimmt, gegen welche Datenbank der Designer (und ab #38 die
  Admin-Commands) arbeitet.

> **SQLite-Hinweis:** „Testen" meldet erst dann Erfolg, wenn die Datei existiert. Bei einem frischen
> SQLite-Profil daher zuerst **migrieren** (legt die Datei + Schema an), dann testen.

### Ablage der Profile (Sicherheitshinweis)

Profile werden als **Klartext-JSON** in `connection-profiles.json` im ContentRoot des Designers abgelegt
(Ablage außerhalb der Flirty-Datenbank, weil die Profile ja erst die Verbindung dorthin herstellen).
Die Datei kann **Secrets** (Passwörter in Verbindungszeichenfolgen) enthalten und ist deshalb per
`.gitignore` ausgeschlossen. Für ein lokales Entwickler-Werkzeug ist das bewusst einfach gehalten – wird
der Designer in einer geteilten Umgebung betrieben, ist ein sichererer Speicher (User-Secrets, KeyVault
o. Ä.) vorzusehen.

## Architektur der Profilwahl

Der Kern (`Flirty`) bleibt provider-agnostisch. Für die Laufzeit-Wahl stellt er seit #37 zwei
öffentliche Bausteine bereit (siehe [PERSISTENCE.md → Provider als Wert wählen](./PERSISTENCE.md#provider-als-wert-wählen-37)):

- `FlirtyDatabaseProvider` (Enum) und
- `DbContextOptionsBuilder.UseFlirtyProvider(provider, connectionString)` – setzt Provider **und**
  passende `MigrationsAssembly` in einem Schritt.

Darauf setzt der Designer auf (`src/Flirty.Designer/`):

| Baustein | Pfad | Aufgabe |
|---|---|---|
| `ConnectionProfile` | `Models/ConnectionProfile.cs` | Profil-Modell (Id, Name, Provider, ConnectionString). |
| `IConnectionProfileStore` / `JsonConnectionProfileStore` | `Services/` | CRUD + Standardprofil, persistiert als JSON. |
| `ActiveConnectionProfile` | `Services/ActiveConnectionProfile.cs` | Hält das aktive Profil (Scoped = pro Circuit). |
| `FlirtyDesignerDbContextFactory` | `Services/` | `IDbContextFactory<FlirtyDbContext>` gegen das **aktive** Profil. |
| `ConnectionProfileOperations` | `Services/` | Test-Connection / Migrations-Status / Migrate für ein **beliebiges** Profil. |
| `ConnectionProfileContextBuilder` | `Services/` | Baut aus einem Profil via `UseFlirtyProvider` einen `FlirtyDbContext`. |
| Seite `ConnectionProfiles.razor` | `Components/Pages/` | UI (`/connections`), server-interaktiv. |

### DI-Verdrahtung (`Program.cs`)

Der Designer ruft **`AddFlirty()` ohne Provider** auf (Engine/Admin/Mediator, aber kein fester
`FlirtyDbContext`). Stattdessen wird der Kontext pro aktivem Profil über die Factory erzeugt:

```csharp
builder.Services.AddFlirty();                                   // Engine ohne fest verdrahteten Provider

builder.Services.AddSingleton<IConnectionProfileStore>(sp => new JsonConnectionProfileStore(
    Path.Combine(sp.GetRequiredService<IWebHostEnvironment>().ContentRootPath, "connection-profiles.json")));
builder.Services.AddSingleton<ConnectionProfileOperations>();
builder.Services.AddScoped<ActiveConnectionProfile>();
builder.Services.AddScoped<IDbContextFactory<FlirtyDbContext>, FlirtyDesignerDbContextFactory>();
builder.Services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<FlirtyDbContext>>().CreateDbContext());
```

Die letzte Zeile bindet den (scoped) `FlirtyDbContext` an das aktive Profil – so laufen die
Admin-Commands ab #38 automatisch gegen die gewählte Datenbank. Ist **kein** Profil aktiv, wirft die
Factory eine verständliche `InvalidOperationException`.

### Migrations-Assemblies referenzieren

`Flirty.Designer.csproj` referenziert **alle drei** `Flirty.Migrations.{Sqlite,PostgreSql,SqlServer}`.
Bei `ProjectReference` greift die NuGet-Bündelung der Migrations-DLLs nicht (siehe
[PERSISTENCE.md](./PERSISTENCE.md)), daher müssen sie explizit referenziert werden, damit „Migrieren"
für jeden Provider funktioniert.

## Konventionen

- Blazor-Komponenten unter `Components/` (Seiten in `Components/Pages/`), Server-interaktiver Render-Mode
  (`@rendermode InteractiveServer` auf interaktiven Seiten).
- UI-Texte und Doku **deutsch**. Der Designer ist `IsPackable=false` → CS1591 ist hier **kein** Fehler,
  XML-Docs sind optional (die übrigen Warnungen bleiben aber via `TreatWarningsAsErrors` Fehler).
- Zeitstempel UTC.

## Tests

Die Service-Logik wird per **xUnit** in `tests/Flirty.Tests` geprüft (das Testprojekt referenziert den
Designer; Interna via `InternalsVisibleTo("Flirty.Tests")`):

- `Persistence/FlirtyDatabaseProviderExtensionsTests` – Core-Mapping Provider → EF-Provider + MigrationsAssembly.
- `Designer/JsonConnectionProfileStoreTests` – CRUD, Kopier-Semantik und Persistenz der Profile.
- `Designer/ConnectionProfileOperationsTests` – Test-Connection und Migrate gegen eine SQLite-Temp-DB.

Die UI selbst wird künftig per Playwright-E2E abgedeckt (`tests/Flirty.E2E`, #46 – noch offen).

```pwsh
dotnet test tests/Flirty.Tests
```

## Roadmap (EPIC 7)

#37 Connection-Profile ✅ → #38 Dialog-CRUD-UI → #39 Frage-Editor → #40 Branching-Editor →
#41 Loop-Visualisierung → #42 Trigger-Editor → #43 Test-Runner. Designer-E2E: #46.
