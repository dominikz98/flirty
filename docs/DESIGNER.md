# Designer (Blazor)

Der **Flirty.Designer** ist eine Blazor Web App (Server-interaktiv, .NET 10) zum Anlegen und Bearbeiten
von Dialogen und zum Verwalten der Datenbank-Verbindungen. Er ist Teil von **EPIC 7** (Issues #37–#43,
Milestone „M3 – Designer"). Referenz: [ARCHITECTURE.md](./ARCHITECTURE.md) §4/§8, [PERSISTENCE.md](./PERSISTENCE.md).

> **Stand:** Umgesetzt sind die **Connection-Profil-Verwaltung (Multi-DB, #37)** und das
> **Dialog-CRUD (#38)**. Die restlichen Editoren (Frage-Editor #39, Branching #40, Loops #41,
> Trigger #42, Test-Runner #43) folgen. Der Designer arbeitet über die Admin-Commands der Engine
> (via `ISender`), nicht direkt am `FlirtyDbContext` vorbei.

## Starten

```pwsh
dotnet run --project src/Flirty.Designer
```

Standard-Ports: `http://localhost:5016` / `https://localhost:7173` (`Properties/launchSettings.json`).
Einstieg ist die Startseite; über die Navigation gelangt man zu **Verbindungen** (`/connections`) und
**Dialoge** (`/dialogs`).

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
| `FlirtyAdminGateway` | `Services/` | Führt die Admin-Commands je Operation in einem frischen DI-Scope aus (#38). |

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
builder.Services.AddScoped<FlirtyAdminGateway>();               // Admin-CRUD, #38
```

Die vorletzte Zeile bindet den (scoped) `FlirtyDbContext` an das aktive Profil – so laufen die
Admin-Commands automatisch gegen die gewählte Datenbank. Ist **kein** Profil aktiv, wirft die
Factory eine verständliche `InvalidOperationException`.

### Migrations-Assemblies referenzieren

`Flirty.Designer.csproj` referenziert **alle drei** `Flirty.Migrations.{Sqlite,PostgreSql,SqlServer}`.
Bei `ProjectReference` greift die NuGet-Bündelung der Migrations-DLLs nicht (siehe
[PERSISTENCE.md](./PERSISTENCE.md)), daher müssen sie explizit referenziert werden, damit „Migrieren"
für jeden Provider funktioniert.

## Dialog-CRUD (#38)

Zwei Seiten, beide server-interaktiv:

| Route | Komponente | Inhalt |
|---|---|---|
| `/dialogs` | `Components/Pages/Dialogs.razor` | Liste (Schlüssel, Name, Version, Status, Einstiegsfrage, Geändert) + Inline-Formular „Neuer Dialog" + Textfilter über Schlüssel/Name. |
| `/dialogs/{id:guid}` | `Components/Pages/DialogEditor.razor` | Metadaten bearbeiten, Einstiegsfrage wählen, veröffentlichen/zurückziehen, löschen. |

> Die Detailseite heißt bewusst **`DialogEditor`** und nicht `DialogDetail`: der generierte
> Komponententyp würde sonst den gleichnamigen Sichttyp `Flirty.Runtime.Admin.DialogDetail` verdecken.

Beide Seiten nutzen ausschließlich die Admin-Commands der Engine
(`CreateDialogCommand`, `UpdateDialogCommand`, `DeleteDialogCommand`, `PublishDialogCommand`,
`UnpublishDialogCommand`, `ListDialogsQuery`, `GetDialogQuery` aus `src/Flirty/Runtime/Admin/`).
Das Formular-Modell `Models/DialogFormModel.cs` spiegelt deren `[Required]`-Annotationen, damit der
`DataAnnotationsValidator` Verstöße schon im Browser meldet.

Regeln, die die UI sichtbar macht:

- Ein neuer Dialog entsteht als **Entwurf** (`Version = 1`, `IsPublished = false`, ohne Einstiegsfrage).
- **Veröffentlichen** ist deaktiviert, solange keine Einstiegsfrage gesetzt *und gespeichert* ist –
  `PublishDialogCommand` würde sonst mit `InvalidOperationException` abbrechen.
- Bei einem veröffentlichten Dialog weist ein Hinweis darauf hin, ihn vor dem Bearbeiten
  zurückzuziehen (laufende Sessions pinnen ihre `DialogVersion` und brechen nicht).
- **Löschen** fragt zweistufig **inline** nach (kein JS-`confirm`, das sonst die Playwright-E2E aus #46
  blockieren würde) und entfernt den gesamten Graphen per DB-Cascade.
- Die Auswahl der Einstiegsfrage listet die Fragen aus `GetDialogQuery`. Solange es keine gibt, ist sie
  deaktiviert – der Frage-Editor folgt in #39.

### Warum ein Gateway statt `@inject ISender`

`FlirtyAdminGateway` (`Services/FlirtyAdminGateway.cs`) führt **jede** Admin-Nachricht in einem
**eigenen DI-Scope** aus:

```csharp
var result = await Admin.ExecuteAsync((sender, token) => sender.Send(new ListDialogsQuery(), token));
if (!result.Success) { _error = result.Error; return; }
```

In Blazor Server entspricht ein DI-Scope einem **Circuit**. Der scoped `FlirtyDbContext` würde damit für
die ganze Sitzung leben – er wäre an das Profil gepinnt, das beim ersten Zugriff aktiv war (ein späterer
Profilwechsel bliebe wirkungslos), sein Change-Tracker liefe voll, und der nicht threadsichere Kontext
würde von parallelen Renderpfaden geteilt. Ein Scope pro Operation löst alle drei Punkte; das aktive
Profil des Circuits wird per `ActiveConnectionProfile.Adopt(...)` in den Kind-Scope durchgereicht.

Das Gateway liefert ein `AdminResult<T>` (`Success` / `Value` / `Error`) statt Ausnahmen, damit ein
Fehler eine Meldung erzeugt und nicht den Circuit killt. Das Mapping spiegelt den
`FlirtyExceptionEndpointFilter` aus `Flirty.AspNetCore` (Not-Found → Validierung → Konflikt) und ergänzt
Datenbankfehler um den Hinweis, das aktive Profil zu **migrieren** (typisch bei frischer SQLite-Datei).

## Konventionen

- Blazor-Komponenten unter `Components/` (Seiten in `Components/Pages/`), Server-interaktiver Render-Mode
  (`@rendermode InteractiveServer` auf interaktiven Seiten).
- Gemeinsame UI-Primitiven (`.editor`, `.field`, `.input`, `.btn`, `.data-table`, `.badge`, `.msg`,
  `.banner`, `.empty` …) liegen **global** in `wwwroot/app.css`; die `*.razor.css`-Dateien enthalten nur
  noch Seitenspezifisches. Neue Editor-Seiten nutzen diese Klassen, statt sie zu duplizieren.
- UI-Texte und Doku **deutsch**. Der Designer ist `IsPackable=false` → CS1591 ist hier **kein** Fehler,
  XML-Docs sind optional (die übrigen Warnungen bleiben aber via `TreatWarningsAsErrors` Fehler).
- Zeitstempel UTC.

## Tests

Die Service-Logik wird per **xUnit** in `tests/Flirty.Tests` geprüft (das Testprojekt referenziert den
Designer; Interna via `InternalsVisibleTo("Flirty.Tests")`):

- `Persistence/FlirtyDatabaseProviderExtensionsTests` – Core-Mapping Provider → EF-Provider + MigrationsAssembly.
- `Designer/JsonConnectionProfileStoreTests` – CRUD, Kopier-Semantik und Persistenz der Profile.
- `Designer/ConnectionProfileOperationsTests` – Test-Connection und Migrate gegen eine SQLite-Temp-DB.
- `Designer/FlirtyAdminGatewayTests` – Admin-CRUD über den echten DI-Stack gegen eine SQLite-Temp-DB:
  Anlegen/Auflisten, Fehler-Mapping (Schlüsselkonflikt, unbekannter Dialog, fehlendes Profil, nicht
  migrierte Datenbank) und – als Regression – dass ein **Profilwechsel sofort greift**.

Die UI selbst wird künftig per Playwright-E2E abgedeckt (`tests/Flirty.E2E`, #46 – noch offen).

```pwsh
dotnet test tests/Flirty.Tests
```

## Roadmap (EPIC 7)

#37 Connection-Profile ✅ → #38 Dialog-CRUD-UI ✅ → #39 Frage-Editor → #40 Branching-Editor →
#41 Loop-Visualisierung → #42 Trigger-Editor → #43 Test-Runner. Designer-E2E: #46.
