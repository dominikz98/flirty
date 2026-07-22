# Designer (Blazor)

Der **Flirty.Designer** ist eine Blazor Web App (Server-interaktiv, .NET 10) zum Anlegen und Bearbeiten
von Dialogen und zum Verwalten der Datenbank-Verbindungen. Er ist Teil von **EPIC 7** (Issues #37–#43,
Milestone „M3 – Designer"). Referenz: [ARCHITECTURE.md](./ARCHITECTURE.md) §4/§8, [PERSISTENCE.md](./PERSISTENCE.md).

> **Stand:** Umgesetzt sind die **Connection-Profil-Verwaltung (Multi-DB, #37)**, das
> **Dialog-CRUD (#38)**, der **Frage-Editor (#39)** und der **Branching-Editor (#40)**. Die restlichen
> Editoren (Loops #41, Trigger #42, Test-Runner #43) folgen. Der Designer arbeitet über die
> Admin-Commands der Engine (via `ISender`), nicht direkt am `FlirtyDbContext` vorbei.

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

Die UI selbst wird künftig per Playwright-E2E abgedeckt (`tests/Flirty.E2E`, #46 – noch offen).

```pwsh
dotnet test tests/Flirty.Tests
```

## Roadmap (EPIC 7)

#37 Connection-Profile ✅ → #38 Dialog-CRUD-UI ✅ → #39 Frage-Editor ✅ → #40 Branching-Editor ✅ →
#41 Loop-Editor ✅ → #42 Trigger-Editor → #43 Test-Runner. Designer-E2E: #46.

> Für #42 (Trigger-Ausdrücke) lässt sich `DesignerExpressionContext` unverändert weiterverwenden –
> `TriggerDefinition.Expression` läuft über dieselbe Engine und denselben Kontext. Das Muster für das
> fehlende CRUD liefert #41: Commands im Core, DTOs/Endpunkte in `Flirty.AspNetCore`, Liste im
> `DialogEditor` plus eigene Unterseite.
