# Triggers – in-process notifications & outbound webhooks

> Status: issue #42. This guide describes the **in-process triggers** of the Flirty engine:
> Mediator notifications that the command handlers publish while running through a dialog, and how
> host apps "plug in" their own handlers via `AddFlirtyHandler<T, THandler>()`. The second trigger flavor
> – **outbound webhooks** – builds on exactly these notifications and has been active since #33 (see
> [section "Outbound webhooks"](#outbound-webhooks)); since #42 webhooks can additionally be configured
> **on the dialog** (`TriggerDefinition`, maintained in the [Designer](./DESIGNER.md#trigger-editor-42)).

## Overview

Flirty knows two back channels into the host app (see [ARCHITECTURE.md](./ARCHITECTURE.md) §7):

1. **In-process notifications** (this document): `INotification` contracts published via the
   [Mediator](./MEDIATOR.md) (martinothamar). The engine calls all `INotificationHandler<T>` registered
   via DI synchronously in the same scope.
2. **Outbound webhooks** (since #33): a built-in `INotificationHandler` that receives the same
   notifications and delivers them as an HTTP `POST` (`IHttpClientFactory` + standard resilience: retry/timeout).
   Targets come from **two complementary sources**: registered in code via `o.AddWebhook(scope, url, expression?)`
   (#33/#34) **or** configured on the dialog as a `TriggerDefinition` (#42). Details below under
   [Outbound webhooks](#outbound-webhooks) and
   [Trigger definitions on the dialog](#trigger-definitions-on-the-dialog-42).

## The four notification contracts

They all live in the core (`src/Flirty/Runtime/Notifications/`), namespace `Flirty.Runtime`, as
`public sealed record ... : INotification`. They **must** live in the core so that the Mediator source
generator knows them and `IPublisher.Publish` delivers them to registered handlers (including those from
host assemblies) – see the two Mediator core rules in [MEDIATOR.md](./MEDIATOR.md).

| Notification | `TriggerScope` | Published by | Payload |
|---|---|---|---|
| `DialogStartedNotification` | `OnDialogStarted` | `StartDialogCommandHandler` **and** (since #43) `StartDialogVersionCommandHandler` – each only on a fresh start | `SessionId, DialogId, DialogKey, ExternalUserKey, CurrentQuestionId?, StartedAt` |
| `AnswerSubmittedNotification` | `AfterAnswer` | `SubmitAnswerCommandHandler` | `SessionId, DialogKey, QuestionId, Value, LoopInstanceId?, IterationIndex?` |
| `QuestionAnsweredNotification` | `AfterQuestion` | `SubmitAnswerCommandHandler` | `SessionId, DialogKey, QuestionId, NextQuestionId?, IsCompleted` |
| `DialogCompletedNotification` | `OnDialogCompleted` | `SubmitAnswerCommandHandler` **and** `EditAnswerCommandHandler` | `SessionId, DialogKey, Answers` (`IReadOnlyList<SessionAnswerView>`) |

The scope mapping coincides 1:1 with `Flirty.Domain.TriggerScope`
(`OnDialogStarted`/`AfterAnswer`/`AfterQuestion`/`OnDialogCompleted`).

## When is what published?

Publication always happens **after** `SaveChangesAsync`, so that a handler sees the persisted state.

- **Start (`StartDialogCommand`)**: A genuine fresh start reports `DialogStarted`. A **resume** of an
  already running session deliberately reports **nothing** (only the first start is a "start"). For
  `StartDialogVersionCommand` (#43, starting a specific version without publication) the same holds:
  a test run in the designer fires `OnDialogStarted` just as a productive start does – see the
  note [designer test runs fire for real](#notes--limits).
- **Answer (`SubmitAnswerCommand`)**: After the answer has been persisted, `AnswerSubmitted` is reported,
  followed by the transition result as `QuestionAnswered` (with `NextQuestionId`/`IsCompleted`). If the
  answer completes the dialog, `DialogCompleted` follows in addition (with all answers so far).
  Order in the completion case: `AnswerSubmitted` → `QuestionAnswered` → `DialogCompleted`.
- **Editing (`EditAnswerCommand`)**: If the path recomputation after a correction leads to completion,
  `DialogCompleted` is reported. A mere **reopen** (reopening a downstream question) as well as the
  overwrite itself do **not** trigger `AnswerSubmitted`/`QuestionAnswered` – later corrections should not
  raise duplicate "after-answer" triggers.

## Registering your own handler

A handler is a `Mediator.INotificationHandler<T>`; registration is enough, the engine calls it
automatically. The convenience helper `AddFlirtyHandler<TNotification, THandler>()` (since #32) wraps
the registration fluently:

```csharp
public sealed class OnDialogCompleted : INotificationHandler<DialogCompletedNotification>
{
    public ValueTask Handle(DialogCompletedNotification notification, CancellationToken cancellationToken)
    {
        // e.g. send an email, create a record, increment a metric …
        return ValueTask.CompletedTask;
    }
}

services
    .AddFlirty(o => o.UseSqlite(connectionString))
    .AddFlirtyHandler<DialogCompletedNotification, OnDialogCompleted>();
```

`AddFlirtyHandler<T, THandler>()` registers the handler as `Scoped` by default – the same lifetime as
the Mediator; via the optional parameter you can choose e.g. `ServiceLifetime.Singleton`. It is pure
convenience for the raw DI line and thus equivalent to:

```csharp
services.AddScoped<INotificationHandler<DialogCompletedNotification>, OnDialogCompleted>();
```

Multiple handlers per notification are allowed (all are called) – the helper deliberately uses
`Add` (no `TryAdd`/`Replace`). A complete example is shown by the
[Console guide](./GETTING-STARTED-Console.md) and the runnable
[`src/Flirty.Samples`](../src/Flirty.Samples).

## Outbound webhooks

Besides in-process handlers, Flirty has also delivered the same notifications as an **outbound HTTP `POST`**
since #33. The built-in `WebhookNotificationHandler` (core, `Flirty.Runtime`) is – like every core handler –
registered automatically per notification by the Mediator source generator; **no** manual registration is
needed.

### Registering targets

```csharp
services.AddFlirty(o =>
{
    o.UseSqlite(connectionString);
    o.AddWebhook(TriggerScope.OnDialogCompleted, "https://host.example/flirty/completed");
    o.AddWebhook(TriggerScope.AfterAnswer, "https://host.example/flirty/answers", expression: "age > 18");
});
```

`o.AddWebhook(TriggerScope scope, string url, string? expression = null)` defines **at which point in time**
(scope) delivery goes to **which URL** and optionally **under which condition**. The scope maps 1:1 onto
the notification (see the table above). Multiple registrations per scope are allowed (all are served).

> The older string overload `o.AddWebhook(eventName, url)` (#34, without a scope) remains for compatibility,
> but is **not** delivered by the built-in handler.

### What is delivered

- **Method/body:** HTTP `POST` with the notification serialized as JSON (camelCase) as the body
  (`application/json`).
- **Header:** `X-Flirty-Event` carries the triggering `TriggerScope` (e.g. `OnDialogCompleted`); for
  triggers with a set `name`, `X-Flirty-Trigger` with that name is added since #42.

### Conditional firing (`expression`)

If an `expression` is set, the handler loads the session and (pinned) dialog version via the `IDialogStore`,
builds the same `ExpressionContext` as branching (answers by `Question.Key`, loop collections,
iteration index) and evaluates the condition via the `IExpressionEvaluator` – the same engine and semantics
as with `Transition.Expression` (see [BRANCHING-EXPRESSIONS.md](./BRANCHING-EXPRESSIONS.md)). Only on
`true` is delivery made; an empty/`null` expression counts as unconditional.

If a condition **cannot be evaluated** – for instance because it references an answer that does not yet
exist at the trigger time (typical with `OnDialogStarted`) – the error is logged and the target is
skipped. The triggering command (start/submit/edit) continues; the condition counts as unmet. The
designer therefore already checks expressions on save (see
[DESIGNER.md](./DESIGNER.md#trigger-editor-42)).

## Trigger definitions on the dialog (#42)

Webhooks can be registered not only in code but also **configured on the dialog** – as a
`TriggerDefinition` row, maintained via the [Designer](./DESIGNER.md#trigger-editor-42) or the
admin endpoints (`POST/PUT/DELETE {prefix}/dialogs/{dialogId}/triggers`). Both sources apply
**additively**: the built-in handler serves, per notification, first the code registrations, then the
configured triggers of the dialog to which the session belongs.

| Field | Meaning |
|---|---|
| `Scope` | The trigger point – maps 1:1 onto the notification (table above). |
| `QuestionId` | **Required** for `AfterQuestion` (the trigger fires only after this question), empty otherwise. |
| `Kind` | `Webhook` (the engine delivers) or `InProcess` (see below). |
| `Config` | Channel configuration as JSON, schema: **`Flirty.Domain.TriggerConfig`**. |
| `Expression` | Optional condition – the same engine/semantics as above. |

The `Config` schema is deliberately small:

```json
{ "url": "https://host.example/flirty/completed", "name": "order-created" }
```

- **`url`** – target of the HTTP `POST`. With `Kind = Webhook` **required** and an absolute `http`/`https` address.
- **`name`** – optional business event name; delivered as the header `X-Flirty-Trigger`.

`TriggerConfig` is a public core API (`TryParse`/`ToJson`/`TryValidate`) and the **one** source of the
schema – admin commands, webhook delivery and the designer hang on it. The commands reject inconsistent
requests with HTTP 400 (broken JSON, missing/relative URL, `AfterQuestion` without a question, or a
question reference at another trigger point). Rows unusable at runtime – e.g. hand-written – are logged
and skipped, never thrown.

> **`Kind = InProcess` delivers nothing.** The four notifications are published anyway; they are handled
> by a handler in the host app (`AddFlirtyHandler<T, THandler>()`). An `InProcess` row therefore only
> documents the intent and names it – the webhook handler deliberately ignores it.

**Cost:** Because the definitions sit in the database, the handler runs **one** slim query per
notification (`IDialogStore.GetTriggersForSessionAsync`, filtered on the session dialog and scope,
via the foreign-key index). The earlier promise "no DB access without an expression" no longer holds
since #42. The full dialog graph is still loaded only if at least one target carries a condition.

### Resilience & error behavior

- Delivery runs through an `IHttpClientFactory` named client (`"Flirty.Webhooks"`) with
  `AddStandardResilienceHandler()` – **retry** on transient errors (5xx/408/429, connection errors,
  timeouts) plus attempt/total **timeout**.
- **Best-effort:** If delivery fails after exhausted retries (status code ≥ 400 or an exception),
  the error is **logged, but not thrown** – a dead webhook must not break the triggering command
  (start/submit/edit). The same holds for unusable trigger configuration and non-evaluable
  conditions: log, skip the target, carry on.

## Notes & limits

- **Synchronous & in-process**: `IPublisher.Publish` calls the handlers synchronously in the scope of
  the triggering command. If a handler throws, the exception propagates to the caller of the command. For
  long or error-prone work the handler should decouple (queue/background service).
- **Persisted state**: Since publication happens after `SaveChangesAsync`, the supplied data reflects
  the saved state.
- **MSG0005**: The Mediator source generator requires a handler in the core compilation per message.
  Because these triggers are deliberately handled only by host apps, the diagnostic is suppressed
  deliberately per notification type (`#pragma warning disable MSG0005`).
- **Designer test runs fire for real**: The designer's [test runner](./DESIGNER.md#test-runner-43) (#43)
  plays dialogs through with the real engine. Configured `Kind = Webhook` triggers are thereby actually
  delivered over HTTP – so before a test run against productive targets, check the URL. The runner
  logs what was published and points it out in the UI.
