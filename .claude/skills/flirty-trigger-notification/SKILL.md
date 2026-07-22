---
name: flirty-trigger-notification
description: Neuen Trigger der Flirty-Engine hinzufügen – Mediator-Notification-Contract, Publikation aus einem Command-Handler, TriggerScope und Outbound-Webhook-Verhalten. Verwenden bei "neue Notification", "neuer Trigger", "Event feuern", "Webhook-Scope", "INotificationHandler", "Rückkanal in die Host-App".
---

# Neue Notification / neuen Trigger + Webhook-Scope hinzufügen

Trigger sind der Rückkanal in die Host-App: die Engine publiziert `INotification`-Contracts, die
Host-Apps über eigene `INotificationHandler<T>` behandeln; derselbe Publish liefert außerdem
Outbound-Webhooks aus. Referenz: `docs/TRIGGERS.md`, `docs/MEDIATOR.md`.

## Vorbilder (vor dem Schreiben lesen)

- `src/Flirty/Runtime/Notifications/DialogCompletedNotification.cs` (und die drei Geschwister).
- `src/Flirty/Runtime/WebhookNotificationHandler.cs` – eingebauter Outbound-Handler.
- `src/Flirty/Domain/TriggerScope.cs` – Scope-Enum (1:1-Mapping zu den Notifications).
- `src/Flirty/Domain/TriggerConfig.cs` – JSON-Schema der am Dialog konfigurierten Trigger (#42).
- `src/Flirty/Runtime/SubmitAnswerCommand.cs` – zeigt Publish nach `SaveChangesAsync`.

## Bestand (4 Notifications, Namespace `Flirty.Runtime`)

| Notification | `TriggerScope` | Publiziert von |
|---|---|---|
| `DialogStartedNotification` | `OnDialogStarted` | `StartDialogCommandHandler` (nur Neu-Start) |
| `AnswerSubmittedNotification` | `AfterAnswer` | `SubmitAnswerCommandHandler` |
| `QuestionAnsweredNotification` | `AfterQuestion` | `SubmitAnswerCommandHandler` |
| `DialogCompletedNotification` | `OnDialogCompleted` | `SubmitAnswerCommandHandler` **und** `EditAnswerCommandHandler` |

## Schritte

1. **Contract** in `src/Flirty/Runtime/Notifications/` – **muss im Core liegen**, sonst kennt ihn der
   Source-Generator nicht:
   ```csharp
   #pragma warning disable MSG0005 // Trigger-Notification wird erst von Host-Apps behandelt.
   /// <summary>…deutscher XML-Doc…</summary>
   public sealed record ThingHappenedNotification(Guid SessionId, string DialogKey) : INotification;
   #pragma warning restore MSG0005
   ```
   > **MSG0005-Fallstrick:** Der Mediator-Generator verlangt je Nachricht einen Handler in der
   > Core-Compilation. Trigger werden aber bewusst erst von Host-Apps behandelt → die Diagnose **je
   > Notification-Typ** gezielt unterdrücken (nicht projektweit, sonst fällt ein echt fehlender
   > Command-/Query-Handler nicht mehr auf).

2. **Publizieren** im passenden Command-Handler via injiziertem `IPublisher`, **nach**
   `SaveChangesAsync` (damit Handler den persistierten Zustand sehen):
   ```csharp
   await _publisher.Publish(new ThingHappenedNotification(session.Id, dialog.Key), ct);
   ```

3. **Neuer `TriggerScope`** (nur falls ein neuer Zeitpunkt gebraucht wird): Wert in
   `src/Flirty/Domain/TriggerScope.cs` ergänzen (Enum als `int` persistiert → **nur anhängen**, keine
   bestehenden Ordinalwerte umsortieren) und im `WebhookNotificationHandler` auf den Notification-Typ
   mappen.

4. **Webhook-Auslieferung:** Damit der neue Trigger als HTTP-`POST` ausgeliefert wird, im
   `WebhookNotificationHandler` das entsprechende `INotificationHandler<ThingHappenedNotification>`
   implementieren – die Weiterleitung an `DispatchAsync(scope, sessionId, currentQuestionId, payload, ct)`
   genügt. Der Handler wird vom Generator automatisch registriert – **keine** manuelle DI nötig.

   `DispatchAsync` bedient seit #42 **zwei** Quellen: die Code-Registrierungen
   (`o.AddWebhook(scope, url, expression?)`) **und** die am Dialog konfigurierten `TriggerDefinition`s mit
   `Kind = Webhook` (`IDialogStore.GetTriggersForSessionAsync`, Konfiguration als `TriggerConfig`-JSON).
   Für einen neuen Scope heißt das: er muss auch im Designer wählbar sein (`TriggerLabels`) und – falls er
   sich wie `AfterQuestion` auf eine Frage bezieht – im Filter berücksichtigt werden.

   **Best-effort ist Gesetz:** unlesbare Konfiguration, fehlende URL, nicht auswertbare Bedingung und
   Zustellfehler werden **geloggt, nicht geworfen** (Named-Client `WebhookNotificationHandler.HttpClientName`).
   Der Handler läuft synchron im Scope von Start/Submit/Edit – jede Ausnahme dort bricht den Command.

5. **Konsum in der Host-App** (dokumentieren/Beispiel): `AddFlirtyHandler<ThingHappenedNotification,
   MyHandler>()` oder roh `services.AddScoped<INotificationHandler<…>, MyHandler>()`. Mehrere Handler je
   Notification sind erlaubt.

## Definition of Done

Deutsche XML-Docs · Publish-Zeitpunkt in `docs/TRIGGERS.md` dokumentiert (inkl. Reihenfolge und ob
Resume/Reopen den Trigger auslöst) · Tests in `tests/Flirty.Tests/Runtime/` grün: Publish-Reihenfolge via
`SpyPublisher`, Webhook isoliert via `RecordingHttpMessageHandler` (`WebhookNotificationHandlerTests`) und
end-to-end über den echten DI-Stack (`DialogTriggerDispatchTests`).

## Verifikation

```pwsh
dotnet build Flirty.sln
dotnet test tests/Flirty.Tests
```
