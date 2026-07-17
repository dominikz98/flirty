# Trigger – In-Process-Notifications

> Stand: Issue #31. Dieser Guide beschreibt die **In-Process-Trigger** der Flirty-Engine:
> Mediator-Notifications, die die Command-Handler beim Durchlaufen eines Dialogs publizieren, und wie
> Host-Apps eigene Handler „reinhängen". Die zweite Trigger-Spielart – **Outbound-Webhooks** – baut auf
> genau diesen Notifications auf und folgt im weiteren Verlauf von EPIC 4 (siehe unten).

## Überblick

Flirty kennt zwei Rückkanäle in die Host-App (siehe [ARCHITECTURE.md](./ARCHITECTURE.md) §7):

1. **In-Process-Notifications** (dieses Dokument): über den [Mediator](./MEDIATOR.md) (martinothamar)
   publizierte `INotification`-Contracts. Die Engine ruft alle per DI registrierten
   `INotificationHandler<T>` synchron im selben Scope auf.
2. **Outbound-Webhooks**: ein eingebauter `INotificationHandler`, der dieselben Notifications empfängt
   und als HTTP-Request ausliefert (`IHttpClientFactory` + Retry/Timeout, `TriggerDefinition`-getrieben).
   Die Registrierung `o.AddWebhook(name, url)` existiert seit #34 als Stub; der aktive Handler folgt in
   EPIC 4.

## Die vier Notification-Contracts

Alle liegen im Core (`src/Flirty/Runtime/Notifications/`), Namespace `Flirty.Runtime`, als
`public sealed record ... : INotification`. Sie **müssen** im Core liegen, damit der Mediator-Source-
Generator sie kennt und `IPublisher.Publish` sie an registrierte Handler (auch aus Host-Assemblies)
ausliefert – siehe die zwei Mediator-Kernregeln in [MEDIATOR.md](./MEDIATOR.md).

| Notification | `TriggerScope` | Publiziert von | Nutzlast |
|---|---|---|---|
| `DialogStartedNotification` | `OnDialogStarted` | `StartDialogCommandHandler` (nur Neu-Start) | `SessionId, DialogId, DialogKey, ExternalUserKey, CurrentQuestionId?, StartedAt` |
| `AnswerSubmittedNotification` | `AfterAnswer` | `SubmitAnswerCommandHandler` | `SessionId, DialogKey, QuestionId, Value, LoopInstanceId?, IterationIndex?` |
| `QuestionAnsweredNotification` | `AfterQuestion` | `SubmitAnswerCommandHandler` | `SessionId, DialogKey, QuestionId, NextQuestionId?, IsCompleted` |
| `DialogCompletedNotification` | `OnDialogCompleted` | `SubmitAnswerCommandHandler` **und** `EditAnswerCommandHandler` | `SessionId, DialogKey, Answers` (`IReadOnlyList<SessionAnswerView>`) |

Das Scope-Mapping deckt sich 1:1 mit `Flirty.Domain.TriggerScope`
(`OnDialogStarted`/`AfterAnswer`/`AfterQuestion`/`OnDialogCompleted`).

## Wann wird was publiziert?

Publiziert wird stets **nach** `SaveChangesAsync`, damit ein Handler den persistierten Zustand sieht.

- **Start (`StartDialogCommand`)**: Ein echter Neu-Start meldet `DialogStarted`. Ein **Resume** einer
  bereits laufenden Session meldet bewusst **nichts** (nur der erste Start ist ein „Start").
- **Antwort (`SubmitAnswerCommand`)**: Nach dem Persistieren der Antwort wird `AnswerSubmitted` gemeldet,
  danach das Übergangs-Ergebnis als `QuestionAnswered` (mit `NextQuestionId`/`IsCompleted`). Schließt die
  Antwort den Dialog ab, folgt zusätzlich `DialogCompleted` (mit allen bisherigen Antworten).
  Reihenfolge im Abschlussfall: `AnswerSubmitted` → `QuestionAnswered` → `DialogCompleted`.
- **Editieren (`EditAnswerCommand`)**: Führt die Pfad-Neuberechnung nach einer Korrektur zum Abschluss,
  wird `DialogCompleted` gemeldet. Ein bloßes **Wieder-Öffnen** (Reopen auf eine Folgefrage) sowie das
  Überschreiben an sich lösen **keine** `AnswerSubmitted`/`QuestionAnswered` aus – nachträgliche
  Korrekturen sollen keine doppelten „Nach-Antwort"-Trigger auslösen.

## Eigenen Handler registrieren

Ein Handler ist ein `Mediator.INotificationHandler<T>`; die Registrierung genügt, die Engine ruft ihn
automatisch auf:

```csharp
public sealed class OnDialogCompleted : INotificationHandler<DialogCompletedNotification>
{
    public ValueTask Handle(DialogCompletedNotification notification, CancellationToken cancellationToken)
    {
        // z. B. E-Mail versenden, Datensatz anlegen, Metrik erhöhen …
        return ValueTask.CompletedTask;
    }
}

services
    .AddFlirty(o => o.UseSqlite(connectionString))
    .AddScoped<INotificationHandler<DialogCompletedNotification>, OnDialogCompleted>();
```

Mehrere Handler je Notification sind erlaubt (alle werden aufgerufen). Ein durchgängiges Beispiel zeigt
der [Console-Guide](./GETTING-STARTED-Console.md) und das lauffähige
[`src/Flirty.Samples`](../src/Flirty.Samples).

## Hinweise & Grenzen

- **Synchron & In-Process**: `IPublisher.Publish` ruft die Handler synchron im Scope des auslösenden
  Commands auf. Wirft ein Handler, propagiert die Ausnahme an den Aufrufer des Commands. Für lange oder
  fehleranfällige Arbeit sollte der Handler entkoppeln (Queue/Hintergrunddienst).
- **Persistierter Zustand**: Da nach `SaveChangesAsync` publiziert wird, spiegeln die mitgelieferten
  Daten den gespeicherten Stand wider.
- **MSG0005**: Der Mediator-Source-Generator verlangt je Nachricht einen Handler in der Core-Compilation.
  Weil diese Trigger bewusst erst von Host-Apps behandelt werden, ist die Diagnose je Notification-Typ
  gezielt unterdrückt (`#pragma warning disable MSG0005`).
