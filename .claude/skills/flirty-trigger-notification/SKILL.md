---
name: flirty-trigger-notification
description: Add a new trigger to the Flirty engine – Mediator notification contract, publication from a command handler, TriggerScope and outbound webhook behavior. Use for "new notification", "new trigger", "fire event", "webhook scope", "INotificationHandler", "back channel into the host app".
---

# Add a new notification / trigger + webhook scope

Triggers are the back channel into the host app: the engine publishes `INotification` contracts that
host apps handle via their own `INotificationHandler<T>`; the same publish also delivers outbound
webhooks. Reference: `docs/TRIGGERS.md`, `docs/MEDIATOR.md`.

## Prior art (read before writing)

- `src/Flirty/Runtime/Notifications/DialogCompletedNotification.cs` (and the three siblings).
- `src/Flirty/Runtime/WebhookNotificationHandler.cs` – built-in outbound handler.
- `src/Flirty/Domain/TriggerScope.cs` – scope enum (1:1 mapping to the notifications).
- `src/Flirty/Domain/TriggerConfig.cs` – JSON schema of the triggers configured on the dialog (#42).
- `src/Flirty/Runtime/SubmitAnswerCommand.cs` – shows publish after `SaveChangesAsync`.

## Existing set (4 notifications, namespace `Flirty.Runtime`)

| Notification | `TriggerScope` | Published by |
|---|---|---|
| `DialogStartedNotification` | `OnDialogStarted` | `StartDialogCommandHandler` (new start only) |
| `AnswerSubmittedNotification` | `AfterAnswer` | `SubmitAnswerCommandHandler` |
| `QuestionAnsweredNotification` | `AfterQuestion` | `SubmitAnswerCommandHandler` |
| `DialogCompletedNotification` | `OnDialogCompleted` | `SubmitAnswerCommandHandler` **and** `EditAnswerCommandHandler` |

## Steps

1. **Contract** in `src/Flirty/Runtime/Notifications/` – **must live in the core**, otherwise the source
   generator does not know it:
   ```csharp
   #pragma warning disable MSG0005 // Trigger notification is only handled by host apps.
   /// <summary>…English XML doc…</summary>
   public sealed record ThingHappenedNotification(Guid SessionId, string DialogKey) : INotification;
   #pragma warning restore MSG0005
   ```
   > **MSG0005 pitfall:** The Mediator generator requires a handler in the core compilation for each
   > message. Triggers, however, are deliberately handled only by host apps → suppress the diagnostic
   > **per notification type** specifically (not project-wide, otherwise a genuinely missing
   > command/query handler no longer stands out).

2. **Publish** in the appropriate command handler via an injected `IPublisher`, **after**
   `SaveChangesAsync` (so handlers see the persisted state):
   ```csharp
   await _publisher.Publish(new ThingHappenedNotification(session.Id, dialog.Key), ct);
   ```

3. **New `TriggerScope`** (only if a new point in time is needed): add the value in
   `src/Flirty/Domain/TriggerScope.cs` (enum persisted as `int` → **append only**, do not reorder
   existing ordinal values) and map it to the notification type in the `WebhookNotificationHandler`.

4. **Webhook delivery:** so the new trigger is delivered as an HTTP `POST`, implement the corresponding
   `INotificationHandler<ThingHappenedNotification>` in the `WebhookNotificationHandler` – forwarding to
   `DispatchAsync(scope, sessionId, currentQuestionId, payload, ct)` is enough. The handler is registered
   automatically by the generator – **no** manual DI needed.

   Since #42 `DispatchAsync` serves **two** sources: the code registrations
   (`o.AddWebhook(scope, url, expression?)`) **and** the `TriggerDefinition`s configured on the dialog
   with `Kind = Webhook` (`IDialogStore.GetTriggersForSessionAsync`, configuration as `TriggerConfig`
   JSON). For a new scope that means: it must also be selectable in the designer (`TriggerLabels`) and –
   if it refers to a question like `AfterQuestion` – be taken into account in the filter.

   **Best-effort is the law:** unreadable configuration, a missing URL, a non-evaluable condition and
   delivery errors are **logged, not thrown** (named client `WebhookNotificationHandler.HttpClientName`).
   The handler runs synchronously in the scope of start/submit/edit – any exception there breaks the
   command.

5. **Consumption in the host app** (document/example): `AddFlirtyHandler<ThingHappenedNotification,
   MyHandler>()` or raw `services.AddScoped<INotificationHandler<…>, MyHandler>()`. Multiple handlers per
   notification are allowed.

## Definition of Done

English XML docs · publish timing documented in `docs/TRIGGERS.md` (incl. order and whether
resume/reopen fires the trigger) · tests in `tests/Flirty.Tests/Runtime/` green: publish order via
`SpyPublisher`, webhook in isolation via `RecordingHttpMessageHandler` (`WebhookNotificationHandlerTests`)
and end-to-end over the real DI stack (`DialogTriggerDispatchTests`).

## Verification

```pwsh
dotnet build Flirty.sln
dotnet test tests/Flirty.Tests
```
