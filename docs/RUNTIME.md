# Dialog-Runtime: Start, Resume, Submit & Edit

Wie eine Host-App einen Dialog zur Laufzeit **startet** bzw. eine laufende Session **fortsetzt**
(Resume), wie sie **Antworten einreicht** (Submit) – wodurch der Dialog per Branching bis zum
Abschluss durchlaufen wird –, wie sie den **aktuellen Session-Zustand samt bisheriger Antworten
liest** (`ResumeDialogQuery`) und wie sie eine **frühere Antwort editiert** (Edit) – wodurch der
nachgelagerte Pfad neu berechnet/invalidiert wird. Umgesetzt in den Issues **#25** (Start/Resume),
**#26** (Submit), **#27** (Zustand lesen) und **#28** (Editieren) – EPIC 3 – Dialog-Runtime.
Referenz: [ARCHITECTURE.md](./ARCHITECTURE.md)
§6/§7, Mediator-Grundlagen in
[MEDIATOR.md](./MEDIATOR.md), Branching/Ausdrücke in
[BRANCHING-EXPRESSIONS.md](./BRANCHING-EXPRESSIONS.md), Repository in
[PERSISTENCE.md](./PERSISTENCE.md#idialogstore-repository-21).

## Überblick

Alle Engine-Operationen sind **Mediator-Commands/Queries** und laufen durch die Basis-Pipeline
(Logging + Validierung). Host-Apps haben zwei gleichwertige Wege:

- **Facade `IFlirtyEngine`** – bequeme, typisierte Methoden; kapselt `ISender`.
- **`ISender.Send(...)` direkt** – volle Kontrolle über die Pipeline (eigene Behaviors/Notifications).

`IFlirtyEngine` wird von `AddFlirty()` als `ServiceLifetime.Scoped` registriert (gleiche Lebensdauer
wie Mediator und `IDialogStore`).

## IFlirtyEngine-Facade

```csharp
public interface IFlirtyEngine
{
    Task<StartDialogResult> StartDialogAsync(
        string dialogKey, string externalUserKey, CancellationToken cancellationToken = default);

    Task<SubmitAnswerResult> SubmitAnswerAsync(
        Guid sessionId, Guid questionId, string value, CancellationToken cancellationToken = default);

    Task<ResumeDialogResult> ResumeDialogAsync(
        Guid sessionId, CancellationToken cancellationToken = default);

    Task<EditAnswerResult> EditAnswerAsync(
        Guid sessionId, Guid questionId, string value, CancellationToken cancellationToken = default);
}
```

Die Facade wächst in den Folge-Issues additiv. Aktuell bietet sie den Dialog-Start (#25), das Einreichen
von Antworten (#26), das Lesen des Session-Zustands (#27) und das Editieren einer früheren Antwort (#28).

## StartDialogCommand

```csharp
public sealed record StartDialogCommand(
    [property: Required] string DialogKey,
    [property: Required] string ExternalUserKey) : ICommand<StartDialogResult>;
```

- `DialogKey` – fachlicher, stabiler Schlüssel des Dialogs (die **höchste veröffentlichte** Version
  wird gestartet).
- `ExternalUserKey` – fachlicher Anwenderschlüssel der Host-App (z. B. Benutzer-Id).
- Beide sind `[Required]`; leere/`null`-Werte werden vom `ValidationPipelineBehavior` mit einer
  `ValidationException` abgewiesen, bevor der Handler läuft.

> Der in ARCHITECTURE §7 skizzierte optionale `seed?` (initiale Ausdruckskontext-Werte) bleibt auch
> mit #26 **bewusst noch nicht** enthalten: Die Transition-Auswertung (siehe unten) speist ihren
> `ExpressionContext` ausschließlich aus den persistierten Antworten der Session; für vorbelegte
> Startwerte gibt es weiterhin keinen Speicherort. Der Parameter wird ergänzt, sobald ein Konsument
> ihn tatsächlich auswertet.

### Ergebnis

```csharp
public sealed record StartDialogResult(Guid SessionId, bool IsResumed, QuestionView CurrentQuestion);

public sealed record QuestionView(
    Guid Id, string Key, string Text, QuestionType Type, IReadOnlyList<AnswerOptionView> Options);

public sealed record AnswerOptionView(Guid Id, string Key, string Label, string Value);
```

- `IsResumed` unterscheidet Neu-Start (`false`) von Resume (`true`).
- `CurrentQuestion` ist eine schlanke, navigationsfreie Sicht auf die aktuell offene Frage inkl. ihrer
  Optionen (in `Order`-Reihenfolge) – die Host-App muss den Konfigurationsgraphen nicht kennen.

## Start vs. Resume – Ablauf

Der Handler nutzt ausschließlich `IDialogStore`:

1. **Dialog auflösen:** `GetPublishedDialogAsync(dialogKey)` lädt die höchste veröffentlichte Version
   samt Graph. Fehlt sie, wirft der Handler `DialogNotFoundException`.
2. **Resume-oder-Neu-Entscheid:** `FindActiveSessionAsync(dialog.Id, externalUserKey)` sucht die
   zuletzt gestartete laufende (`InProgress`) Session.
   - **Treffer → Resume:** die bestehende Session wird zurückgegeben (`IsResumed = true`), es wird
     **keine** neue Session angelegt.
   - **Kein Treffer → Neu-Start:** eine neue `DialogSession` wird angelegt (`Status = InProgress`,
     `CurrentQuestionId = dialog.StartQuestionId`, `DialogVersion` gepinnt, `StartedAt = UtcNow`),
     via `AddSession` + `SaveChangesAsync` persistiert (`IsResumed = false`).
3. **Aktuelle Frage:** wird aus dem geladenen Dialog-Graphen projiziert.

### Versions-Pinning

`FindActiveSessionAsync` filtert auf die exakte `dialog.Id` – also die **aktuell veröffentlichte**
Version. Resume gilt damit nur innerhalb dieser Version: Wird zwischen zwei Aufrufen eine neue
Dialogversion veröffentlicht, findet der zweite Aufruf die auf die alte Version gepinnte Session
nicht und startet eine neue Session auf der neuen Version. Eine bereits laufende Session bleibt an
ihre `DialogVersion` gebunden und wird durch spätere Dialog-Änderungen nicht gebrochen.

### Fehlerfälle

| Situation | Verhalten |
|---|---|
| Kein veröffentlichter Dialog zum Schlüssel | `DialogNotFoundException` (trägt den `DialogKey`) |
| Veröffentlichter Dialog ohne `StartQuestionId` | `InvalidOperationException` (Fehlkonfiguration) |
| Leerer/`null` `DialogKey`/`ExternalUserKey` | `ValidationException` (aus der Pipeline) |

## SubmitAnswerCommand

```csharp
public sealed record SubmitAnswerCommand(
    [property: Required] Guid SessionId,
    [property: Required] Guid QuestionId,
    [property: Required] string Value) : ICommand<SubmitAnswerResult>;
```

- `SessionId` – die laufende Session, in der geantwortet wird.
- `QuestionId` – die zu beantwortende Frage; muss der aktuell offenen Frage
  (`DialogSession.CurrentQuestionId`) entsprechen. Das Bearbeiten früherer Antworten ist dem
  `EditAnswerCommand` (#28) vorbehalten.
- `Value` – der Antwortwert als **roher JSON-Text** (Format abhängig vom Fragetyp, z. B. der
  `AnswerOption.Value` einer Auswahl als JSON-String `"\"dev\""`).

> `[Required]` weist über das `ValidationPipelineBehavior` nur `null`/leeres `Value` ab; bei den
> `Guid`-Feldern greift es nicht gegen `Guid.Empty` (Werttyp). Leere/falsche Ids werden fachlich im
> Handler behandelt (Session-Lookup schlägt fehl bzw. Frage ≠ aktuelle Frage). Die typisierte,
> regelbasierte Antwort-Validierung (`IAnswerValidator` + `ValidationRules`) folgt in **#30**.

### Ergebnis

```csharp
public sealed record SubmitAnswerResult(Guid SessionId, bool IsCompleted, QuestionView? NextQuestion);
```

- `IsCompleted` – `true`, wenn der Dialog mit dieser Antwort abgeschlossen wurde.
- `NextQuestion` – die als Nächstes zu präsentierende Frage (dieselbe schlanke `QuestionView` wie bei
  `StartDialogCommand`) bzw. `null` bei Abschluss.

### Ablauf

Der Handler nutzt `IDialogStore` (getrackte Session) und `IExpressionEvaluator` (Transitions):

1. **Session laden:** `GetSessionAsync(sessionId)` (getrackt, inkl. Antworten). Fehlt sie, wirft der
   Handler `SessionNotFoundException`.
2. **Vorbedingungen:** Die Session muss `InProgress` sein und `QuestionId` der aktuell offenen Frage
   entsprechen; sonst `InvalidOperationException`.
3. **Gepinnten Dialog laden:** `GetDialogAsync(session.DialogId)` liefert die von der Session
   gepinnte Version samt Graph.
4. **Antwort persistieren:** ein neuer `SessionAnswer` wird an die getrackte Session angehängt
   (`Value`, `AnsweredAt = UtcNow`, fortlaufende `Sequence`) – die `Id` wird **nicht** vorbelegt
   (store-generiert; vgl. [PERSISTENCE.md](./PERSISTENCE.md#idialogstore-repository-21)).
5. **Transition-Auswertung:** Die ausgehenden Übergänge der Frage werden nach `Priority` geordnet;
   der erste bedingte Übergang, dessen Ausdruck über den `IExpressionEvaluator` zutrifft, gewinnt,
   sonst greift der als `IsDefault` markierte. Ein `null`er/leerer `Expression` gilt als bedingungslos
   zutreffend (Kurzschluss in der Runtime). Der `ExpressionContext` wird aus den bisherigen Antworten
   der Session gebildet (je Frage die zuletzt gegebene Antwort, indiziert nach `Question.Key`);
   Loop-Collections/Iterationsindex bleiben in #26 leer (Loop-Runtime folgt in #29).
6. **Weiterschalten oder Abschluss:**
   - **Kein ausgehender Übergang** → Abschluss: `Status = Completed`, `CompletedAt = UtcNow`,
     `CurrentQuestionId = null`.
   - **Greifender Übergang** → `CurrentQuestionId` = dessen `TargetQuestionId`.
7. **Speichern:** `SaveChangesAsync()` (Unit-of-Work-Naht).

> **Notifications** (`AnswerSubmittedNotification`, `QuestionAnsweredNotification`,
> `DialogCompletedNotification`) sind **nicht** Teil von #26. Sie werden zusammen mit ihrer Publikation
> aus den Command-Handlern im dedizierten Issue *„Notification-Contracts + Publikation"* der
> **EPIC 4 – Trigger** (Meilenstein M2) umgesetzt.

### Fehlerfälle

| Situation | Verhalten |
|---|---|
| Keine Session zur `SessionId` | `SessionNotFoundException` (trägt die `SessionId`) |
| Session nicht `InProgress` (abgeschlossen/abgebrochen) | `InvalidOperationException` |
| `QuestionId` ≠ aktuell offene Frage | `InvalidOperationException` |
| Übergänge vorhanden, keiner trifft **und** kein Default | `InvalidOperationException` (Fehlkonfiguration) |
| Greifender Übergang zeigt auf unbekannte Zielfrage | `InvalidOperationException` (Fehlkonfiguration) |
| `null`/leeres `Value` | `ValidationException` (aus der Pipeline) |

## ResumeDialogQuery

```csharp
public sealed record ResumeDialogQuery(
    [property: Required] Guid SessionId) : IQuery<ResumeDialogResult>;
```

- `SessionId` – die auszulesende Session. Die Query ist **rein lesend** (kein `SaveChangesAsync`) und
  verändert die Session nicht.
- Erste `IQuery` des Projekts; sie durchläuft dieselbe Pipeline (Logging + Validierung) wie die
  Commands. `[Required]` greift bei `Guid` (Werttyp) nicht gegen `Guid.Empty` – eine unbekannte Id wird
  fachlich im Handler mit `SessionNotFoundException` behandelt.

> **Abgrenzung zum Resume von #25:** Das *Resume-oder-Neu* einer Session je Anwender (per `dialogKey`
> + `externalUserKey`) leistet weiterhin `StartDialogCommand`. `ResumeDialogQuery` ist das rein lesende
> Gegenstück: „gegeben eine `SessionId`, gib mir Zustand + bisherige Antworten" – z. B. um eine
> Befragung nach einem Reload der Host-App wiederherzustellen.

### Ergebnis

```csharp
public sealed record ResumeDialogResult(
    Guid SessionId,
    SessionStatus Status,
    QuestionView? CurrentQuestion,
    IReadOnlyList<SessionAnswerView> Answers);

public sealed record SessionAnswerView(
    Guid QuestionId, string QuestionKey, string Value, int Sequence, DateTimeOffset AnsweredAt);
```

- `Status` – der Domain-Status der Session (`InProgress`/`Completed`/`Abandoned`), direkt durchgereicht.
- `CurrentQuestion` – die aktuell offene Frage (dieselbe schlanke `QuestionView` wie bei Start/Submit)
  bzw. `null`, wenn die Session keine offene Frage mehr hat (abgeschlossen/abgebrochen).
- `Answers` – die bisher gegebenen Antworten in aufsteigender `Sequence` (chronologisch); je Antwort
  wird der fachliche `QuestionKey` aus der gepinnten Dialogversion aufgelöst. Der `Value` ist der
  gespeicherte rohe JSON-Text. Loop-Felder (`LoopInstanceId`/`IterationIndex`) sind bewusst noch nicht
  enthalten und werden mit der Loop-Runtime (#29) ergänzt.

### Ablauf

Der Handler nutzt ausschließlich `IDialogStore` (lesend):

1. **Session laden:** `GetSessionAsync(sessionId)` (inkl. Antworten). Fehlt sie, wirft der Handler
   `SessionNotFoundException`.
2. **Gepinnten Dialog laden:** `GetDialogAsync(session.DialogId)` liefert die von der Session gepinnte
   Version samt Graph (für die Auflösung der fachlichen Frage-Schlüssel und die Frage-Projektion).
3. **Antworten projizieren:** je `SessionAnswer` → `SessionAnswerView` (Schlüssel via Dialog-Graph),
   aufsteigend nach `Sequence`.
4. **Aktuelle Frage projizieren:** hat die Session eine `CurrentQuestionId`, wird die Frage aus dem
   Graphen projiziert; sonst `null`.

### Fehlerfälle

| Situation | Verhalten |
|---|---|
| Keine Session zur `SessionId` | `SessionNotFoundException` (trägt die `SessionId`) |
| Gepinnte Dialogversion existiert nicht mehr | `InvalidOperationException` |
| Aktuelle Frage nicht im Dialog-Graphen | `InvalidOperationException` (Fehlkonfiguration) |

## EditAnswerCommand

```csharp
public sealed record EditAnswerCommand(
    [property: Required] Guid SessionId,
    [property: Required] Guid QuestionId,
    [property: Required] string Value) : ICommand<EditAnswerResult>;
```

- `SessionId` – die Session, deren Antwort editiert wird.
- `QuestionId` – die Frage, deren bereits gegebene Antwort überschrieben werden soll. Sie muss zum
  gepinnten Dialog gehören und in dieser Session **bereits beantwortet** worden sein; anders als bei
  `SubmitAnswerCommand` muss sie **nicht** die aktuell offene Frage sein (Zurückspringen).
- `Value` – der neue Antwortwert als **roher JSON-Text** (Format abhängig vom Fragetyp).

> `[Required]` weist über das `ValidationPipelineBehavior` nur `null`/leeres `Value` ab; bei den
> `Guid`-Feldern greift es nicht gegen `Guid.Empty` (Werttyp). Leere/falsche Ids werden fachlich im
> Handler behandelt.

### Ergebnis

```csharp
public sealed record EditAnswerResult(
    Guid SessionId, bool IsCompleted, QuestionView? NextQuestion, int InvalidatedAnswers);
```

- `IsCompleted` – `true`, wenn der Dialog nach der Neuberechnung abgeschlossen ist (die editierte Frage
  ist terminal).
- `NextQuestion` – die nach der Neuberechnung als Nächstes zu präsentierende Frage (dieselbe schlanke
  `QuestionView`) bzw. `null` bei Abschluss.
- `InvalidatedAnswers` – Anzahl der wegen der Editierung verworfenen nachgelagerten Antworten.

### Ablauf

Der Handler nutzt `IDialogStore` (getrackte Session) und – über den gemeinsamen `TransitionResolver` –
den `IExpressionEvaluator` (Transitions). Mentales Modell: **„nachgelagerten Pfad abschneiden + die
editierte Frage mit neuem Wert neu einreichen".**

1. **Session laden:** `GetSessionAsync(sessionId)` (getrackt, inkl. Antworten). Fehlt sie, wirft der
   Handler `SessionNotFoundException`.
2. **Vorbedingung:** Die Session darf **nicht** abgebrochen (`Abandoned`) sein; laufende **und**
   abgeschlossene Sessions sind editierbar (nachträgliche Korrektur). Sonst `InvalidOperationException`.
3. **Gepinnten Dialog laden:** `GetDialogAsync(session.DialogId)` liefert die von der Session gepinnte
   Version samt Graph; die Frage muss zum Dialog gehören.
4. **Ziel-Antwort finden:** die (früheste) Antwort auf `QuestionId` in der Session. Wurde die Frage noch
   nicht beantwortet, wirft der Handler `InvalidOperationException` (es gibt nichts zu editieren).
5. **Überschreiben:** der `Value` der Ziel-Antwort wird ersetzt und `AnsweredAt = UtcNow` gesetzt; die
   `Sequence` bleibt erhalten.
6. **Invalidieren:** alle nachgelagerten Antworten (höhere `Sequence` als die editierte) werden aus der
   getrackten Session entfernt und beim Speichern gelöscht (Cascade-/Orphan-Delete). Es gibt bewusst
   **kein** Validity-Flag – der Pfad ist implizit, invalidierte Antworten werden hart gelöscht.
7. **Pfad neu berechnen:** ausgehend von der editierten Frage werden die Übergänge über den
   `TransitionResolver` neu ausgewertet (der Kontext sieht nun den überschriebenen Wert und **keine**
   nachgelagerten Antworten mehr).
8. **Weiterschalten, Abschluss oder Wieder-Öffnen:**
   - **Kein ausgehender Übergang** → Abschluss (`Status = Completed`, `CompletedAt = UtcNow`,
     `CurrentQuestionId = null`).
   - **Greifender Übergang** → `Status = InProgress`, `CompletedAt = null`,
     `CurrentQuestionId = TargetQuestionId`. Eine zuvor abgeschlossene Session wird damit **wieder
     geöffnet**.
9. **Speichern:** `SaveChangesAsync()` (Unit-of-Work-Naht).

### Fehlerfälle

| Situation | Verhalten |
|---|---|
| Keine Session zur `SessionId` | `SessionNotFoundException` (trägt die `SessionId`) |
| Session abgebrochen (`Abandoned`) | `InvalidOperationException` |
| Gepinnte Dialogversion existiert nicht mehr | `InvalidOperationException` |
| `QuestionId` gehört nicht zum Dialog | `InvalidOperationException` |
| Frage in dieser Session noch nicht beantwortet | `InvalidOperationException` |
| Übergänge vorhanden, keiner trifft **und** kein Default | `InvalidOperationException` (Fehlkonfiguration) |
| Greifender Übergang zeigt auf unbekannte Zielfrage | `InvalidOperationException` (Fehlkonfiguration) |
| `null`/leeres `Value` | `ValidationException` (aus der Pipeline) |

> **Loop-Iterationen** (mehrere Antworten pro Frage über `LoopInstanceId`/`IterationIndex`) sind noch
> nicht Teil des Editierens; die Loop-Runtime folgt in **#29**.

## Nutzung

```csharp
var services = new ServiceCollection();
services.AddLogging();
services.AddFlirty();
services.AddDbContext<FlirtyDbContext>(o => o.UseSqlite(connectionString));
var provider = services.BuildServiceProvider();

using var scope = provider.CreateScope();

// Variante A – über die Facade:
var engine = scope.ServiceProvider.GetRequiredService<IFlirtyEngine>();
var result = await engine.StartDialogAsync("onboarding", externalUserKey: "user-42");

// Variante B – direkt über den Mediator:
var sender = scope.ServiceProvider.GetRequiredService<ISender>();
var same = await sender.Send(new StartDialogCommand("onboarding", "user-42"));

Console.WriteLine(result.IsResumed ? "Fortgesetzt" : "Neu gestartet");
Console.WriteLine(result.CurrentQuestion.Text);

// Antwort auf die aktuelle Frage einreichen → nächste Frage oder Abschluss:
var next = await engine.SubmitAnswerAsync(
    result.SessionId, result.CurrentQuestion.Id, value: "\"dev\"");

Console.WriteLine(next.IsCompleted ? "Dialog abgeschlossen" : next.NextQuestion!.Text);

// Später (z. B. nach einem Reload) den Zustand samt bisheriger Antworten wiederherstellen:
var state = await engine.ResumeDialogAsync(result.SessionId);
Console.WriteLine($"Status: {state.Status}, Antworten bisher: {state.Answers.Count}");
Console.WriteLine(state.CurrentQuestion?.Text ?? "keine offene Frage (abgeschlossen)");

// Eine frühere Antwort korrigieren → nachgelagerter Pfad wird neu berechnet/invalidiert:
var edited = await engine.EditAnswerAsync(
    result.SessionId, result.CurrentQuestion.Id, value: "\"pm\"");
Console.WriteLine($"Verworfene Folge-Antworten: {edited.InvalidatedAnswers}");
Console.WriteLine(edited.IsCompleted ? "Dialog abgeschlossen" : edited.NextQuestion!.Text);
```

## Geplante Folge-Commands (EPIC 3)

| Issue | Command/Query | Zweck |
|---|---|---|
| #29 | Loop-Runtime | Iterations-Sammlung je `CollectionKey`, Break-Bedingung |
| #30 | `IAnswerValidator` | Typisierte, regelbasierte Antwort-Validierung (Pipeline-Behavior) |

## Verifikation

```pwsh
dotnet test tests/Flirty.Tests
```

Die Tests unter `tests/Flirty.Tests/Runtime/` treiben Start, Resume, Submit, das Lesen des
Session-Zustands (`ResumeDialogQuery`) **und** das Editieren einer früheren Antwort
(`EditAnswerCommand`) gegen eine echte SQLite-Datenbank durch die volle Mediator-Pipeline via
`IFlirtyEngine` (Facade → `ISender` → Handler → `IDialogStore`/`IExpressionEvaluator` → EF Core) und
decken Branching (bedingter/Default-Übergang), Completion, die fortlaufende Antwort-`Sequence`, die
chronologische Antwort-Reihenfolge beim Lesen, die Pfad-Neuberechnung samt Invalidierung nachgelagerter
Antworten und Wieder-Öffnen abgeschlossener Sessions, die Fehlerfälle sowie die DI-Registrierung ab.
