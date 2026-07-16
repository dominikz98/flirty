# Dialog-Runtime: Start & Resume

Wie eine Host-App einen Dialog zur Laufzeit **startet** bzw. eine laufende Session **fortsetzt**
(Resume). Umgesetzt in Issue **#25** (Einstiegspunkt von EPIC 3 – Dialog-Runtime). Referenz:
[ARCHITECTURE.md](./ARCHITECTURE.md) §6/§7, Mediator-Grundlagen in [MEDIATOR.md](./MEDIATOR.md),
Repository in [PERSISTENCE.md](./PERSISTENCE.md#idialogstore-repository-21).

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
}
```

Die Facade wächst in den Folge-Issues additiv (Submit/Resume/Edit). Aktuell (#25) bietet sie den
Dialog-Start.

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

> Der in ARCHITECTURE §7 skizzierte optionale `seed?` (initiale Ausdruckskontext-Werte) ist in #25
> **bewusst noch nicht** enthalten: Es gibt weder einen Speicherort noch einen Konsumenten – die
> Ausdrucksauswertung (Transitions) beginnt erst mit `SubmitAnswerCommand` (#26). Der Parameter wird
> ergänzt, sobald er ausgewertet wird.

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
```

## Geplante Folge-Commands (EPIC 3)

| Issue | Command/Query | Zweck |
|---|---|---|
| #26 | `SubmitAnswerCommand` | Antwort validieren → persistieren → Transition-Auswertung → nächste Frage/Completion → Notifications |
| #27 | `ResumeDialogQuery` | Aktuellen Zustand + bisherige Antworten lesen |
| #28 | `EditAnswerCommand` | Zurückspringen, überschreiben, nachgelagerten Pfad neu berechnen |
| #29 | Loop-Runtime | Iterations-Sammlung je `CollectionKey`, Break-Bedingung |

## Verifikation

```pwsh
dotnet test tests/Flirty.Tests
```

Die Tests unter `tests/Flirty.Tests/Runtime/` treiben Start **und** Resume gegen eine echte
SQLite-Datenbank durch die volle Mediator-Pipeline via `IFlirtyEngine` (Facade → `ISender` → Handler
→ `IDialogStore` → EF Core) und decken die Fehlerfälle sowie die DI-Registrierung ab.
