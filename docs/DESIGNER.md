# Designer (Blazor)

Der **Flirty.Designer** ist eine Blazor Web App (Server-interaktiv, .NET 10) zum Anlegen und Bearbeiten
von Dialogen und zum Verwalten der Datenbank-Verbindungen. Er ist Teil von **EPIC 7** (Issues #37–#43,
Milestone „M3 – Designer"; die Playwright-E2E der UI kam mit #46 in M4 dazu). Referenz:
[ARCHITECTURE.md](./ARCHITECTURE.md) §4/§8, [PERSISTENCE.md](./PERSISTENCE.md).

> **Stand:** EPIC 7 ist umgesetzt: **Connection-Profil-Verwaltung (Multi-DB, #37)**, **Dialog-CRUD
> (#38)**, **Frage-Editor (#39)**, **Branching-Editor (#40)**, **Loop-Editor (#41)**,
> **Trigger-Editor (#42)** und **Test-Runner (#43)**; die UI ist seit **#46** per Playwright-E2E
> abgedeckt. Aus **EPIC 11** (visueller Graph-Designer, #99) sind die **Graph-Ansicht (#101)**, die
> **Layout-Persistenz (#102)** und das **Editieren auf dem Canvas (#103)** dazugekommen – der Canvas ist
> damit ein Editor, der Formular- und Listenpfad bleibt gleichwertig erhalten. Der Designer arbeitet über
> die Commands der Engine (via `ISender`), nicht direkt am `FlirtyDbContext` vorbei.

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
  Admin-Commands) arbeitet,
- **löschen** – zweistufig inline bestätigt, wie alle Löschaktionen des Designers. Wird das **aktive**
  Profil gelöscht, gibt `ActiveConnectionProfile.Clear()` es auch im laufenden Circuit frei; ohne diesen
  Schritt arbeitete der Designer bis zum nächsten vollständigen Reload gegen ein Profil weiter, das in
  der Verwaltung nicht mehr existiert.

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
| `FlirtyRuntimeGateway` | `Services/` | Dasselbe für die Laufzeit-Operationen des Test-Runners (#43). |

### DI-Verdrahtung (`DesignerApp`)

Die gesamte Komposition liegt in `src/Flirty.Designer/DesignerApp.cs`
(`ConfigureServices(WebApplicationBuilder)` + `Configure(WebApplication)`); `Program.cs` ruft nur noch
beides auf. Grund für die Auslagerung ist die Playwright-E2E (#46), die denselben Aufbau in-Prozess
hostet – dasselbe Muster wie `WebSampleApp` in `Flirty.Samples.Web`.

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

builder.Services.AddScoped<DesignerTriggerLog>();               // Test-Runner, #43
builder.Services.AddScoped<FlirtyRuntimeGateway>();
builder.Services
    .AddFlirtyHandler<DialogStartedNotification, DesignerTriggerLogHandlers.DialogStarted>()
    .AddFlirtyHandler<AnswerSubmittedNotification, DesignerTriggerLogHandlers.AnswerSubmitted>()
    .AddFlirtyHandler<QuestionAnsweredNotification, DesignerTriggerLogHandlers.QuestionAnswered>()
    .AddFlirtyHandler<DialogCompletedNotification, DesignerTriggerLogHandlers.DialogCompleted>();
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
- Hat der Graph **offene Übergangs-Warnungen** (dieselben Regeln wie je Ausgangsfrage, gesammelt in
  `GraphWarnings`), wiederholt der Abschnitt „Veröffentlichung & Löschen" sie und fragt vor dem
  Veröffentlichen zurück. Grund: Veröffentlicht ist der Graph gesperrt – ein durchgerutschter
  Konfigurationsfehler (etwa ein bedingter Übergang ohne Default) kostet dann eine neue Version, während
  laufende Sessions bereits in den 409 der Laufzeit laufen (#97).
- Ein **veröffentlichter** Dialog ist gesperrt: Die Editoren für Fragen, Übergänge, Schleifen, Trigger
  und die Einstiegsfrage sind deaktiviert, ein Banner nennt die beiden Auswege (neue Version anlegen
  oder zurückziehen). Name und Beschreibung bleiben änderbar. Details unten unter
  [Versionierung](#versionierung-95).
- **Löschen** fragt zweistufig **inline** nach (kein JS-`confirm`, das sonst die Playwright-E2E aus #46
  blockieren würde) und entfernt den gesamten Graphen per DB-Cascade.
- Die Auswahl der Einstiegsfrage listet die Fragen aus `GetDialogQuery`. Solange es keine gibt, ist sie
  deaktiviert – Fragen entstehen im **Frage-Editor** (nächster Abschnitt).

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

## Versionierung (#95)

Eine **veröffentlichte** Dialogversion ist unveränderlich – die Engine lehnt Graph-Änderungen daran mit
`DialogPublishedException` (→ 409) ab, damit laufende Sessions nicht brechen (Begründung und verworfene
Alternativen: [ADR 0005](./adr/0005-unveraenderliche-veroeffentlichte-dialogversion.md), Mechanik:
[RUNTIME.md § Versions-Pinning](./RUNTIME.md#versions-pinning)). Der Designer spiegelt diese Regel,
statt den Anwender in die Fehlermeldung laufen zu lassen:

- Jede Seite kennt eine Eigenschaft `Editable` (`_detail is not null && !_detail.Dialog.IsPublished`).
  Daran hängen die Mutations-Schaltflächen aller Graph-Abschnitte – Anlegen, Sortieren (↑/↓), Löschen,
  Speichern in den Detail-Editoren sowie die Auswahl der Einstiegsfrage. **Ansehen bleibt möglich:**
  „Bearbeiten" navigiert weiterhin in die Detailseiten, dort ist nur das Speichern gesperrt.
- Der Banner am veröffentlichten Dialog nennt die beiden Wege und bietet **„Neue Version anlegen"** an
  (`CreateDialogVersionCommand`): Das klont den Graphen als Entwurf mit der nächsten Versionsnummer und
  wechselt direkt in dessen Editor. Ab da arbeitet man am Entwurf – der Seitenwechsel ist Absicht.
- **Veröffentlichen** der neuen Version zieht die bisher produktive zurück (je Schlüssel ist höchstens
  eine Version veröffentlicht). In der Dialogliste stehen die Versionen als eigene Zeilen, sortiert nach
  Schlüssel und Version.
- Der **Löschen**-Abschnitt zeigt die Anzahl laufender Sessions (`CountActiveSessionsQuery`) und bietet
  „Laufende Sessions beenden" an (`AbandonDialogSessionsCommand`, Status `Abandoned`, Antworten bleiben).
  Ohne diesen Schritt lehnt die Engine das Löschen ab, weil die Sessions es überlebten und danach weder
  fortsetzbar noch lesbar wären.
- **Ausnahme: die Canvas-Positionen.** `SetDialogLayoutCommand`/`ResetDialogLayoutCommand` laufen nicht
  unter der Sperre – ein veröffentlichter Dialog lässt sich also übersichtlich anordnen, obwohl sein
  Graph gesperrt ist. Das ist keine Lücke, sondern der Rand des Geltungsbereichs: Die Positionen liegen
  in einer eigenen Tabelle, die nicht zum Graphen gehört ([ADR 0007](./adr/0007-layout-als-eigene-tabelle.md)).

> **Der Test-Runner ist davon nicht betroffen:** Er startet über `StartDialogVersionAsync` eine konkrete
> Version – auch einen Entwurf. Genau dafür gibt es ihn (#43): eine neue Version durchspielen, *bevor*
> sie veröffentlicht wird.

## Frage-Editor (#39)

Fragen werden zweistufig gepflegt: die **Liste** hängt im Dialog-Editor, die **Details** einer Frage
(Validierung, Antwortoptionen) haben eine eigene Seite.

| Route | Komponente | Inhalt |
|---|---|---|
| `/dialogs/{id:guid}` | `DialogEditor.razor`, Abschnitt „Fragen" | Tabelle (Position, Schlüssel, Text, Typ, Pflicht, Optionen-Anzahl, Einstiegs-Badge), Inline-Formular „Neue Frage", Sortieren via ↑/↓, Löschen mit Inline-Bestätigung. |
| `/dialogs/{dialogId:guid}/questions/{questionId:guid}` | `QuestionEditor.razor` | Metadaten (Schlüssel, Text, Typ, Pflicht), Validierungsregeln, Antwortoptionen, Frage löschen. |

> Auch diese Seite heißt bewusst **`QuestionEditor`** und nicht `QuestionDetail` – sonst verdeckte der
> generierte Komponententyp den Sichttyp `Flirty.Runtime.Admin.QuestionDetail` (gleiche Falle wie bei
> `DialogEditor`).

Verwendet werden ausschließlich die Admin-Commands `Create/Update/DeleteQuestionCommand` und
`Create/Update/DeleteAnswerOptionCommand` (via `FlirtyAdminGateway`). Der `QuestionEditor` lädt seinen
Zustand mit **einem** `GetDialogQuery`: der liefert Fragen inklusive Optionen und dazu die
Dialog-Metadaten für Titel, Einstiegs-Badge und Veröffentlichungs-Hinweis.

### Reihenfolge

Die ↑/↓-Schaltflächen schreiben den **Positionsindex** als neue `Order` – nicht bloß die beiden Werte
vertauscht. Das repariert nebenbei doppelte oder lückenhafte `Order`-Werte, bei denen ein Tausch
wirkungslos bliebe (auf `Order` liegt bewusst kein Unique-Index, nur `{DialogId, Key}` ist eindeutig).
Alle betroffenen `UpdateQuestionCommand`s laufen in **einem** `ExecuteAsync`-Aufruf, also im selben
DI-Scope mit einem gemeinsamen Fehlerpfad. Für Antwortoptionen gilt dasselbe.

### Validierungsregeln

`Question.ValidationRules` ist eine JSON-Spalte; maßgeblich ist der öffentliche Core-Typ
`Flirty.Validation.ValidationRules` (`minLength`, `maxLength`, `min`, `max`, `pattern`, siehe
[VALIDATION.md](./VALIDATION.md)). Das Formular-Modell `Models/QuestionFormModel.cs` bildet ihn auf
Eingabefelder ab und benutzt ihn direkt als Serialisierungstyp – das Schema wird **nicht** dupliziert.

- **Typ-skopiert:** Die Engine wertet Längen/Muster nur bei `FreeText` und Min/Max nur bei `Number` aus.
  Die UI blendet entsprechend um, und gespeichert werden ausschließlich die zum aktuellen Typ passenden
  Regeln – nach einem Typwechsel bleibt kein wirkungsloser Ballast im JSON stehen.
- **Muster werden beim Speichern übersetzt** (`new Regex(...)` mit demselben 250-ms-Timeout wie im
  `AnswerValidator`). Ein ungültiger Ausdruck wird mit deutscher Meldung abgelehnt, statt erst zur
  Laufzeit als `InvalidOperationException` beim Validieren einer Antwort aufzuschlagen. Analog werden
  vertauschte Grenzen (`MinLength > MaxLength`, `Min > Max`) abgefangen.
- **Sind keine Regeln gesetzt**, wird `null` gespeichert – kein leeres `{}` in der Spalte.
- **Roh-JSON-Fallback:** Enthält das gespeicherte JSON Felder, die `ValidationRules` nicht kennt, oder ist
  es kein gültiges JSON-Objekt, zeigt der Editor statt der Einzelfelder ein Textfeld mit dem Roh-JSON
  (plus Warnhinweis). Die Eingabe wird nur auf Lesbarkeit geprüft und unverändert durchgereicht – ein
  Speichern darf fremde Felder nicht stillschweigend verwerfen.

### Antwortoptionen

Der Options-Abschnitt erscheint bei `SingleChoice`/`MultiChoice` – und zusätzlich immer dann, wenn noch
Optionen vorhanden sind, damit nach einem Typwechsel verwaiste Optionen sichtbar und löschbar bleiben
(mit Hinweis, dass sie wirkungslos sind). Ein Choice-Typ **ohne** Optionen wird gewarnt: gegen eine leere
Optionsliste ist keine Antwort gültig. Gespeichert und validiert wird der *Wert*; die *Beschriftung* ist
reiner Anzeigetext für die Host-UI.

### Zusammenspiel mit dem Dialog-Editor

- Nach dem Anlegen bleibt die Ansicht in der Liste (zügiges Erfassen mehrerer Fragen); Validierung und
  Optionen pflegt man danach im Frage-Editor.
- Fragen-Operationen laden den Graphen neu, überschreiben dabei aber **nicht** das Metadaten-Formular –
  sonst gingen dort gerade getippte, ungespeicherte Änderungen verloren. Nur die Auswahl der
  Einstiegsfrage wird abgeglichen, falls die gewählte Frage serverseitig wegfiel.
- `DeleteQuestionCommand` entfernt verweisende Übergänge mit und setzt eine darauf zeigende
  Einstiegsfrage zurück; die UI weist darauf hin, und „Veröffentlichen" sperrt danach wieder.

## Branching-Editor (#40)

Übergänge (`Transition`) werden wie die Fragen zweistufig gepflegt: die **Liste** hängt im Dialog-Editor,
die **Bedingung** einer Verzweigung hat eine eigene Seite mit Live-Validierung.

| Route | Komponente | Inhalt |
|---|---|---|
| `/dialogs/{id:guid}` | `DialogEditor.razor`, Abschnitt „Übergänge (Branching)" | Je Ausgangsfrage eine Tabelle (Position, Bedingung, Ziel, Default-/Rücksprung-Badge), Warnungen, ↑/↓, Löschen mit Inline-Bestätigung, Inline-Formular „Neuer Übergang" (auch je Gruppe über „+ Übergang"). |
| `/dialogs/{dialogId:guid}/transitions/{transitionId:guid}` | `TransitionEditor.razor` | Ausgangs-/Zielfrage, Default-Kennzeichen, Bedingung mit Live-Validierung, Baustein-Einfüger, Bezeichner-Referenz, Löschen. |

> Auch diese Seite heißt bewusst **`TransitionEditor`** – `TransitionDetail` würde den gleichnamigen
> Sichttyp aus `Flirty.Runtime.Admin` verdecken (gleiche Falle wie bei `DialogEditor`/`QuestionEditor`).

Verwendet werden ausschließlich `Create/Update/DeleteTransitionCommand` (via `FlirtyAdminGateway`); der
Zustand kommt aus **einem** `GetDialogQuery`. Die **Priorität** wird nicht direkt getippt: ↑/↓ schreibt
den Positionsindex **innerhalb der Ausgangsfrage** als neue `Priority` (alle Updates in einem
Gateway-Aufruf) – dasselbe Muster wie bei Fragen und Optionen. Wechselt man im Editor die Ausgangsfrage,
bekommt der Übergang die nächste freie Priorität der neuen Gruppe, statt still mit einem bestehenden
Übergang zu kollidieren.

### Live-Validierung über den Musterkontext

Die Bedingung wird bei **jeder Eingabe** über `IExpressionEvaluator.Validate(...)` kompiliert (nicht
ausgeführt) und der Status grün/rot angezeigt – bei gemeldeter Position mit einer `^`-Zeile unter der
Fehlerstelle. Beim Speichern läuft dieselbe Prüfung **blockierend**: ein ungültiger Ausdruck fiele sonst
erst in einer laufenden Session auf (`ExpressionEvaluationException` mitten im Dialog).

Dafür baut `Services/DesignerExpressionContext.cs` einen **Musterkontext** – das Gegenstück zum
Core-internen `SessionExpressionContextBuilder`, nur ohne Session:

| Fragetyp | Beispielwert (roh, als JSON) | Typ im Ausdruck |
|---|---|---|
| `FreeText` | `"Text"` | `string` |
| `Number` | `0` | `long` |
| `Boolean` | `true` | `bool` |
| `Date` | `"2026-01-01"` | **`string`** (wie zur Laufzeit – kein Vergleich mit `now` möglich) |
| `SingleChoice` | erster Optionswert als JSON-String | `string` |
| `MultiChoice` | JSON-Array der Optionswerte | Liste (`.Count`, `.Contains`) |

Maßgeblich sind die **Typen**, nicht die Werte: Sie spiegeln exakt die Deserialisierung des
`DynamicExpressoExpressionEvaluator` (siehe [BRANCHING-EXPRESSIONS.md](./BRANCHING-EXPRESSIONS.md)).
Loop-Collections werden – wie vom `LoopResolver` zur Laufzeit – **stets** gebunden (vor der ersten
Iteration als leere Liste), damit `skills.Count > 0` prüfbar ist; dafür liefert `GetDialogQuery` die
Schleifen-Marker seit #40 lesend mit (`DialogDetail.Loops`).

Nicht referenzierbare Schlüssel werden in der Referenztabelle als **„nicht nutzbar"** ausgewiesen statt
stillschweigend zu fehlen: Schlüssel, die keine gültigen Bezeichner sind (`vor-name`), und solche, die
von den reservierten Kontext-Variablen `now`/`iterationIndex`/`session` verdeckt werden (der Evaluator
setzt sie zuletzt).

> Die Fehlermeldung stammt aus der Ausdrucks-Engine (DynamicExpresso) und ist **englisch**
> („Unknown identifier 'rolle' (at index 0)"). Der Designer rahmt sie deutsch ein, statt sie zu
> übersetzen – so bleibt sie zur Engine-Ausgabe konsistent und übersteht einen Engine-Tausch.

### Baustein-Einfüger

Der Ausdruck bleibt ein Textfeld (kein Rückwärts-Parsen). Darunter setzt ein Einfüger aus
**Variable / Operator / Wert** einen Baustein zusammen und hängt ihn per `&&`/`||` an. Die angebotenen
Operatoren richten sich nach der Wertart (Zahl: `== != > >= < <=`; Liste: `Anzahl >`, `Anzahl ==`,
`enthält`), und der Vergleichswert wird typgerecht quotiert. Das Quotieren läuft bewusst **nicht** über
`JsonSerializer`: dessen `\u00XX`-Escapes lehnt der Parser der Engine ab („Invalid character escape
sequence") – erzeugt werden nur die C#-Escapes, die DynamicExpresso kennt.

### Warnungen (nicht blockierend)

Die Übergangsliste spiegelt die Regeln des `TransitionResolver` und meldet Konfigurationen, die zur
Laufzeit anders wirken als gedacht:

- **Kein Default und kein bedingungsloser Übergang** → trifft keine Bedingung zu, bricht die Session ab.
- **Mehrere Defaults** → es greift nur der oberste.
- **Default mit Bedingung** → die Bedingung wird nicht ausgewertet (der Resolver prüft sie nicht).
- **Bedingungsloser Übergang mit Nachfolgern** → er greift immer, die nachfolgenden werden nie geprüft.
- **Rücksprung** (Ziel liegt nicht nach der Ausgangsfrage) → Badge; den Marker dazu pflegt der
  [Loop-Editor](#loop-editor-41).
- **Frage ohne ausgehende Übergänge** → Hinweis „der Dialog endet nach dieser Frage".
- **Verwaiste Übergänge** (Ausgangsfrage existiert nicht mehr) werden sichtbar gemacht und lassen sich
  löschen. Über den Designer entstehen sie nicht – die Admin-API prüft Frage-Verweise aber bewusst nicht.

## Loop-Editor (#41)

Schleifen sind **Branching + Marker**: Den Zyklus bilden die Übergänge, die `LoopDefinition` legt nur die
Metadaten-Ebene darüber (Details: [LOOPS.md](./LOOPS.md)). Der Designer pflegt deshalb ausschließlich den
**Marker** – angelegt wird der Zyklus im Branching-Editor.

| Route | Komponente | Inhalt |
|---|---|---|
| `/dialogs/{id:guid}` | `DialogEditor.razor`, Abschnitt „Schleifen (Loops)" | Tabelle (Collection, Einstieg, Breaking, Bereichsgröße, Warnungs-Badge), Löschen mit Inline-Bestätigung, Inline-Formular „Neue Schleife" und die Vorschläge aus unmarkierten Rücksprüngen. |
| `/dialogs/{dialogId:guid}/loops/{loopId:guid}` | `LoopEditor.razor` | Loop-Block, `CollectionKey`, Einstiegs-/Breaking-Frage, Warnungen, Löschen. |

> Auch diese Seite heißt bewusst **`LoopEditor`** – `LoopDetail` würde den gleichnamigen Sichttyp aus
> `Flirty.Runtime.Admin` verdecken (gleiche Falle wie bei `DialogEditor`/`QuestionEditor`/`TransitionEditor`).

Verwendet werden `Create/Update/DeleteLoopCommand` (via `FlirtyAdminGateway`), der Zustand kommt aus
**einem** `GetDialogQuery`. Neu in #41 sind auch die REST-Endpunkte
(`POST {prefix}/dialogs/{dialogId}/loops`, `PUT|DELETE .../loops/{loopId}`) und `Loops` in der
`DialogDetailResponse` – bis dahin waren die Marker nur lesend erreichbar.

Der `CollectionKey` muss **im Dialog eindeutig** sein; das prüft der Command-Handler (409 an der REST-Schicht).
Ohne diese Prüfung würden sich zwei gleichnamige Marker zur Laufzeit still überschreiben – `LoopResolver`
baut die Collections in ein Dictionary, der zuletzt aufgebaute Marker gewänne.

Frage-Verweise prüft die Admin-API bewusst **nicht** (wie bei `Transition`); der Designer weist stattdessen
darauf hin. Umgekehrt räumt `DeleteQuestionCommand` seit #41 verweisende Marker mit ab – wie schon die
Übergänge –, damit kein Marker auf einer gelöschten Frage stehenbleibt.

### Loop-Block

`Services/LoopAnalyzer.cs` leitet den **Schleifenbereich** aus dem Übergangs-Graphen ab und spiegelt dabei
die Vorberechnung des Core-internen `LoopResolver`:
`(vorwärts ab Entry, Stopp an Breaking) ∩ (rückwärts zu Breaking) ∪ {Entry, Breaking}`. Der Resolver selbst
ist nicht wiederverwendbar – er ist `internal` und arbeitet auf einer `Dialog`-Entity mit Navigationen,
während der Designer nur `DialogDetail` hat (dieselbe Abgrenzung wie `DesignerExpressionContext` ↔
`SessionExpressionContextBuilder`). Gegen ein Auseinanderlaufen sichert `LoopAnalyzerTests` ab, indem es
beide Implementierungen auf demselben Graphen vergleicht.

Angezeigt werden die Bereichsfragen in Dialog-Reihenfolge mit den Badges **Einstieg**/**Breaking**; unter der
Breaking Question stehen ihre Übergänge getrennt als **↩ Rücksprung** (Ziel im Bereich) und **⇥ Ausstieg**
(Ziel außerhalb), jeweils mit Bedingung und Link in den Übergangs-Editor.

### Warnungen (nicht blockierend)

| Situation | Warum sie zählt |
|---|---|
| Einstiegs-/Breaking-Frage gehört nicht (mehr) zum Dialog | Der Marker zeigt ins Leere und sammelt nichts. |
| Kein Rücksprung Breaking → Entry | Es entsteht gar kein Zyklus; die nächste Iteration startet nur über die **Einstiegsfrage**. |
| **Kein Ausstieg** aus dem Bereich | Endlosschleife – die Kernwarnung aus #41. |
| **Ausstieg unerreichbar** | Ein bedingungsloser Nicht-Default-Rücksprung steht vor jedem Ausstieg (oder der oberste Default zeigt zurück in den Bereich): Nach den Regeln des `TransitionResolver` greift immer der Rücksprung. Ebenfalls eine Endlosschleife. |
| Überlappende Schleifenbereiche | Der `LoopResolver` wirft schon im Konstruktor – **jede** Session gegen den Dialog bricht ab. |
| `CollectionKey` verdeckt einen Frage-Schlüssel bzw. ist kein gültiger Bezeichner / reserviert | Die Frage bzw. die Collection ist in Bedingungen nicht referenzierbar. Die Prüfung teilt sich `DesignerExpressionContext.IsBindable`/`IdentifierNote` mit der Bezeichner-Referenz des Branching-Editors. |

### Vorschläge aus Rücksprüngen

Rücksprung-Übergänge ohne passenden Marker listet der Dialog-Editor als Hinweis auf – ohne Marker
**überschreibt** die Laufzeit die Antworten des Zyklus, statt sie je Iteration zu sammeln. Ein Klick öffnet
das Anlege-Formular vorbelegt: Einstiegsfrage = Ziel des Rücksprungs, Breaking Question = dessen
Ausgangsfrage, `CollectionKey` = Frage-Schlüssel plus `_liste` (`skill` → `skill_liste`, `belag` →
`belag_liste`). Bewusst **keine** Pluralbildung mit angehängtem „s": Die passt nur zu englischen
Schlüsseln und erzeugt in einem deutschsprachigen Dialog Wortmüll wie `belags` (#97). Kollidiert der
Vorschlag mit einem vorhandenen Frage-/Collection-Schlüssel oder ist er kein gültiger Bezeichner, bleibt
das Feld leer – ein stiller Ausweichname wäre schwerer nachzuvollziehen als ein leeres Pflichtfeld.

## Trigger-Editor (#42)

Trigger sind die **Rückkanäle** eines Dialogs in die Host-Anwendung (Details:
[TRIGGERS.md](./TRIGGERS.md)). Der Designer pflegt sie als `TriggerDefinition`-Zeilen am Dialog; die
Engine stellt Webhook-Trigger seitdem selbst zu – konfiguriert ist also nicht mehr nur dokumentiert.

| Route | Komponente | Inhalt |
|---|---|---|
| `/dialogs/{id:guid}` | `DialogEditor.razor`, Abschnitt „Trigger" | Tabelle (Zeitpunkt, Frage, Kanal, Ziel, Bedingung), Löschen mit Inline-Bestätigung, Inline-Formular „Neuer Trigger". |
| `/dialogs/{dialogId:guid}/triggers/{triggerId:guid}` | `TriggerEditor.razor` | Zeitpunkt + Frage-Bezug, Kanal + Konfiguration, Bedingung mit Live-Validierung, Löschen. |

> Auch diese Seite heißt bewusst **`TriggerEditor`** – `TriggerDetail` würde den gleichnamigen Sichttyp
> aus `Flirty.Runtime.Admin` verdecken (gleiche Falle wie bei `DialogEditor`/`QuestionEditor`/
> `TransitionEditor`/`LoopEditor`).

Verwendet werden `Create/Update/DeleteTriggerCommand` (via `FlirtyAdminGateway`), der Zustand kommt aus
**einem** `GetDialogQuery`. Neu in #42 sind neben dem CRUD auch die REST-Endpunkte
(`POST {prefix}/dialogs/{dialogId}/triggers`, `PUT|DELETE .../triggers/{triggerId}`) und `Triggers` in der
`DialogDetailResponse`. Eine **Reihenfolge** gibt es hier nicht – `TriggerDefinition` hat kein
`Order`/`Priority`, alle passenden Trigger feuern; die Liste ist nur stabil sortiert (Zeitpunkt, Kanal,
Konfiguration).

### Konfiguration (`Config`)

Das JSON der Spalte wird über den öffentlichen Core-Typ **`Flirty.Domain.TriggerConfig`** gelesen und
geschrieben – dasselbe Muster wie `ValidationRules` im Frage-Editor (#39), also **kein** Schema-Duplikat im
Designer. `Models/TriggerFormModel.cs` bildet die Felder auf zwei Eingaben ab:

- **Ziel-URL** (`url`) – nur bei Kanal *Webhook* sichtbar und dort Pflicht; geprüft wird beim Speichern
  über `TriggerConfig.TryValidate(kind, …)`, also mit **derselben** Regel wie im Command-Handler.
- **Ereignisname** (`name`) – optional, wird bei der Zustellung als Header `X-Flirty-Trigger` mitgeliefert.

Enthält das gespeicherte JSON unbekannte Felder (oder ist es kein Objekt), schaltet der Editor auf ein
**Roh-JSON-Feld** um und gibt den Text unverändert weiter – sonst würde das Speichern fremde Felder
stillschweigend verwerfen (Muster aus #39).

### Zeitpunkt und Frage-Bezug

Der Frage-Bezug gehört ausschließlich zu `AfterQuestion`: dort ist er Pflicht (nur nach dieser Frage feuert
der Trigger), bei allen anderen Zeitpunkten muss er leer sein. Beides erzwingen `CreateTriggerCommand`/
`UpdateTriggerCommand` über `IValidatableObject` – das vorhandene `ValidationPipelineBehavior` führt die
Prüfung aus (an der REST-Schicht: HTTP 400). Die UI blendet die Auswahl passend ein und normalisiert den
Wert (`TriggerFormModel.NormalizedQuestionId()`), statt sich auf die Fehlermeldung zu verlassen.

Wie bei Übergängen und Schleifen prüft die Admin-API den Frage-**Verweis** selbst nicht; umgekehrt räumt
`DeleteQuestionCommand` seit #42 verweisende Trigger mit ab, damit keiner auf einer gelöschten Frage
stehenbleibt und nie mehr feuert.

### Bedingung

Die Bedingung nutzt **unverändert** `DesignerExpressionContext` aus #40 – `TriggerDefinition.Expression`
läuft über dieselbe Engine und denselben Musterkontext wie `Transition.Expression`. Entsprechend gibt es
auch hier Live-Prüfung mit Caret-Position, Baustein-Einfüger und Bezeichner-Referenz; gespeichert wird
**nur** ein gültiger Ausdruck.

Zwei Hinweise gibt der Editor zusätzlich:

- **Beim Dialogstart** liegen noch keine Antworten vor. Eine Bedingung auf einen Fragen-Schlüssel lässt
  sich zur Laufzeit nicht auswerten – der Fehler wird protokolliert und der Trigger feuert nicht.
- **Kanal `InProcess`** stellt nichts zu: die Notification wird ohnehin publiziert, behandelt wird sie von
  einem Handler der Host-App (`AddFlirtyHandler<T, THandler>()`). Der Eintrag benennt die Absicht.

## Test-Runner (#43)

Der Test-Runner spielt einen Dialog **mit der echten Engine** durch – erreichbar über „Durchspielen" im
Dialog-Editor oder direkt unter `/dialogs/{dialogId}/test` (`DialogTestRunner.razor`). Er ist das
Abnahme-Feature von EPIC 7: Fragen, Branching, Schleifen und Trigger lassen sich damit ausprobieren,
ohne eine Host-App zu bauen.

Seit #104 hat er **zwei Ansichten desselben Laufs**: die hier beschriebene Verlaufsliste und den Graphen
mit dem gelaufenen Pfad (§ [Testlauf im Graphen](#testlauf-im-graphen-104)). Alles unter dieser
Überschrift gilt für beide – die Ansicht wechselt nur, was aus dem Lauf gezeigt wird.

### Entwürfe durchspielen

Der Runner startet über das Core-API **`IFlirtyEngine.StartDialogVersionAsync(dialogId, …)`** (#43,
siehe [RUNTIME.md](./RUNTIME.md#startdialogversioncommand-43)) statt über `StartDialogAsync(dialogKey, …)`.
Der Unterschied ist der ganze Punkt: `StartDialogAsync` löst über den fachlichen Schlüssel auf und startet
nur **veröffentlichte** Dialoge – ein Entwurf wäre nicht testbar, und „zum Testen kurz veröffentlichen"
würde ihn für echte Anwender scharf schalten. Alles ab dem Start ist unverändert: Die Session pinnt ihre
`DialogId`, Submit/Resume/Edit laden ihre Dialogversion ohnehin veröffentlichungs-unabhängig.

Voraussetzung ist lediglich eine gesetzte (und gespeicherte) **Einstiegsfrage**; ohne sie ist
„Durchspielen" deaktiviert.

### Der Lauf ist echt

Ein Testlauf ist keine Simulation – er schreibt in die Datenbank des aktiven Profils und löst Trigger aus.
Der Runner weist beides oben als Banner aus:

- Es entsteht eine echte `DialogSession` samt `SessionAnswer`-Zeilen. Der Anwenderschlüssel ist je Lauf
  frisch und trägt das Präfix **`designer-test-`** – damit sind Testsessions in der Datenbank erkennbar
  und ein neuer Lauf beginnt garantiert neu, statt die noch offene Session des letzten Laufs
  fortzusetzen (Resume). Aufgeräumt wird **nicht**: Die Engine kennt bewusst kein Löschen von Sessions.
- Am Dialog konfigurierte **Webhook**-Trigger werden tatsächlich per HTTP zugestellt (seit #42, siehe
  [TRIGGERS.md](./TRIGGERS.md)). Vor einem Testlauf gegen produktive Ziele also die URL prüfen.

### Verlauf, Iterationen und Editieren

Nach jedem Schritt liest der Runner den Zustand über `ResumeDialogAsync` neu – eine Quelle für Verlauf,
aktuelle Frage und Ausdruckskontext. Der Verlauf zeigt je Antwort den Frage-Schlüssel, den lesbaren Wert
(Options-**Beschriftung** statt Rohwert, `true` → „Ja") und – der Kern des Akzeptanzkriteriums – bei
Loop-Antworten ein Badge **`Iteration n`**; Antworten derselben `LoopInstanceId` sind als Bereich
abgesetzt.

Jede Zeile lässt sich **bearbeiten** (`EditAnswerAsync`). Der Iterationsindex wird mitgegeben, damit
innerhalb einer Schleife genau die angeklickte Iteration getroffen wird und nicht die früheste; die
Meldung nennt, wie viele nachgelagerte Antworten dabei verworfen wurden.

### Ausdruckskontext

Das Panel „Ausdruckskontext" zeigt, **womit die Bedingungen gerade rechnen**: je Frage die zuletzt
gegebene Antwort, je Schleife die gesammelten Werte und den `iterationIndex` – alles als roher JSON-Text,
genau wie im `ExpressionContext` der Engine. Damit wird nachvollziehbar, warum ein Übergang gegriffen hat.

> **`iterationIndex` richtig lesen:** Er meint den Index der **zuletzt gegebenen** Antwort auf die offene
> Frage, nicht die bevorstehende Iteration (Semantik von `LoopResolver.ResolveIterationIndex`). Deshalb
> steht er nur im Kontext-Panel und bewusst **nicht** als „laufende Iteration" an der aktuellen Frage –
> dort wäre er irreführend.

### Trigger-Protokoll

Das Panel „Trigger" listet oben, was die Engine im Lauf publiziert hat (Zeitpunkt/`TriggerScope`, Frage,
Kurzbeschreibung), darunter die am Dialog konfigurierten `TriggerDefinition`s. `InProcess`-Einträge werden
dabei ausdrücklich als „stellt die Engine nicht selbst zu" benannt.

### Bausteine

| Baustein | Pfad | Aufgabe |
|---|---|---|
| `DesignerGateway` | `Services/DesignerGateway.cs` | Gemeinsame Basis beider Gateways: frischer DI-Scope je Operation, `Adopt`-Durchreichung, Fehler-Mapping (`GatewayResult<T>`). |
| `FlirtyRuntimeGateway` | `Services/FlirtyRuntimeGateway.cs` | Führt die `IFlirtyEngine`-Aufrufe aus; ergänzt das Mapping um `DialogNotFound`/`SessionNotFound`/`AnswerValidation`. |
| `AnswerValueCodec` | `Services/AnswerValueCodec.cs` | **Einzige** Quelle des JSON-Vertrags je `QuestionType` (Kodieren, Anzeigen, Zurücklesen). |
| `RunExpressionContext` | `Services/RunExpressionContext.cs` | Spiegelt den Core-`SessionExpressionContextBuilder` auf `DialogDetail` + `ResumeDialogResult`. |
| `DesignerTriggerLog` (+ `…Handlers`) | `Services/` | Sammelt die publizierten Notifications; vier `INotificationHandler<T>` schreiben hinein. |
| `AnswerInputModel`, `AnswerChoice` | `Models/` | Eingabezustand und Auswahloption (`public`, weil `[Parameter]` der Komponente). |
| `AnswerInput` | `Components/AnswerInput.razor` | Eingabefeld je Fragetyp – von aktueller Frage und Editier-Modus geteilt. |
| Seite `DialogTestRunner.razor` | `Components/Pages/` | Die Seite (`/dialogs/{dialogId}/test`). |

Zwei Fallen, die beim Bau aufgeschlagen sind und beim Erweitern gelten:

- **Der Log muss in den Kind-Scope adoptiert werden.** Weil jeder Engine-Schritt in einem frischen Scope
  läuft, werden dort auch die Notification-Handler konstruiert. Ohne `DesignerTriggerLog.Adopt` (Muster
  von `ActiveConnectionProfile.Adopt`) schrieben sie in eine Wegwerf-Instanz, und das Panel bliebe
  dauerhaft leer.
- **Die Kodierung gehört an genau eine Stelle.** `AnswerValueCodec` ist verbindlich am
  Core-`AnswerValidator` ausgerichtet; `DesignerExpressionContext` leitet seine Beispielwerte davon ab,
  damit Ausdrucks-Validierung und Testlauf nicht auseinanderlaufen.

## Graph-Ansicht (#101)

Die Seite `/dialogs/{id}/graph` (`Components/Pages/DialogGraph.razor`) zeigt denselben Dialog als
**Graphen** statt als Formularstapel – verlinkt aus der Dialogliste und aus dem Kopf des Dialog-Editors.
Knoten sind verschiebbar (#102, § Layout-Persistenz), seit #103 ist der Canvas auch ein **Editor**
(§ Editieren auf dem Canvas), und der Test-Runner zeigt seinen Lauf seit #104 auf demselben Bild
(§ Testlauf im Graphen). Stufen 1–4 von **EPIC 11** (#99); Entscheidungen in
[ADR 0006](./adr/0006-canvas-technik-im-designer.md) (Canvas-Technik),
[ADR 0007](./adr/0007-layout-als-eigene-tabelle.md) (Layout als eigene Tabelle) und
[ADR 0008](./adr/0008-gesten-auf-dem-canvas.md) (Gesten auf dem Canvas).

Datenquelle ist der vorhandene `GetDialogQuery` über den `FlirtyAdminGateway`; geschrieben wird
ausschließlich über die bestehenden Admin-Commands – Positionen über
`Set`/`ResetDialogLayoutCommand`, Graph-Änderungen über dieselben `Create`/`Update`/`Delete`-Commands, die
auch die Listenansicht ruft. Es gibt kein Canvas-CRUD.

| Baustein | Ort | Aufgabe |
|---|---|---|
| `GraphLayout` | `Services/` | Auto-Layout („Sugiyama-Light"), rein geometrisch – und der Einbau gespeicherter Positionen. |
| `DialogGraphBuilder` | `Services/` | Fügt Graph, Warnungen, Schleifen und Trigger zum Zeichenmodell. |
| `TransitionWarningAnalyzer` | `Services/` | Die Übergangs-Warnungen – **dieselbe** Quelle wie die Liste. |
| `DialogGraphModel` | `Models/` | Knoten, Kanten, Rahmen, Marker, Auswahl. |
| `GraphMetrics`, `SvgFormat` | `Models/` | Maße bzw. kulturfeste Zahlformatierung. |
| `GraphNodeCard`, `GraphInspector` | `Components/` | Knoteninhalt bzw. Detailpanel. |
| `DialogGraph.razor.js` | `Components/Pages/` | Ansicht verschieben/zoomen und Knoten ziehen – clientseitig. |

### Was der Graph zeigt – und warum genau das

Nicht alles, was nach „Baustein" klingt, ist im Domänenmodell ein Knoten. Wer Schleifen und Trigger als
frei ziehbare Kacheln baut, erfindet ein zweites Modell neben der Domäne:

| Konzept | Entity | Auf dem Canvas |
|---|---|---|
| Frage | `Question` | **Knoten** – der einzige echte |
| Übergang | `Transition` | **Kante**, beschriftet mit Bedingung und Auswertungsposition |
| Schleife | `LoopDefinition` | **Bereichsrahmen** um den *berechneten* Body – kein eigener Knoten |
| Trigger | `TriggerDefinition` | **Chip** am Knoten bzw. an einem Scope-Marker |

Zwei Eigenschaften machen die Darstellung erst ehrlich:

- **Es gibt keine impliziten Kanten.** `TransitionResolver.ResolveTransitionTarget` liefert `null`, wenn
  eine Frage keine ausgehenden Übergänge hat – das ist der **reguläre Abschluss**, kein „weiter mit der
  nächsten Frage nach `Order`". Der Graph ist damit vollständig durch die `Transition`s beschrieben.
  Deshalb trägt eine Frage ohne ausgehende Kante das Badge *Abschluss* und eine doppelte Unterkante:
  Ohne Kennzeichnung liest sie sich wie eine fehlende Verbindung.
- **Der Loop-Body ist abgeleitet, nicht gespeichert.** Der Rahmen ist die Bounding-Box über
  `LoopAnalyzer`-Body – also vorhandene Logik, die den Core-internen `LoopResolver` spiegelt.

Ergänzend markiert die Ansicht die **Einstiegsfrage** und jede Frage, zu der von dort **kein Pfad**
führt. Fehlt die Einstiegsfrage ganz, ist Erreichbarkeit nicht bestimmbar – dann bleibt es bei *einer*
Warnung am Dialog, statt jeden Knoten rot zu färben.

### Warnungen hängen am verursachenden Element

`GraphWarning` (`Models/`) ist der gemeinsame Typ beider Sichten: derselbe Befund, zusätzlich einem
Element zugeordnet (`Question`, `Transition`, `Loop` oder `Dialog`). Die Regeln lagen bis #101 privat in
`DialogEditor.razor` und sind unverändert in den `TransitionWarningAnalyzer` gewandert; `LoopAnalyzer`
liefert seine Befunde ebenfalls verortet (`LoopInsight.TargetedWarnings`, mit `Warnings` als berechneter
Textsicht).

**Die Wortlaute sind Vertrag.** Der Dialog-Editor zeigt sie unverändert, die Publish-Rückfrage zählt sie,
die E2E-Suite sucht darin. `TransitionWarningAnalyzerTests` nagelt alle vier Volltexte fest.

Verortet wird nach Verursacher, nicht nach Fundort: „Kein Default-Übergang" und „Mehrere Defaults" sind
Eigenschaften der **Gruppe** und hängen an der Frage; „Bedingung wird nicht ausgewertet" und „greift
immer" hängen an **ihrem** Übergang. Beim verdeckten Schleifen-Ausstieg trägt der verdeckende Rücksprung
die Warnung – er ist die Kante, die zu ändern ist.

### Auto-Layout: deterministisch, sonst wertlos

`GraphLayout.Compute` schichtet per Breitensuche ab der Einstiegsfrage, nimmt Rückwärtskanten aus dem
azyklischen Satz und reduziert Kreuzungen per Baryzentrum. Gespeicherte Positionen gibt es hier noch
nicht – die bringt Stufe 2 (#102).

Derselbe Graph **muss** dieselben Koordinaten ergeben, sonst wackeln E2E-Selektoren. Drei Zusagen tragen
das, und alle drei sind Testfälle in `GraphLayoutTests`:

1. **Nur Listen nach außen**, nie eine Menge oder ein Wörterbuch – deren Iterationsreihenfolge ist nicht
   zugesichert.
2. **Sortierschlüssel enden mit einem eindeutigen Ordinal**, sind also eine Totalordnung und nicht auf
   die Stabilität von `OrderBy` angewiesen. Das Ordinal kommt aus `(Order, Id)` und **nicht** aus der
   Guid allein: `CreateDialogVersionCommand` vergibt beim Klonen jeder Frage eine neue Guid (ADR 0005),
   ein Guid-basiertes Layout würfelte bei jeder neuen Dialogversion neu durch.
3. **Koordinaten entstehen nur aus ganzzahligen Schicht- und Spaltenwerten**, nie aus einem
   Baryzentrum. Gleitkomma-Mittelwerte bestimmen die *Reihenfolge*, nicht die Position – sonst hingen die
   letzten Nachkommastellen an der Rechenreihenfolge und die Zusage gälte nur meistens.

**Ohne Dummy-Knoten.** Ein vollständiger Sugiyama zieht Platzhalterketten durch übersprungene Schichten.
Nötig wären sie hier nur für Rücksprünge – und die laufen ohnehin in einem Kanal rechts am Graphen
vorbei, nicht zwischen den Knoten. Bei der Zielgröße von rund 30 Knoten sparen sie kein einziges Kreuz,
kosten aber eine zweite Knotenart im Modell, im Rendering und in der Auswahl.

Mehrere Übergänge zwischen denselben zwei Fragen bleiben über drei unabhängige Merkmale unterscheidbar:
seitlicher Fächerversatz (trifft Ansatzpunkt *und* Kontrollpunkte), eigene Beschriftung am eigenen
Ankerpunkt, eigenes `aria-label`.

### Der Canvas selbst

Vier Zusagen aus ADR 0006 sind hier eingelöst – sie gelten für jede Erweiterung:

- **Verschieben, Zoomen und das Ziehen eines Knotens laufen im JS-Modul**, nicht in C#. Der Designer ist
  Blazor *Server*; jedes Blazor-Ereignis ist ein SignalR-Roundtrip. Zwischen `pointerdown` und
  `pointerup` geht **keine** Nachricht an den Server – `invokeMethodAsync` steht in
  `DialogGraph.razor.js` ausschließlich in `onPointerUp`. Wer dort einen Aufruf in `onPointerMove`
  ergänzt, bricht ein Akzeptanzkriterium von #102.
- **Das `transform` auf `.graph-viewport` gehört dem JS.** C# rendert es nie – sonst setzte der nächste
  Re-Render (etwa eine Auswahl) Verschiebung und Zoom zurück.
- **Kanten werden vor den Knoten gezeichnet.** Der breite, unsichtbare Trefferpfad, der die dünne Linie
  greifbar macht, läge sonst über der Knotenmitte und verschluckte den Klick (im Spike nachgemessen).
  Aus demselben Grund hat der Schleifen-Rahmen `fill="none"` und `pointer-events: stroke`: Er umschließt
  alles und würde sonst jeden Klick darin abfangen.
- **Der Canvas setzt `data-canvas-ready`**, sobald das Modul gebunden ist. Das ist das **erste**
  `data-`-Attribut im Designer und bewusst eine Ausnahme – siehe § Tests.

Zwei Punkte, die beim Erweitern zählen:

- **Zahlen in SVG nur über `SvgFormat.N`.** Der Designer läuft unter `de-DE`; eine interpolierte
  `double`-Koordinate schreibt `12,5`, und da das Komma in der Pfadsyntax ein *Trennzeichen* ist, wird
  daraus stillschweigend eine falsche Zahlenfolge – ohne Ausnahme, nur mit falschem Bild.
- **Das Modell wird einmal nach dem Laden in ein Feld gerechnet.** Aus einer Markup-Methode heraus
  aufgerufen (wie `GraphWarnings()` im Dialog-Editor) liefe die ganze Anordnung bei jedem Render erneut,
  also bei jedem Klick.

### Layout-Persistenz: Knoten verschieben (#102)

Ein Auto-Layout ordnet an, aber es ist nicht die Anordnung des Autors. Knoten sind deshalb verschiebbar,
und die Position liegt in der Tabelle **`DialogLayout`** am Dialog – nicht als zwei Spalten an
`Question` und nicht in einer Datei neben `connection-profiles.json`. Begründung samt verworfener
Alternativen: [ADR 0007](./adr/0007-layout-als-eigene-tabelle.md).

Der Ablauf einer Zieh-Geste:

1. `pointerdown` auf `.graph-node` merkt den Zug vor – **ohne** `preventDefault` und ohne
   Pointer-Capture. Bis zur Schwelle von **4 px** bleibt die Geste ein Klick, sonst verschluckte jeder
   leicht wackelige Klick die Auswahl.
2. Ab der Schwelle schreibt das Modul das `transform` des Knotens direkt und dimmt die anliegenden
   Kanten (`.graph-edge.is-stale`, gefunden über `data-from`/`data-to`). Die Pfade werden **nicht** im
   Browser neu gerechnet: Ihre Geometrie entsteht in `GraphLayout.Route` und ist dort getestet – eine
   zweite Quelle dafür wäre teurer als die Ungenauigkeit von einem Zug lang.
3. `pointerup` verschluckt das unmittelbar folgende `click` (sonst wählte jeder Zug den Knoten
   zusätzlich aus) und sendet **genau eine** Nachricht: `MoveNodeAsync(questionId, x, y)`.
4. C# schreibt `SetDialogLayoutCommand`, übernimmt das zurückgegebene Layout in das **gepufferte**
   `DialogDetail` und baut das Zeichenmodell daraus neu – kein zweiter `GetDialogQuery` je Geste. Jetzt
   stimmen Kanten und Schleifenrahmen wieder exakt.

Vier Dinge, die dabei zählen:

- **Bildschirm → Nutzerkoordinaten über `viewport.getScreenCTM().inverse()`.** Die Matrix enthält auch
  die Skalierung, die die `viewBox` gegenüber der CSS-Breite des SVG erzeugt. Wer nur durch den
  Zoomfaktor teilt, unterschlägt sie – der Knoten liefe dann je nach Fensterbreite schneller oder
  langsamer als der Zeiger.
- **Gespeicherte Positionen greifen an genau einer Stelle:** am Ende von `GraphLayout.Render`, wo die
  Knotenboxen entstehen. Schichtung, Kantenform, Baryzentrum und Kanalvergabe bleiben am Auto-Layout.
  Ein Zug ändert damit nur die Position eines Knotens – nie die Zeichenform einer Kante und nie die
  Anordnung der übrigen. Die Zeichenfläche wächst mit, sonst ragte ein weit gezogener Knoten aus der
  `viewBox`.
- **Der Commit rendert denselben Wert, den das JS geschrieben hat.** Das Modul rundet auf ganze Pixel und
  `SvgFormat.N` formatiert ganze Zahlen ohne Nachkomma – deshalb springt der Knoten beim Re-Render nicht.
- **Verschieben funktioniert auch bei veröffentlichtem Dialog.** Die Layout-Commands laufen bewusst nicht
  unter `DialogEditGuard`; Koordinaten berühren die Session-Semantik nicht. Die Publish-Sperre der
  Graph-Editoren (§ Versionierung) bleibt unverändert.

„Layout zurücksetzen" in der Werkzeugleiste verwirft alle Zeilen des Dialogs
(`ResetDialogLayoutCommand`) – danach greift wieder das Auto-Layout. Der Knopf erscheint nur, wenn
überhaupt Positionen gespeichert sind, und fragt wie jede destruktive Aktion im Designer inline zurück.
Ein Knoten mit eigener Position trägt einen Balken an der rechten Kante (`.is-pinned`; die
Schleifen-Zugehörigkeit markiert die linke) und im `aria-label` den Zusatz „eigene Position".

### Editieren auf dem Canvas (#103)

Seit Stufe 3 ist der Canvas ein Editor. Die tragende Regel: **jede Geste ruft denselben Admin-Command,
den auch die Listenansicht ruft.** Es gibt kein Canvas-CRUD, keinen neuen Core-Command und keine
Schema-Änderung – Begründung und verworfene Alternativen in
[ADR 0008](./adr/0008-gesten-auf-dem-canvas.md).

| Geste | Einstieg | Commands |
|---|---|---|
| Baustein aus der Palette **ziehen** | `onPaletteUp` → `CreateQuestionAtAsync` | `CreateQuestionCommand` + `SetDialogLayoutCommand` |
| Palette-Eintrag **betätigen** | `@onclick` → `AddQuestionAsync` | `CreateQuestionCommand` (ohne Position – das Auto-Layout ordnet ein) |
| Vom **Port** auf einen Knoten ziehen | `endLink` → `ConnectAsync` | `CreateTransitionCommand` |
| Vom Port **ins Leere** ziehen | `endLink` → `ConnectToNewQuestionAsync` | `CreateQuestionCommand` + `SetDialogLayoutCommand` + `CreateTransitionCommand` |
| Port betätigen, dann Knoten wählen | `StartLink` + `SelectQuestion` | `CreateTransitionCommand` (der zeigerlose Weg) |
| Kopffelder einer Frage speichern | Inspector-Panel | `UpdateQuestionCommand` |
| ↑/↓ an den ausgehenden Kanten | Inspector-Panel | mehrere `UpdateTransitionCommand` |
| „Default" umschalten | Inspector-Panel | `UpdateTransitionCommand` |
| Ziel/Bedingung eines Übergangs | Inspector-Panel | `UpdateTransitionCommand` |
| „Als Schleife markieren" am Rücksprung | Inspector-Panel | `CreateLoopCommand` |
| Trigger anlegen (Frage bzw. Dialog) | Inspector-Panel | `CreateTriggerCommand` |
| Löschen (Frage, Übergang, Marker) | Inspector-Panel, zweistufig | `Delete*Command` |

Die Rechenregeln dahinter lagen bis #103 privat im `@code`-Block von `DialogEditor.razor` und waren damit
durch keinen Test gedeckt. Sie liegen jetzt in `Services/GraphEditing.cs` (`NextOrder`, `NextPriority` je
Ausgangsfrage, `Reorder`) und `Services/LoopAnalyzer.cs` (`IsBackJump`, `UnmarkedBackJumps`) – und werden
von **beiden** Ansichten benutzt. Neu dazu kam `QuestionFormModel.SuggestKey`: Anders als
`LoopFormModel.SuggestCollectionKey`, das bei einer Kollision bewusst leer liefert, darf hier nie leer
herauskommen – der Vorschlag trägt eine Geste, die sofort schreibt.

#### Nach einer Mutation wird neu geladen

`MoveNodeAsync` rechnet die neue Position lokal ein (ADR 0007: der Layout-Command gibt das
**vollständige** Layout zurück). Jede **Graph**-Änderung lädt dagegen neu. Der Grund sind nicht die
Entitäten, sondern die **Warnungen**: `TransitionWarningAnalyzer` und `LoopAnalyzer` rechnen über den
ganzen `DialogDetail`, ein neuer Übergang kann eine Warnung an einer *anderen* Frage aufheben. Dazu räumt
`DeleteQuestionCommand` Übergänge, Marker, Trigger und Layout-Zeilen mit ab – diese Kaskade lokal
nachzubauen wäre die zweite Wahrheit, die das Issue verbietet. Die gelöschten Bestände werden als
Differenz vor/nach dem Reload gezählt und gemeldet („… – 2 Übergänge, 1 Trigger mit entfernt"), und
`ReconcileSelection()` verwirft eine Auswahl, deren Element es nicht mehr gibt.

#### Gesten sind nicht idempotent

Ein doppelter Drop legte zwei Fragen an. Deshalb zwei Riegel, und beide sind nötig:

- **Client:** Jede Nachricht läuft über `send()` im JS-Modul, das bis zur Rückkehr der .NET-Methode
  sperrt. **Das Versprechen von `invokeMethodAsync` ist die Quittung** – Blazor Server erfüllt es, wenn
  der Aufruf durch ist. Ein hängender Circuit lässt den Canvas gesperrt; das ist die richtige Reihenfolge
  der Übel (gesperrt statt doppelt angelegt), und den echten Abriss behandelt das Reconnect-Modal.
- **Server:** `RunGestureAsync` steigt bei `_busy` früh aus. Der Client-Riegel ist umgehbar, das
  Server-Gate ist die Invariante. Umgekehrt allein genommen verschluckte es die zweite *berechtigte*
  Geste eines schnellen Anwenders stillschweigend. `MoveNodeAsync` hängt seit #103 ebenfalls am Gate.

Ein Restfenster bleibt: Das Versprechen löst auf, bevor der Render-Batch angewandt ist. Ein Klick in
diesem Sub-Frame-Fenster arbeitet auf altem DOM – vom Server-Gate abgefangen.

#### Lesemodus statt Konfliktmeldung

Bei veröffentlichtem Dialog werden Ports **gar nicht gerendert**, die Palette ist `disabled`, und der
Hinweis bietet „Neue Version anlegen" an (→ Graph der neuen Version, nicht in die Liste: wer hier
arbeitet, will hier weiterarbeiten). Das JS-Modul erfährt den Zustand über `data-editable` am `<svg>` –
ein Attribut, das **C# besitzt und das Modul nur liest**, bei jeder Geste frisch. Eine `attach`-Option
wäre eingefroren. Das ist die Kehrseite der ADR-0006-Regel „was das JS setzt, rendert C# nie", nicht ihr
Bruch. Verschieben bleibt erlaubt und läuft nicht ins 409.

#### Port, Gummiband und Vorschau

Der Ausgangs-Port ist ein **Geschwister** der Knotenkarte, kein Kind: `<button>` in `<button>` ist
ungültiges HTML, und der äußere verschluckte Klick und Fokus. Er sitzt an der Unterkante-Mitte – genau
dort, wo `GraphLayout.Route` eine Vorwärtskante ansetzt; die Affordanz lügt also nicht über die
Geometrie. Im `pointerdown` wird `.graph-port` **vor** `.graph-node` geprüft, sonst verschluckt der
Verschiebe-Zug die Verbindungsgeste – und wie überall ohne `preventDefault`, damit der Klick (der
zeigerlose Weg) trägt.

Gummiband und Drop-Vorschau sind von C# gerenderte, leere Platzhalter (`.graph-rubber`, `.graph-ghost`);
das Modul setzt nur ihre Geometrie und leert sie wieder. Per `createElement` erzeugtes DOM in einem von
Blazor verwalteten Container brächte den Renderer beim nächsten Diff über die Kindindizes aus dem Tritt.
Beide brauchen `pointer-events: none` – der Ziel-Hit-Test läuft über `document.elementFromPoint` (nach
`setPointerCapture` ist `event.target` das Capture-Element), und ohne die Regel träfe er das Gummiband.

**Der Riegel gegen den Folge-Klick sitzt am jeweiligen Element**, nicht immer am Canvas: Nach einem
Palette-Zug feuert der `click` am Palette-Eintrag. Horchte `swallowNextClick` dort auf dem Canvas, legte
jeder Zug zusätzlich die Klick-Frage an – zwei Fragen aus einer Geste.

**Der leere Dialog rendert jetzt eine Zeichenfläche.** Bis #103 ersetzte ein Hinweis den Canvas, solange
es keine Fragen gab – auf eine nicht vorhandene Fläche lässt sich nichts ziehen. Der Hinweis steht
darüber, und `GraphMetrics.MinCanvasWidth`/`MinCanvasHeight` geben eine benutzbare Untergrenze (vorher
80 × 80 px). Damit entfiel auch das Flag `_canvasAttached`: Wahrheit ist `_module`, und bei einem
Dialogwechsel wird sauber `detach`t.

#### Der Ausdrucks-Editor ist eine Komponente

Eingabefeld, Live-Status, Fehlerstelle, Baustein-Einfüger und Bezeichner-Referenz standen im
`TransitionEditor` und im `TriggerEditor` Zeile für Zeile gleich – bis auf einen Satz. Sie liegen jetzt in
`Components/ExpressionField.razor` (`@bind-Value`, `EmptyHint`/`EmptyMeaning`, `ShowBuilder`), das auch
das Inspector-Panel nutzt. Die **blockierende** Prüfung vor dem Speichern bleibt beim Aufrufer: Die
Komponente zeigt an, der Aufrufer entscheidet. Der Parametersatz besteht ausschließlich aus öffentlichen
Typen (`DialogDetail`) – `ExpressionVariable` bleibt `internal`, weil es nicht in der Parameterliste
steht.

**Nebenbefund:** `.expr-status` und `.expr-caret` lagen scoped in `TransitionEditor.razor.css`, wurden
aber vom `TriggerEditor` seit #42 mitbenutzt – dort war der Live-Status also **unstyled**. Beide Regeln
liegen jetzt global in `wwwroot/app.css`.

#### Die Inspector-Panels arbeiten ohne `EditForm`

`GraphQuestionPanel` und `GraphTransitionPanel` binden rohe `<input>`/`<select>` mit `@oninput` statt
`InputText`/`InputSelect` in einer `EditForm`. Das ist keine Stilfrage, sondern ein am laufenden Panel
gemessener Befund:

- **`onchange` verliert Eingaben.** Das voreingestellte Binding liefert den Wert erst beim Verlassen des
  Felds. Das Panel wird aber nach *jeder* Geste neu aufgebaut (der Reload ersetzt `Detail`) – beim
  Speichern stand dann stillschweigend der alte Wert im Command.
- **Der Submit einer `EditForm` kam im Panel nicht an.** Sie setzt einen stabilen Formular-Lebenszyklus
  voraus, den ein Panel in einem `@if`-Zweig über wechselnden Auswahlen nicht hat.

Die Pflichtprüfung übernimmt damit `SaveAsync` im Panel – wie die Querfeld-Regeln des Trigger-Formulars,
die ebenfalls vor dem Command laufen. Der Command prüft ohnehin erneut. Ein `@key` an beiden Panels bindet
die Instanz an das bearbeitete Element, damit ein begonnener Entwurf jeden Re-Render derselben Auswahl
überlebt und beim Wechsel bewusst verworfen wird.

### Testlauf im Graphen (#104)

Stufe 4 macht aus der Verlaufsliste des Runners ein Bild: Der Test-Runner (§ Test-Runner) hat unter
`/dialogs/{id}/test` **zwei Ansichten desselben Laufs** – „Verlauf" (die Liste, unverändert) und „Graph"
(der Canvas mit dem gelaufenen Pfad). Der Umschalter steht über der Karte „Aktuelle Frage"; ein Deep-Link
`?view=graph` öffnet direkt die Graph-Ansicht (so verlinkt der Graph-Editor „Durchspielen").

**Es ist kein zweiter Runner.** Start, Antwort, Editieren und `ResumeDialogAsync` liegen unverändert in
`DialogTestRunner.razor`; die Karte „Aktuelle Frage/Ergebnis" steht **außerhalb** des Umschalters und wird
in beiden Ansichten gerendert. Damit ist „der listenbasierte Runner bleibt gleichwertig bedienbar"
strukturell wahr statt zugesagt – es gibt nur eine Choreografie, und ein Wechsel der Ansicht berührt den
Lauf nicht (auch eine begonnene Bearbeitung bleibt dieselbe). Der Hinweis **„Der Lauf ist echt"** steht
ebenfalls über dem Umschalter: Die grafische Aufbereitung darf nicht harmloser aussehen, als der Lauf ist.

| Baustein | Ort | Aufgabe |
|---|---|---|
| `GraphRunAnalyzer` | `Services/` | Leitet den Laufzustand aus `DialogDetail` + `ResumeDialogResult` + Trigger-Protokoll ab. |
| `GraphRunOverlay` & Co. | `Models/GraphRunModel.cs` | Besuche, gegriffene Kanten, Schleifen-Zustand, Ereignisse. |
| `GraphRunCanvas` | `Components/` | Der Canvas der Laufansicht (bindet auch das JS-Modul). |
| `GraphRunInspector` | `Components/` | Antworten je Iteration, Bindungen und Ereignisse **am gewählten Knoten**. |

#### Der Pfad ist abgeleitet, nicht protokolliert

Die Engine hält **nicht** fest, welcher Übergang gegriffen hat: `SessionAnswer` trägt keine
`TransitionId`, und `QuestionAnsweredNotification` nennt nur die nächste *Frage*. Der Pfad entsteht deshalb
aus der Antwortfolge – zwei aufeinanderfolgende Antworten bilden das Paar *(von, nach)*, die letzte Antwort
zusammen mit der offenen Frage das letzte Paar.

Daraus folgt eine Grenze, die die Oberfläche benennt statt sie zu verdecken: **Liegen zwischen denselben
zwei Fragen mehrere Übergänge, ist nicht entscheidbar, welcher gegriffen hat.** Dann sind alle markiert
(gestrichelt statt durchgezogen), der Inspector sagt „mehrdeutig", und das `aria-label` nennt den Grund.
Die Auswertung nachzustellen wäre nicht nur eine weitere Spiegelung des Core-`TransitionResolver`, sondern
eine unmögliche: Sie bräuchte die Ausdruckswerte von *damals*.

Der angenehme Nebeneffekt: **Ein Edit rechnet den Pfad ohne eigenen Code neu.** `EditAnswerCommand`
verwirft die nachgelagerten Antworten, die Ableitung schrumpft mit – auch wenn der neue Pfad einen anderen
Zweig nimmt.

#### Was am Knoten steht

- **Besucht** heißt: in diesem Lauf beantwortet. Die Karte zeigt dann statt der Konfigurationszeile
  (Typ/Pflicht/Optionen) den **Wert der letzten Antwort**; der Typ steht im Inspector. Grund ist die feste
  Kartenhöhe – sie schneidet Überlauf ab, beides nebeneinander verlöre eine der beiden Angaben.
- **Offen** trägt das Badge „▶ offen". Ein Knoten kann offen *und* besucht sein: In einer Schleife wird
  dieselbe Frage erneut gestellt.
- **Iteration n** kommt aus dem Iterationsindex der letzten Antwort. Ein Zyklus **ohne** Schleifen-Marker
  erzeugt keinen Index – dort steht „n× beantwortet", weil „Iteration" dort schlicht falsch wäre.
- **Publizierte Trigger** hängen als `⚡`-Chip am auslösenden Knoten (Quelle: `DesignerTriggerLog`, wie im
  Protokoll der Liste) und **blitzen einmal auf**. Gebündelt wird je Zeitpunkt („⚡ Antwort 2×"): Eine
  Schleifenfrage sammelt zwei Ereignisse *pro* Iteration, ungebündelt sprengt die Chip-Reihe die Karte.
  Die Einzelereignisse samt Zeit stehen im Inspector. Ereignisse ohne Frage-Bezug (Abschluss) – und solche
  zu einer inzwischen gelöschten Frage – zeigt der Inspector dialogweit.
- Das Aufblitzen läuft über `@keyframes` beim **Entstehen** des Chip-Elements; deshalb tragen die Chips
  bewusst **kein** `@key` (ein neues Ereignis soll ein neues Element sein), und `prefers-reduced-motion`
  bekommt dieselbe Aussage statisch.

#### Iterationszahl und Bindungen

Der Schleifenrahmen trägt die Zahl der Iterationen der **jüngsten** Schleifen-Instanz – dieselbe Auswahl,
die der Core-`LoopResolver` für die Collection trifft – und wird durchgezogen gezeichnet, solange die
offene Frage in seinem Bereich liegt.

Die Ausdrucks-Bindungen (`RunExpressionContext`, § Ausdruckskontext) bleiben einsehbar, aber **am
gewählten Knoten** statt nur global: seine eigene Antwort, die Collection jeder umschließenden Schleife –
und `iterationIndex` **nur an der offenen Frage**. Er meint den Index der zuletzt gegebenen Antwort auf
*genau diese* Frage; an einem anderen Knoten gezeigt behauptete er etwas Falsches. Für das Editieren je
Iteration listet der Inspector die Antworten des Knotens mit ihrem Badge und je einer Schaltfläche
„Bearbeiten" – dieselbe `EditAnswerAsync`-Operation wie in der Liste, inklusive Iterationsindex.

#### Der Graph ist hier nicht bearbeitbar – Verschieben schon

Eine laufende Session arbeitet auf genau diesem Graphen; ihn unter ihr wegzuändern ist die Falle, die #95
teuer gemacht hat. Die Laufansicht rendert deshalb keine Palette und keine Ports und setzt
`data-editable="false"` am `<svg>`. **Verschieben bleibt erlaubt** (`SetDialogLayoutCommand`, guard-frei,
ADR 0007) und ist der einzige schreibende Weg dieser Ansicht.

Anders als die Editor-Seite bindet hier die **Komponente** `GraphRunCanvas` das JS-Modul (dasselbe
`DialogGraph.razor.js`) und reicht den beendeten Zug als `NodeMove` nach oben: Der Canvas gehört ihr, und
der Rückkanal wäre sonst ein `ElementReference`, den die Seite durchreichen müsste. Ohne Palette im DOM und
ohne gerenderte Ports laufen im Modul nur Verschieben, Zoomen und Panning an – `MoveNodeAsync` ist damit
die einzige Nachricht, die von dort kommt.

### Inspector und Barrierefreiheit

Der Inspector war in Stufe 1 eine reine Lesesicht mit Sprung; seit #103 bearbeitet er das gewählte
Element. Eingebettet werden dabei **nicht** die `@page`-Editoren – `QuestionEditor` & Co. haben eigenen
`PageTitle`, eigene Überschrift und eigenen Rücklink –, sondern eigene Panels
(`GraphQuestionPanel`, `GraphTransitionPanel`), die dieselben Commands rufen. Die Grenze verläuft entlang
der **Datenform**: skalare Felder im Panel, alles mit eigener Unterstruktur oder Roh-JSON-Fallback
(Antwortoptionen, Validierungsregeln, Trigger-Bedingung) im Vollteditor, den „… bearbeiten →" öffnet.

Die Formularmodelle bleiben dabei `internal` und **privater Zustand** des Panels; über die
Komponentengrenze geht nur das Ergebnis (`Models/GraphEdits.cs`: `QuestionEdit`, `TransitionEdit`,
`TransitionMove`, `LoopDraft`, `TriggerDraft`). Grund ist CS0053 – Razor erzeugt Komponenten als
`public`, ein `internal` Typ an einem `[Parameter]` bricht unter `TreatWarningsAsErrors` den Build.
Nebeneffekt ist die klarere Zuständigkeit: **Panel = Formular, Seite = Commands**, und damit genau eine
Stelle für Gesten-Riegel und Fehlerpfad.

Der Inspector ist zugleich der **Tastaturpfad zu allem, was auf der Fläche eine Zeiger-Geste ist**:
Verbinden über eine Auswahlliste, und die Auswertungsreihenfolge wird ohnehin nur hier gepflegt – eine
Position auf dem Canvas darf keine Semantik tragen, sonst ändert Aufräumen das Verhalten.

Ein reiner Canvas wäre gegenüber den Formularen ein Rückschritt. Deshalb:

- Knoten sind echte `<button>` in einem `<foreignObject>`. Damit kommen Fokusring, Enter/Leertaste und
  Screenreader-Rolle von der Plattform statt aus Handarbeit. Blazor trägt das: Seine
  Namensraum-Prüfung schließt `foreignObject` ausdrücklich aus, Kindelemente entstehen im HTML-Namensraum.
- **Die Tab-Reihenfolge ist der Ablauf** – Knoten werden nach Schicht und Spalte gerendert. Das ist eine
  Zusage an das Rendering, kein Zufall.
- Kanten sind **nicht** fokussierbar (45 Tabstopps wären eine Wüste), aber vorlesbar und über die
  Übergangslisten des Inspectors vollständig per Tastatur erreichbar.
- Jeder Knoten trägt ein vollständiges `aria-label`; vor dem Canvas steht eine versteckte
  Zusammenfassung („3 Fragen, 3 Übergänge, 1 Schleife, keine Warnungen").
- Das `<svg>` hat `role="group"`, **nicht** `role="application"` – letzteres kapert die
  Screenreader-Navigation.
- Kontrast ≥ 4,5:1 auch für Striche (die WCAG verlangt dort nur 3:1; der Befund aus #95 sitzt tief), und
  **nie Farbe allein**: Einstieg, Abschluss und Unerreichbarkeit tragen zusätzlich ein Badge und eine
  eigene Konturform.

Der Listen- und Formularpfad bleibt vollständig erhalten – der Canvas ist zusätzlich, nicht Ersatz.

## Konventionen

- Blazor-Komponenten unter `Components/` (Seiten in `Components/Pages/`), Server-interaktiver Render-Mode
  (`@rendermode InteractiveServer` auf interaktiven Seiten).
- Gemeinsame UI-Primitiven (`.editor`, `.field`, `.input`, `.btn`, `.data-table`, `.badge`, `.msg`,
  `.banner`, `.empty`, `.back`, `.confirm`, `h1 .badge` …) liegen **global** in
  `wwwroot/app.css`; die `*.razor.css`-Dateien enthalten nur
  noch Seitenspezifisches. Neue Editor-Seiten nutzen diese Klassen, statt sie zu duplizieren.
- UI-Texte und Doku **deutsch**. Der Designer ist `IsPackable=false` → CS1591 ist hier **kein** Fehler,
  XML-Docs sind optional (die übrigen Warnungen bleiben aber via `TreatWarningsAsErrors` Fehler).
- **Anzeige-Kultur fest `de-DE`** (`DesignerApp.DisplayCulture`, gesetzt als
  `CultureInfo.DefaultThreadCurrentCulture`). Ohne diese Festlegung folgte die Formatierung der Kultur
  des Hosts – auf einem englischen System stand „7/27/2026 10:38 AM" mitten im deutschen Text. Bewusst
  über die Prozess-Kultur statt `RequestLocalization`: In Blazor Server rendert der Circuit, nicht ein
  HTTP-Request. Antwort**werte** bleiben davon unberührt – die kodiert `AnswerValueCodec` invariant.
- Alle Regeln der `*.razor.css`-Dateien gelten nur für die HTML-Elemente **der eigenen** Komponente:
  CSS-Isolation vergibt ihr Scope-Attribut nicht an Kind-Komponenten. Styles für gerenderte Komponenten
  (`<NavLink>` &c.) gehören deshalb global nach `wwwroot/app.css` – siehe den Kommentar in
  `NavMenu.razor.css`, wo genau das die Navigationslinks unlesbar gemacht hatte.
- **Zahlen in SVG-Attributen ausschließlich über `SvgFormat.N`.** Die feste Anzeige-Kultur `de-DE` gilt
  auch beim Rendern: Eine interpolierte `double`-Koordinate wird zu `12,5`, und weil das Komma in der
  SVG-Pfadsyntax ein *Trennzeichen* ist, entsteht daraus eine falsche Zahlenfolge – ohne Ausnahme, ohne
  Meldung, nur mit falschem Bild. Betrifft `d`, `transform`, `viewBox`, `x`/`y`, `width`/`height`.
- **Was clientseitig läuft, bleibt clientseitig.** Zieh- und Zoomgesten gehören in ein collocated
  `*.razor.js`-Modul (Muster: `ReconnectModal.razor.js`, `DialogGraph.razor.js`); zwischen `pointerdown`
  und `pointerup` geht keine Nachricht an den Server (ADR 0006). Attribute, die dieses Modul setzt
  (`transform` auf `.graph-viewport`), darf C# **nie** rendern – der nächste Re-Render setzte sie sonst
  zurück.
- Zeitstempel UTC.

## Tests

Die Service-Logik wird per **xUnit** in `tests/Flirty.Tests` geprüft (das Testprojekt referenziert den
Designer; Interna via `InternalsVisibleTo("Flirty.Tests")`):

- `Persistence/FlirtyDatabaseProviderExtensionsTests` – Core-Mapping Provider → EF-Provider + MigrationsAssembly.
- `Designer/JsonConnectionProfileStoreTests` – CRUD, Kopier-Semantik und Persistenz der Profile.
- `Designer/ConnectionProfileOperationsTests` – Test-Connection und Migrate gegen eine SQLite-Temp-DB.
- `Designer/FlirtyAdminGatewayTests` – Admin-CRUD über den echten DI-Stack gegen eine SQLite-Temp-DB:
  Anlegen/Auflisten, Fehler-Mapping (Schlüsselkonflikt, unbekannter Dialog, fehlendes Profil, nicht
  migrierte Datenbank), – als Regression – dass ein **Profilwechsel sofort greift**, die Fragen-
  Flüsse aus #39 (Frage mit Optionen anlegen, Reihenfolge in *einer* Operation tauschen, Rücksetzen der
  Einstiegsfrage beim Löschen), die Übergangs-Flüsse aus #40 (anlegen/löschen, Prioritäten in *einer*
  Operation neu vergeben) und die Schleifen-Flüsse aus #41 (anlegen/ändern/löschen, Konflikt bei doppeltem
  `CollectionKey`, Mitentfernen des Markers beim Löschen einer Frage).
- `Designer/LoopAnalyzerTests` – die Schleifen-Analyse (#41): Bereichsermittlung inklusive Ein-Fragen-Loop,
  Einteilung in Rücksprünge/Ausstiege, jede Warnregel einzeln – und als Kernprobe der Abgleich mit dem
  Core-`LoopResolver` auf demselben Graphen (kein Auseinanderlaufen der gespiegelten Berechnung).
- `Designer/DesignerExpressionContextTests` – der Musterkontext der Ausdrucks-Validierung (#40), geprüft
  gegen die **echte** Engine: gültige Ausdrücke je Fragetyp, Loop-Collection ohne Iteration, Tippfehler
  mit Position, verdeckte/ungültige Schlüssel und die typgerechte Quotierung des Baustein-Einfügers.
- `Designer/QuestionFormModelTests` – die Abbildung zwischen Eingabefeldern und Regel-JSON (#39):
  typ-skopiertes Serialisieren, camelCase ohne Nullwerte, Roh-JSON-Fallback bei unbekannten Feldern,
  abgelehnte Muster/Grenzen und – als Kernprobe – dass der `AnswerValidator` der Engine das erzeugte
  JSON tatsächlich anwendet.
- `Designer/TriggerFormModelTests` – die Abbildung zwischen Eingabefeldern und `Config`-JSON (#42):
  Lesen/Schreiben über den Core-Typ, Roh-JSON-Fallback samt Erhalt fremder Felder, die kanal-abhängige
  URL-Prüfung und die Normalisierung von Frage-Bezug und Ausdruck.
- `Designer/FlirtyRuntimeGatewayTests` – der Test-Runner (#43). Kernprobe ist das **Akzeptanzkriterium
  in Testform**: einen Dialog samt Schleife über die Admin-Commands anlegen und **ohne Veröffentlichung**
  mit zwei Iterationen durchspielen (inkl. der erwarteten `IterationIndex`-Werte und Loop-Instanz). Dazu
  das gezielte Editieren einer Iteration und das Fehler-Mapping (ungültige Antwort ohne rohe GUID,
  unbekannte Session/Dialogversion, fehlendes Profil).
- `Designer/AnswerValueCodecTests` – die Kodierung der Antwortwerte (#43), geprüft gegen den **echten**
  `AnswerValidator`: die JSON-Form je Fragetyp, invariante Zahlliterale trotz Dezimalkomma, das
  Weiterreichen unlesbarer Eingaben an die Engine, die Anzeige (Beschriftung statt Rohwert) und die
  Umkehrbarkeit von `Decode`/`Encode` für den Editier-Modus.
- `Designer/RunExpressionContextTests` – die Live-Bindungen des Laufs (#43), als Kernprobe an **jedem**
  Schritt eines echten Durchlaufs gegen den Core-`SessionExpressionContextBuilder` abgeglichen (kein
  Auseinanderlaufen der gespiegelten Berechnung), dazu die gesammelte Collection und die Semantik des
  `iterationIndex`.
- `Designer/DesignerTriggerLogTests` – das Trigger-Protokoll (#43): dass die Notifications trotz frischem
  Scope je Schritt im adoptierten Log des Circuits landen, Reihenfolge/Scope-Zuordnung, `Clear()` und
  dass Admin-Operationen nichts protokollieren.
- `Designer/TransitionWarningAnalyzerTests` – die Übergangs-Warnungen (#101), die bis dahin privat im
  `DialogEditor` lagen: jede Regel einzeln, die Verortung am Knoten bzw. an der Kante – und als
  Kernprobe, dass alle vier **Wortlaute unverändert** sind. Listenansicht, Publish-Rückfrage und
  E2E-Suite hängen daran.
- `Designer/GraphLayoutTests` – das Auto-Layout (#101). Kern ist der **Determinismus**, geprüft gegen
  die drei Quellen, aus denen er üblicherweise wegbricht: Hash-Iterationsreihenfolge (zweimal rechnen),
  neu vergebene Guids (denselben Graphen zweimal bauen – der Test, der `CreateDialogVersionCommand`
  überlebt) und die globale Reihenfolge der Übergänge. Dazu Schichtung, aufgebrochene Rückwärtskanten,
  unerreichbare Komponenten, Überlappungsfreiheit, aufgefächerte Mehrfachkanten, Kreuzungsreduktion und
  das Zahlformat unter `de-DE`. Für #102 kommen die gespeicherten Positionen dazu: Sie überschreiben die
  berechnete Position ohne die Schicht zu verändern, die Kanten folgen mit, die Zeichenfläche wächst um
  einen weit gezogenen Knoten, eine Zeile ohne Frage wird übergangen – und ohne Zeile ist das Ergebnis
  identisch zum reinen Auto-Layout (der Nachweis, dass „Layout zurücksetzen" wirklich zurücksetzt).
- `Designer/DialogGraphBuilderTests` – das Zeichenmodell (#101): Marker für Einstieg, Abschluss und
  Unerreichbarkeit, Warnungen am verursachenden Element, Loop-Rahmen über dem `LoopAnalyzer`-Body,
  Trigger an Frage bzw. Scope-Marker, getrennt ausgewiesene verwaiste Übergänge und die
  `aria-label`-Beschreibung jedes Knotens; dazu (#102) der Schleifen-Rahmen über einer verschobenen
  Frage.
- `Designer/GraphRunAnalyzerTests` – der Laufzustand über dem Graphen (#104), gespielt mit der **echten
  Engine**: besuchte Knoten, offene Frage und gegriffene Kanten (samt der Gegenprobe, dass Rücksprung und
  Ausstieg unmarkiert bleiben), die Iterationszahl der Schleife und ihr Verlassen, parallele Übergänge als
  **mehrdeutig** – und als Kernprobe des Akzeptanzkriteriums, dass ein `EditAnswerCommand` den Pfad neu
  rechnet und dabei den Zweig wechselt. Dazu die Trigger-Zuordnung (Knoten, dialogweit, `freshFrom`) ohne
  Engine, weil sie am Protokoll hängt.
- `Designer/DesignerTestHost` – kein Test, sondern der gemeinsame DI-Stack (Spiegel von `DesignerApp`)
  und die SQLite-Temp-Datenbank für die Gateway-Tests. Ändert sich `DesignerApp.ConfigureServices`, ist
  das die eine Stelle, die nachzuziehen ist.

Dazu kommen im Core die Gegenstücke: `Domain/TriggerConfigTests` (das Schema selbst) und
`Runtime/DialogTriggerDispatchTests` – der End-to-End-Nachweis, dass ein im Designer konfigurierter
Webhook-Trigger beim Durchlaufen eines Dialogs tatsächlich zugestellt wird (echte Engine, echte SQLite-DB,
HTTP-Spy).

```pwsh
dotnet test tests/Flirty.Tests
```

### Playwright-E2E der UI (#46)

Die Oberfläche selbst wird in `tests/Flirty.E2E` im **Browser** geprüft – dieselbe Mechanik wie bei der
Chat-UI der Web-Sample (#45/#47):

- `DesignerAppFixture` hostet `DesignerApp` in-Prozess auf einem freien Kestrel-Port und legt vorab ein
  **aktives** Connection-Profil auf eine frisch migrierte SQLite-Temp-Datenbank an (Profil-Datei und DB
  liegen in einem Temp-ContentRoot, nicht im Repo).
- `DesignerE2ETests.Dialog_mit_Branching_und_Schleife_anlegen_und_speichern` – das Akzeptanzkriterium
  des Issues: Dialog anlegen → drei Fragen → Antwortoptionen im Frage-Editor → Einstiegsfrage → drei
  Übergänge → Bedingung `more == "yes"` inklusive **Live-Validierung** → Schleife über den
  Rücksprung-Vorschlag markieren → veröffentlichen. Ein abschließendes **Neuladen** rendert alles aus
  der Datenbank neu und belegt so die Persistenz.
- `DesignerE2ETests.Testlauf_spielt_die_Schleife_mit_der_echten_Engine_durch` – der Test-Runner (#43)
  auf demselben (unveröffentlichten) Dialog: zwei Iterationen, Ausstieg, Abschluss; geprüft werden das
  `Iteration 2`-Badge des Verlaufs und die gesammelte Collection im Ausdruckskontext.
- `DesignerE2ETests.Graph_Ansicht_zeigt_den_Ablauf_und_fuehrt_in_den_Frage_Editor` – die Rauchprobe der
  Graph-Ansicht (#101): Der Canvas bindet sein JS-Modul, zeichnet drei Knoten und drei Kanten, markiert
  Einstieg und Abschluss, rahmt die Schleife und hängt den Trigger-Chip an genau die Frage, nach der er
  feuert; die Auswahl öffnet den Inspector und führt in den bestehenden Frage-Editor. Die vollständige
  Canvas-Abdeckung folgt in Stufe 5 (#105).
- `DesignerE2ETests.Graph_Knoten_verschieben_ueberlebt_den_Reload` – die Rauchprobe der
  Layout-Persistenz (#102) am **veröffentlichten** Dialog: Knoten ziehen, Reload (der Server rendert die
  Position aus der Datenbank), „Layout zurücksetzen" – danach liegt der Knoten wieder auf seiner
  Auto-Layout-Position. Dass der Dialog veröffentlicht ist, belegt zugleich die Guard-Ausnahme aus
  ADR 0007: Es erscheint keine Fehlermeldung.
- `DesignerE2ETests.Graph_Palette_und_Port_legen_Fragen_und_Uebergang_an`,
  `…Graph_Inspector_bearbeitet_Frage_Uebergang_und_loescht_mit_Kaskade` und
  `…Graph_Gesten_sind_bei_veroeffentlichtem_Dialog_deaktiviert` – die Gesten und der Lesemodus aus #103
  (siehe den letzten Punkt unten: was davon im Browser belegt ist und was #105 bleibt).
- `DesignerE2ETests.Testlauf_im_Graphen_hebt_den_gelaufenen_Pfad_hervor` – der Testlauf im Graphen (#104)
  auf demselben (unveröffentlichten) Dialog: umschalten, Lauf starten, zwei Iterationen; geprüft werden
  besuchte Knoten mit ihrem Antwortwert, die offene Frage, die Zahl der gegriffenen Kanten nach jedem
  Schritt, „2 Iterationen" am Schleifenrahmen und der `⚡`-Chip am Knoten. Danach der Inspector-Pfad
  (Bindungen und Antworten je Iteration am gewählten Knoten), ein **Edit**, der den Pfad sichtbar schrumpfen
  lässt – und zum Schluss zweimal umschalten: „Verlauf" zeigt denselben Lauf, „Graph" bindet den Canvas
  neu. Das Zurückschalten ist zugleich die Probe, dass das Lösen der JS-Bindung den Circuit nicht reißt.

Ein paar Punkte, die beim Erweitern der Suite Zeit sparen:

- **Der Host braucht `ApplicationName = "Flirty.Designer"` und `EnvironmentName = "Development"`.**
  Nur so findet der `StaticWebAssetsLoader` die `*.staticwebassets.runtime.json` (er lädt sie über
  `Assembly.Load(ApplicationName)`) und `MapStaticAssets()` die passende `endpoints.json`. Fehlen sie,
  wird `_framework/blazor.web.js` nicht ausgeliefert, der Circuit kommt nie zustande und **jeder Klick
  verpufft**.
- **Nach jedem Seitenwechsel ist die erste Interaktion unzuverlässig.** Die Seite ist zunächst nur
  vorgerendert; bis der Circuit sie übernommen hat, verpuffen Klicks und Eingaben still. Ein
  brauchbares JS-Signal dafür gibt es nicht – `window.Blazor.reconnect` ist gesetzt und die
  `<!--Blazor:…-->`-Boot-Marker sind weg, *bevor* Ereignisse ankommen (nachgemessen). Deshalb führt
  `InteractWhenReadyAsync` die erste – **idempotente** – Interaktion in einer Wiederholschleife aus.
- **Der Canvas benutzt `InteractWhenReadyAsync` nicht, sondern wartet auf `data-canvas-ready`.** Das
  Wiederholmuster setzt Idempotenz voraus, und die gilt auf einem Canvas nicht: Ein wiederholtes Ziehen
  verschöbe doppelt, ein wiederholter Zoomschritt zoomte zweimal. Das Attribut setzt das JS-Modul,
  sobald es gebunden ist – und weil `OnAfterRenderAsync` beim Prerendering gar nicht läuft, ist es
  zugleich der Nachweis, dass der Circuit die Seite übernommen hat. Es ist das **erste**
  `data-`-Attribut im Designer; die übrige Suite adressiert über Rolle, Überschrift, Feld-`id` und
  CSS-Klasse.
- **Ein Drag braucht `ScrollIntoViewIfNeededAsync` und `page.Mouse`, nicht `DragToAsync`.** Zwei Fallen
  in einer: `DragToAsync` nutzt die HTML5-Drag-and-Drop-API, die auf einem SVG-Canvas mit
  Pointer-Events gar nicht auslöst – und Maus-Koordinaten sind **fensterbezogen**, während der
  Canvas-Host 70 vh hoch unter Kopfzeile, Hinweis und Werkzeugleiste steht. Ohne das Scrollen zielt die
  Geste auf einen Knoten der unteren Schichten ins Leere, ganz ohne Fehlermeldung; der Test scheitert
  dann an der Auswirkung, nicht an der Ursache. Gezogen wird über mehrere `Mouse.MoveAsync`-Schritte,
  damit die 4-px-Schwelle des Moduls wie bei einer echten Geste überschritten wird.
- **Ein Knoten enthält seit #103 zwei Buttons** – die Karte und den Ausgangs-Port. `GetByRole(Button)`
  innerhalb von `.graph-node` ist damit eine Strict-Mode-Verletzung; adressiert wird über
  `.graph-node-card` bzw. `.graph-port`. Das ist die Sorte Bruch, die eine neue Affordanz mit
  Zwangsläufigkeit auslöst: Der bestehende Test aus #101 wurde davon getroffen und musste mitgezogen
  werden.
- **Die Palette-Geste läuft über dieselbe Mechanik wie der Knoten-Zug** (`DragToCanvasAsync` →
  `DragBetweenAsync`), obwohl die Palette HTML *außerhalb* des SVG ist: Sie benutzt bewusst
  Pointer-Events statt HTML5-DnD (ADR 0008), damit es ein Ereignismodell und einen Riegel gibt.
- **Ein DOM-Wert beweist nicht, dass Blazor die Eingabe gesehen hat.** Verpufft die erste Interaktion auf
  einem frisch gerenderten Feld, steht der getippte Wert trotzdem im DOM – bis der nächste Render ihn mit
  dem gebundenen Wert überschreibt. Wer in diesem Fenster `ToHaveValueAsync` prüft, sieht Erfolg und
  speichert den alten Wert; der Test wird dann **unter Last** rot und allein grün. Belastbar ist nur eine
  Wirkung, die der Server erzeugt hat (hier: der Knoten mit dem neuen Schlüssel) – deshalb umfasst die
  wiederholte Einheit Füllen **und** Speichern.
- **Eine Geste, die ihren eigenen Auslöser sperrt, darf nicht in `InteractWhenReadyAsync` stehen** – es
  sei denn, die Wirkungsprüfung umfasst die ganze Sequenz. Der Speichern-Knopf ist für die Dauer des
  Requests `disabled`; eine Wiederholung nur des Klicks läuft in einen deaktivierten Knopf und wartet bis
  zum Timeout.
- **Was #103 im Browser prüft und was nicht.** Belegt sind die zwei riskantesten Gesten (Palette-Zug,
  Port → Knoten), der Reload als Schreib-Nachweis, die **Listen-Parität**, der Lesemodus samt „409 bleibt
  aus" sowie der komplette Inspector-Pfad (Felder speichern, verbinden, Default umschalten, löschen mit
  sichtbarer Kaskade). Bewusst offen für #105: Ziehen ins Leere, Trigger- und Schleifen-Anlage. Beide sind
  auf Command- bzw. Funktionsebene in `tests/Flirty.Tests/Designer` gedeckt – die E2E fehlt, die Prüfung
  nicht.

```pwsh
pwsh tests/Flirty.E2E/bin/Release/net10.0/playwright.ps1 install chromium   # einmalig
dotnet test tests/Flirty.E2E
```

## Roadmap (EPIC 7 / EPIC 11)

**EPIC 7 – Designer (abgeschlossen):** #37 Connection-Profile ✅ → #38 Dialog-CRUD-UI ✅ →
#39 Frage-Editor ✅ → #40 Branching-Editor ✅ → #41 Loop-Editor ✅ → #42 Trigger-Editor ✅ →
#43 Test-Runner ✅ → #46 Designer-E2E ✅.

**EPIC 11 – Visueller Graph-Designer (#99):** #100 Spike Canvas-Technik ✅ (ADR 0006) →
#101 Graph-Ansicht (lesend) ✅ → #102 Layout-Persistenz + Verschieben ✅ (ADR 0007) →
#103 Editieren auf dem Canvas ✅ (ADR 0008) → #104 Testlauf im Graphen ✅ →
#105 Playwright-E2E des Canvas.
