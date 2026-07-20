# Trigger – In-Process-Notifications & Outbound-Webhooks

> Stand: Issue #33. Dieser Guide beschreibt die **In-Process-Trigger** der Flirty-Engine:
> Mediator-Notifications, die die Command-Handler beim Durchlaufen eines Dialogs publizieren, und wie
> Host-Apps eigene Handler per `AddFlirtyHandler<T, THandler>()` „reinhängen". Die zweite Trigger-Spielart
> – **Outbound-Webhooks** – baut auf genau diesen Notifications auf und ist seit #33 aktiv (siehe
> [Abschnitt „Outbound-Webhooks"](#outbound-webhooks)).

## Überblick

Flirty kennt zwei Rückkanäle in die Host-App (siehe [ARCHITECTURE.md](./ARCHITECTURE.md) §7):

1. **In-Process-Notifications** (dieses Dokument): über den [Mediator](./MEDIATOR.md) (martinothamar)
   publizierte `INotification`-Contracts. Die Engine ruft alle per DI registrierten
   `INotificationHandler<T>` synchron im selben Scope auf.
2. **Outbound-Webhooks** (seit #33): ein eingebauter `INotificationHandler`, der dieselben Notifications
   empfängt und als HTTP-`POST` ausliefert (`IHttpClientFactory` + Standard-Resilience: Retry/Timeout).
   Ziele werden per `o.AddWebhook(scope, url, expression?)` registriert (Registrierung als Stub seit #34).
   Details unten unter [Outbound-Webhooks](#outbound-webhooks).

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
automatisch auf. Der Convenience-Helper `AddFlirtyHandler<TNotification, THandler>()` (seit #32) kapselt
die Registrierung fluent:

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
    .AddFlirtyHandler<DialogCompletedNotification, OnDialogCompleted>();
```

`AddFlirtyHandler<T, THandler>()` registriert den Handler standardmäßig als `Scoped` – dieselbe
Lebensdauer wie der Mediator; über den optionalen Parameter lässt sich z. B. `ServiceLifetime.Singleton`
wählen. Er ist reine Bequemlichkeit für die rohe DI-Zeile und damit gleichwertig zu:

```csharp
services.AddScoped<INotificationHandler<DialogCompletedNotification>, OnDialogCompleted>();
```

Mehrere Handler je Notification sind erlaubt (alle werden aufgerufen) – der Helper nutzt bewusst
`Add` (kein `TryAdd`/`Replace`). Ein durchgängiges Beispiel zeigt der
[Console-Guide](./GETTING-STARTED-Console.md) und das lauffähige
[`src/Flirty.Samples`](../src/Flirty.Samples).

## Outbound-Webhooks

Neben In-Process-Handlern liefert Flirty dieselben Notifications seit #33 auch als **ausgehende HTTP-`POST`**
aus. Der eingebaute `WebhookNotificationHandler` (Core, `Flirty.Runtime`) wird – wie jeder Core-Handler –
vom Mediator-Source-Generator automatisch je Notification registriert; es ist **keine** manuelle
Registrierung nötig.

### Ziele registrieren

```csharp
services.AddFlirty(o =>
{
    o.UseSqlite(connectionString);
    o.AddWebhook(TriggerScope.OnDialogCompleted, "https://host.example/flirty/completed");
    o.AddWebhook(TriggerScope.AfterAnswer, "https://host.example/flirty/answers", expression: "age > 18");
});
```

`o.AddWebhook(TriggerScope scope, string url, string? expression = null)` legt fest, **zu welchem Zeitpunkt**
(Scope) an **welche URL** ausgeliefert wird und optional **unter welcher Bedingung**. Der Scope mappt 1:1 auf
die Notification (siehe Tabelle oben). Mehrere Registrierungen je Scope sind erlaubt (alle werden bedient).

> Der ältere String-Overload `o.AddWebhook(eventName, url)` (#34, ohne Scope) bleibt aus Kompatibilität
> bestehen, wird vom eingebauten Handler aber **nicht** ausgeliefert.

### Was ausgeliefert wird

- **Methode/Body:** HTTP-`POST` mit der als JSON serialisierten Notification (camelCase) als Body
  (`application/json`).
- **Header:** `X-Flirty-Event` trägt den auslösenden `TriggerScope` (z. B. `OnDialogCompleted`).

### Bedingtes Auslösen (`expression`)

Ist ein `expression` gesetzt, lädt der Handler Session und (gepinnte) Dialogversion über den `IDialogStore`
nach, baut denselben `ExpressionContext` wie das Branching (Antworten nach `Question.Key`, Loop-Collections,
Iterationsindex) und wertet die Bedingung über den `IExpressionEvaluator` aus – dieselbe Engine und Semantik
wie bei `Transition.Expression` (siehe [BRANCHING-EXPRESSIONS.md](./BRANCHING-EXPRESSIONS.md)). Nur bei
`true` wird ausgeliefert; ein leerer/`null`-Ausdruck gilt als bedingungslos. Ohne Ausdruck erfolgt **kein**
DB-Zugriff.

### Resilience & Fehlerverhalten

- Die Zustellung läuft über einen `IHttpClientFactory`-Named-Client (`"Flirty.Webhooks"`) mit
  `AddStandardResilienceHandler()` – **Retry** bei transienten Fehlern (5xx/408/429, Verbindungsfehler,
  Timeouts) plus Attempt-/Total-**Timeout**.
- **Best-effort:** Schlägt die Zustellung nach erschöpften Retries fehl (Statuscode ≥ 400 oder Ausnahme),
  wird der Fehler **geloggt, aber nicht geworfen** – ein toter Webhook darf den auslösenden Command
  (Start/Submit/Edit) nicht brechen.

## Hinweise & Grenzen

- **Synchron & In-Process**: `IPublisher.Publish` ruft die Handler synchron im Scope des auslösenden
  Commands auf. Wirft ein Handler, propagiert die Ausnahme an den Aufrufer des Commands. Für lange oder
  fehleranfällige Arbeit sollte der Handler entkoppeln (Queue/Hintergrunddienst).
- **Persistierter Zustand**: Da nach `SaveChangesAsync` publiziert wird, spiegeln die mitgelieferten
  Daten den gespeicherten Stand wider.
- **MSG0005**: Der Mediator-Source-Generator verlangt je Nachricht einen Handler in der Core-Compilation.
  Weil diese Trigger bewusst erst von Host-Apps behandelt werden, ist die Diagnose je Notification-Typ
  gezielt unterdrückt (`#pragma warning disable MSG0005`).
