# Designer (Blazor)

Der **Flirty.Designer** ist eine Blazor Web App (Server-interaktiv, .NET 10) zum Anlegen und Bearbeiten
von Dialogen und zum Verwalten der Datenbank-Verbindungen. Er ist Teil von **EPIC 7** (Issues #37–#43,
Milestone „M3 – Designer"; die Playwright-E2E der UI kam mit #46 in M4 dazu). Referenz:
[ARCHITECTURE.md](./ARCHITECTURE.md) §4/§8, [PERSISTENCE.md](./PERSISTENCE.md).

> **Stand:** EPIC 7 ist umgesetzt: **Connection-Profil-Verwaltung (Multi-DB, #37)**, **Dialog-CRUD
> (#38)**, **Frage-Editor (#39)**, **Branching-Editor (#40)**, **Loop-Editor (#41)**,
> **Trigger-Editor (#42)** und **Test-Runner (#43)**; die UI ist seit **#46** per Playwright-E2E
> abgedeckt. Der Designer arbeitet über die Commands der Engine (via `ISender`), nicht direkt
> am `FlirtyDbContext` vorbei.

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
- Bei einem veröffentlichten Dialog weist ein Hinweis darauf hin, ihn vor dem Bearbeiten
  zurückzuziehen (laufende Sessions pinnen ihre `DialogVersion` und brechen nicht).
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
Ausgangsfrage, `CollectionKey` = Plural des Frage-Schlüssels (`skill` → `skills`). Kollidiert der Vorschlag
mit einem vorhandenen Frage-/Collection-Schlüssel oder ist er kein gültiger Bezeichner, bleibt das Feld
leer – ein stiller Ausweichname wäre schwerer nachzuvollziehen als ein leeres Pflichtfeld.

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

## Konventionen

- Blazor-Komponenten unter `Components/` (Seiten in `Components/Pages/`), Server-interaktiver Render-Mode
  (`@rendermode InteractiveServer` auf interaktiven Seiten).
- Gemeinsame UI-Primitiven (`.editor`, `.field`, `.input`, `.btn`, `.data-table`, `.badge`, `.msg`,
  `.banner`, `.empty`, `.back`, `.confirm`, `h1 .badge` …) liegen **global** in
  `wwwroot/app.css`; die `*.razor.css`-Dateien enthalten nur
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

Zwei Punkte, die beim Erweitern der Suite Zeit sparen:

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

```pwsh
pwsh tests/Flirty.E2E/bin/Release/net10.0/playwright.ps1 install chromium   # einmalig
dotnet test tests/Flirty.E2E
```

## Roadmap (EPIC 7)

#37 Connection-Profile ✅ → #38 Dialog-CRUD-UI ✅ → #39 Frage-Editor ✅ → #40 Branching-Editor ✅ →
#41 Loop-Editor ✅ → #42 Trigger-Editor ✅ → #43 Test-Runner ✅ → #46 Designer-E2E ✅.
