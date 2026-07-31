# Dialog Runtime: Start, Resume, Submit & Edit

How a host app **starts** a dialog at runtime or **resumes** a running session (Resume), how it
**submits answers** (Submit) – whereby the dialog is run to completion via branching –, how it
**reads the current session state including the answers given so far** (`ResumeDialogQuery`) and how it
**edits an earlier answer** (Edit) – whereby the downstream path is recomputed/invalidated. Implemented
in issues **#25** (Start/Resume), **#26** (Submit), **#27** (read state) and **#28** (edit) – EPIC 3 –
Dialog Runtime. Reference: [ARCHITECTURE.md](./ARCHITECTURE.md)
§6/§7, Mediator basics in
[MEDIATOR.md](./MEDIATOR.md), branching/expressions in
[BRANCHING-EXPRESSIONS.md](./BRANCHING-EXPRESSIONS.md), loops in
[LOOPS.md](./LOOPS.md), repository in
[PERSISTENCE.md](./PERSISTENCE.md#idialogstore-repository-21).

## Overview

All engine operations are **Mediator commands/queries** and run through the base pipeline
(logging + validation). Host apps have two equivalent paths:

- **Facade `IFlirtyEngine`** – convenient, typed methods; encapsulates `ISender`.
- **`ISender.Send(...)` directly** – full control over the pipeline (custom behaviors/notifications).

`IFlirtyEngine` is registered by `AddFlirty()` as `ServiceLifetime.Scoped` (same lifetime
as the Mediator and `IDialogStore`).

## IFlirtyEngine facade

```csharp
public interface IFlirtyEngine
{
    Task<StartDialogResult> StartDialogAsync(
        string dialogKey, string externalUserKey, CancellationToken cancellationToken = default);

    Task<StartDialogResult> StartDialogVersionAsync(
        Guid dialogId, string externalUserKey, CancellationToken cancellationToken = default);

    Task<SubmitAnswerResult> SubmitAnswerAsync(
        Guid sessionId, Guid questionId, string value, CancellationToken cancellationToken = default);

    Task<ResumeDialogResult> ResumeDialogAsync(
        Guid sessionId, CancellationToken cancellationToken = default);

    Task<EditAnswerResult> EditAnswerAsync(
        Guid sessionId, Guid questionId, string value, int? iterationIndex = null,
        CancellationToken cancellationToken = default);
}
```

The facade grows additively across the follow-up issues. Currently it offers the dialog start (#25), the
start of a specific dialog version (#43), submitting answers (#26), reading the session state (#27)
and editing an earlier answer (#28).

## StartDialogCommand

```csharp
public sealed record StartDialogCommand(
    [property: Required] string DialogKey,
    [property: Required] string ExternalUserKey) : ICommand<StartDialogResult>;
```

- `DialogKey` – the stable business key of the dialog (the **highest published** version
  is started).
- `ExternalUserKey` – the host app's business user key (e.g. a user id).
- Both are `[Required]`; empty/`null` values are rejected by the `ValidationPipelineBehavior` with a
  `ValidationException` before the handler runs.

> An optional `seed?` (initial expression-context values) **deliberately does not exist**: the
> transition evaluation (see below) feeds its `ExpressionContext` exclusively from the
> persisted answers of the session; there is no storage location in the model for pre-filled start values.
> Whoever needs context from the outside puts it up front as an answered question or swaps the
> `IExpressionEvaluator` (see [BRANCHING-EXPRESSIONS.md](./BRANCHING-EXPRESSIONS.md#di-integration--replacement-34)).

### Result

```csharp
public sealed record StartDialogResult(Guid SessionId, bool IsResumed, QuestionView CurrentQuestion);

public sealed record QuestionView(
    Guid Id, string Key, string Text, QuestionType Type, IReadOnlyList<AnswerOptionView> Options);

public sealed record AnswerOptionView(Guid Id, string Key, string Label, string Value);
```

- `IsResumed` distinguishes a fresh start (`false`) from a resume (`true`).
- `CurrentQuestion` is a slim, navigation-free view of the currently open question incl. its
  options (in `Order` order) – the host app does not need to know the configuration graph.

## Start vs. Resume – flow

The handler uses `IDialogStore` exclusively:

1. **Resolve dialog:** `GetPublishedDialogAsync(dialogKey)` loads the highest published version
   including its graph. If it is missing, the handler throws `DialogNotFoundException`.
2. **Resume-or-fresh decision:** `FindActiveSessionAsync(dialog.Id, externalUserKey)` looks for the
   most recently started running (`InProgress`) session.
   - **Hit → Resume:** the existing session is returned (`IsResumed = true`), **no**
     new session is created.
   - **No hit → fresh start:** a new `DialogSession` is created (`Status = InProgress`,
     `CurrentQuestionId = dialog.StartQuestionId`, `DialogVersion` pinned, `StartedAt = UtcNow`),
     persisted via `AddSession` + `SaveChangesAsync` (`IsResumed = false`).
3. **Current question:** is projected from the loaded dialog graph.

### Version pinning

`FindActiveSessionAsync` filters on the exact `dialog.Id` – i.e. the **currently published**
version. Resume therefore applies only within this version: when a new dialog version is published,
the next start call does not find the session pinned to the old version and begins a new
session on the new version. An already running session stays bound to **its** version and
runs to completion there – Submit/Resume/Edit load their graph via the pinned `DialogId`.

For this promise to hold, two rules are needed in the admin CRUD (both since
[ADR 0005](./adr/0005-immutable-published-dialog-version.md)):

1. **A published version is immutable.** Every change to its graph (questions,
   answer options, transitions, loop markers, triggers, entry question) is rejected with
   `DialogPublishedException` → **409**. Otherwise it would immediately propagate into the running sessions:
   they pin the version but load the same row that the CRUD changes.
2. **Evolution happens via a new version** – `CreateDialogVersionCommand` clones the graph as a
   draft with the next version number. `PublishDialogCommand` retires the previously productive version
   of the same key in doing so, so that at most one version per key is published.

> **What the promise does not cover:** if a version is **deleted** while sessions are running on it,
> their graph is missing – every access ends in a conflict. That is why `DeleteDialogCommand` rejects
> deletion as long as sessions with `InProgress` exist, and
> `AbandonDialogSessionsCommand` ends them beforehand on request (status `Abandoned`, answers remain).

### Error cases

| Situation | Behavior |
|---|---|
| No published dialog for the key | `DialogNotFoundException` (carries the `DialogKey`) |
| Published dialog without `StartQuestionId` | `InvalidOperationException` (misconfiguration) |
| Empty/`null` `DialogKey`/`ExternalUserKey` | `ValidationException` (from the pipeline) |

## StartDialogVersionCommand (#43)

```csharp
public sealed record StartDialogVersionCommand(
    [property: Required] Guid DialogId,
    [property: Required] string ExternalUserKey) : ICommand<StartDialogResult>;
```

Starts a **specific dialog version – regardless of publication status**. Counterpart to the
`StartDialogCommand`, which deliberately starts only the highest *published* version of a business key.

The handler is identical except for the resolution: instead of `GetPublishedDialogAsync(key)` it uses
`GetDialogAsync(dialogId)`. Everything else – the resume-or-fresh decision via
`FindActiveSessionAsync(dialog.Id, externalUserKey)`, version pinning, `DialogStartedNotification` only
on a genuine fresh start – stays the same. This works without a special path because the session pins its
`DialogId` and Resume/Submit/Edit load their dialog version via the publication-**in**dependent
`GetDialogAsync` anyway.

**Use case:** preview and test before publishing – concretely the designer's test runner
(see [DESIGNER.md](./DESIGNER.md#test-runner-43)).

### Error cases

| Situation | Behavior |
|---|---|
| No dialog version with this id | `ConfigurationNotFoundException` |
| Dialog without `StartQuestionId` | `InvalidOperationException` (misconfiguration) |
| Empty/`null` `ExternalUserKey` | `ValidationException` (from the pipeline) |

> **Deliberately without an HTTP endpoint.** In `Flirty.AspNetCore` the publish status is the production
> barrier: over `MapFlirtyEndpoints` only published dialogs can be started, and that has not changed.
> What *has* changed is who may bypass it. Until #128 the bypass was reserved for in-process callers
> (designer, worker, tests); since then the optional package `Flirty.Mcp` exposes it deliberately as the
> tool `flirty_session_start_version`, because an MCP client authoring a dialog needs to test a draft for
> the same reason the designer's test runner does. That is an opt-in surface behind
> `FlirtyMcpSurface.Runtime` and behind whatever `MapFlirtyMcp().RequireAuthorization()` the host adds –
> a host that does not want the bypass reachable registers `FlirtyMcpSurface.Admin`, and one that exposes
> the command from its own routes reintroduces the barrier itself. See [MCP.md](./MCP.md).

## SubmitAnswerCommand

```csharp
public sealed record SubmitAnswerCommand(
    [property: Required] Guid SessionId,
    [property: Required] Guid QuestionId,
    [property: Required] string Value) : ICommand<SubmitAnswerResult>;
```

- `SessionId` – the running session in which the answer is given.
- `QuestionId` – the question to be answered; must correspond to the currently open question
  (`DialogSession.CurrentQuestionId`). Editing earlier answers is reserved for the
  `EditAnswerCommand` (#28).
- `Value` – the answer value as **raw JSON text** (format depends on the question type, e.g. an
  option's `AnswerOption.Value` as the JSON string `"\"dev\""`).

> `[Required]` only rejects `null`/empty `Value` via the `ValidationPipelineBehavior`; for the
> `Guid` fields it does not catch `Guid.Empty` (value type). Empty/wrong ids are handled at the business
> level in the handler (session lookup fails or question ≠ current question). The typed,
> rule-based answer validation (`IAnswerValidator` + `ValidationRules`) additionally kicks in via the
> `AnswerValidationPipelineBehavior` **before** the handler (#30, see [VALIDATION.md](./VALIDATION.md)).

### Result

```csharp
public sealed record SubmitAnswerResult(Guid SessionId, bool IsCompleted, QuestionView? NextQuestion);
```

- `IsCompleted` – `true` when the dialog was completed with this answer.
- `NextQuestion` – the question to present next (the same slim `QuestionView` as with
  `StartDialogCommand`) or `null` on completion.

### Flow

The handler uses `IDialogStore` (tracked session) and `IExpressionEvaluator` (transitions):

1. **Load session:** `GetSessionAsync(sessionId)` (tracked, incl. answers). If it is missing, the
   handler throws `SessionNotFoundException`.
2. **Preconditions:** the session must be `InProgress` and `QuestionId` must correspond to the currently
   open question; otherwise `InvalidOperationException`.
3. **Load pinned dialog:** `GetDialogAsync(session.DialogId)` delivers the version pinned by the session
   including its graph.
4. **Persist answer:** a new `SessionAnswer` is appended to the tracked session
   (`Value`, `AnsweredAt = UtcNow`, consecutive `Sequence`) – the `Id` is **not** pre-set
   (store-generated; cf. [PERSISTENCE.md](./PERSISTENCE.md#idialogstore-repository-21)). If the question
   lies within a loop range, `LoopInstanceId`/`IterationIndex` are additionally set (#29, see
   [LOOPS.md](./LOOPS.md)); outside a loop both stay `null`.
5. **Transition evaluation:** the outgoing transitions of the question are ordered by `Priority`;
   the first conditional transition whose expression evaluates true via the `IExpressionEvaluator` wins,
   otherwise the one marked as `IsDefault` takes effect. A `null`/empty `Expression` counts as
   unconditionally matching (short circuit in the runtime). The `ExpressionContext` is built from the
   answers given so far in the session (per question the answer given last, indexed by `Question.Key`);
   since the loop runtime (#29) the shared `TransitionResolver` additionally fills the loop collections
   gathered per iteration and the `iterationIndex` (see [LOOPS.md](./LOOPS.md)), so that break conditions like
   `positions.Count > 0` take effect.
6. **Advance or complete:**
   - **No outgoing transition** → completion: `Status = Completed`, `CompletedAt = UtcNow`,
     `CurrentQuestionId = null`.
   - **Matching transition** → `CurrentQuestionId` = its `TargetQuestionId`.
7. **Save:** `SaveChangesAsync()` (unit-of-work seam).

> **Notifications** (`AnswerSubmittedNotification`, `QuestionAnsweredNotification`,
> `DialogCompletedNotification`) are published by the handler since **#31** after saving:
> `AnswerSubmitted` after persisting the answer, `QuestionAnswered` with the transition result and –
> on completion – additionally `DialogCompleted`. Details and the full scope mapping in
> [TRIGGERS.md](./TRIGGERS.md).

### Error cases

| Situation | Behavior |
|---|---|
| No session for the `SessionId` | `SessionNotFoundException` (carries the `SessionId`) |
| Session not `InProgress` (completed/abandoned) | `InvalidOperationException` |
| `QuestionId` ≠ currently open question | `InvalidOperationException` |
| Transitions present, none matches **and** no default | `InvalidOperationException` (misconfiguration) |
| Matching transition points to an unknown target question | `InvalidOperationException` (misconfiguration) |
| `null`/empty `Value` | `ValidationException` (from the pipeline) |
| `Value` does not match the question's type/rules | `AnswerValidationException` (from the `AnswerValidationPipelineBehavior`, #30, see [VALIDATION.md](./VALIDATION.md)) |

## ResumeDialogQuery

```csharp
public sealed record ResumeDialogQuery(
    [property: Required] Guid SessionId) : IQuery<ResumeDialogResult>;
```

- `SessionId` – the session to read out. The query is **purely reading** (no `SaveChangesAsync`) and
  does not change the session.
- The project's first `IQuery`; it runs through the same pipeline (logging + validation) as the
  commands. `[Required]` does not catch `Guid.Empty` for `Guid` (value type) – an unknown id is
  handled at the business level in the handler with `SessionNotFoundException`.

> **Delimitation from the Resume of #25:** the *resume-or-fresh* of a session per user (by `dialogKey`
> + `externalUserKey`) is still done by `StartDialogCommand`. `ResumeDialogQuery` is the purely reading
> counterpart: "given a `SessionId`, give me the state + the answers given so far" – e.g. to restore a
> survey after a reload of the host app.

### Result

```csharp
public sealed record ResumeDialogResult(
    Guid SessionId,
    SessionStatus Status,
    QuestionView? CurrentQuestion,
    IReadOnlyList<SessionAnswerView> Answers);

public sealed record SessionAnswerView(
    Guid QuestionId, string QuestionKey, string Value, int Sequence, DateTimeOffset AnsweredAt,
    Guid? LoopInstanceId, int? IterationIndex);
```

- `Status` – the domain status of the session (`InProgress`/`Completed`/`Abandoned`), passed through directly.
- `CurrentQuestion` – the currently open question (the same slim `QuestionView` as with start/submit)
  or `null` when the session no longer has an open question (completed/abandoned).
- `Answers` – the answers given so far in ascending `Sequence` (chronological); per answer
  the business `QuestionKey` is resolved from the pinned dialog version. The `Value` is the
  stored raw JSON text. Since the loop runtime (#29) the view additionally carries `LoopInstanceId`
  and `IterationIndex` (both `null` outside a loop), so that host apps can display the gathered
  iterations (see [LOOPS.md](./LOOPS.md)).

### Flow

The handler uses `IDialogStore` exclusively (reading):

1. **Load session:** `GetSessionAsync(sessionId)` (incl. answers). If it is missing, the handler throws
   `SessionNotFoundException`.
2. **Load pinned dialog:** `GetDialogAsync(session.DialogId)` delivers the version pinned by the session
   including its graph (for resolving the business question keys and the question projection).
3. **Project answers:** per `SessionAnswer` → `SessionAnswerView` (key via the dialog graph),
   ascending by `Sequence`.
4. **Project current question:** if the session has a `CurrentQuestionId`, the question is projected from
   the graph; otherwise `null`.

### Error cases

| Situation | Behavior |
|---|---|
| No session for the `SessionId` | `SessionNotFoundException` (carries the `SessionId`) |
| Pinned dialog version no longer exists | `InvalidOperationException` |
| Current question not in the dialog graph | `InvalidOperationException` (misconfiguration) |

## EditAnswerCommand

```csharp
public sealed record EditAnswerCommand(
    [property: Required] Guid SessionId,
    [property: Required] Guid QuestionId,
    [property: Required] string Value,
    int? IterationIndex = null) : ICommand<EditAnswerResult>;
```

- `SessionId` – the session whose answer is edited.
- `QuestionId` – the question whose already-given answer is to be overwritten. It must belong to the
  pinned dialog and **already have been answered** in this session; unlike with
  `SubmitAnswerCommand` it does **not** have to be the currently open question (jumping back).
- `Value` – the new answer value as **raw JSON text** (format depends on the question type).
- `IterationIndex` – optional zero-based iteration index to edit, within a loop, the answer of a
  specific iteration in a targeted way (#29, see [LOOPS.md](./LOOPS.md)). `null` edits –
  as outside loops – the earliest answer of the question (iteration 0 for a loop question).

> `[Required]` only rejects `null`/empty `Value` via the `ValidationPipelineBehavior`; for the
> `Guid` fields it does not catch `Guid.Empty` (value type). Empty/wrong ids are handled at the business
> level in the handler.

### Result

```csharp
public sealed record EditAnswerResult(
    Guid SessionId, bool IsCompleted, QuestionView? NextQuestion, int InvalidatedAnswers);
```

- `IsCompleted` – `true` when the dialog is completed after the recomputation (the edited question
  is terminal).
- `NextQuestion` – the question to present next after the recomputation (the same slim
  `QuestionView`) or `null` on completion.
- `InvalidatedAnswers` – the number of downstream answers discarded because of the edit.

### Flow

The handler uses `IDialogStore` (tracked session) and – via the shared `TransitionResolver` –
the `IExpressionEvaluator` (transitions). Mental model: **"cut off the downstream path + re-submit the
edited question with the new value."**

1. **Load session:** `GetSessionAsync(sessionId)` (tracked, incl. answers). If it is missing, the
   handler throws `SessionNotFoundException`.
2. **Precondition:** the session must **not** be abandoned (`Abandoned`); running **and**
   completed sessions are editable (subsequent correction). Otherwise `InvalidOperationException`.
3. **Load pinned dialog:** `GetDialogAsync(session.DialogId)` delivers the version pinned by the session
   including its graph; the question must belong to the dialog.
4. **Find target answer:** without `IterationIndex` the (earliest) answer to `QuestionId` in the session;
   with `IterationIndex` specifically the answer of that iteration (loop). If the question (or the specified
   iteration) has not yet been answered, the handler throws `InvalidOperationException` (there is nothing to
   edit).
5. **Overwrite:** the target answer's `Value` is replaced and `AnsweredAt = UtcNow` is set; the
   `Sequence` is retained.
6. **Invalidate:** all downstream answers (higher `Sequence` than the edited one) are removed from the
   tracked session and deleted on save (cascade/orphan delete). There is deliberately
   **no** validity flag – the path is implicit, invalidated answers are hard-deleted.
7. **Recompute path:** starting from the edited question the transitions are re-evaluated via the
   `TransitionResolver` (the context now sees the overwritten value and **no** more
   downstream answers).
8. **Advance, complete or reopen:**
   - **No outgoing transition** → completion (`Status = Completed`, `CompletedAt = UtcNow`,
     `CurrentQuestionId = null`).
   - **Matching transition** → `Status = InProgress`, `CompletedAt = null`,
     `CurrentQuestionId = TargetQuestionId`. A previously completed session is thereby **reopened**.
9. **Save:** `SaveChangesAsync()` (unit-of-work seam).

### Error cases

| Situation | Behavior |
|---|---|
| No session for the `SessionId` | `SessionNotFoundException` (carries the `SessionId`) |
| Session abandoned (`Abandoned`) | `InvalidOperationException` |
| Pinned dialog version no longer exists | `InvalidOperationException` |
| `QuestionId` does not belong to the dialog | `InvalidOperationException` |
| Question not yet answered in this session | `InvalidOperationException` |
| Transitions present, none matches **and** no default | `InvalidOperationException` (misconfiguration) |
| Matching transition points to an unknown target question | `InvalidOperationException` (misconfiguration) |
| `null`/empty `Value` | `ValidationException` (from the pipeline) |
| `Value` does not match the question's type/rules | `AnswerValidationException` (from the `AnswerValidationPipelineBehavior`, #30, see [VALIDATION.md](./VALIDATION.md)) |

> **Loop iterations** (several answers per question via `LoopInstanceId`/`IterationIndex`) are edited in a
> targeted way via the optional `IterationIndex` (#29). The sequence-based invalidation thereby discards
> the rest of the edited iteration **and** all subsequent iterations; details in [LOOPS.md](./LOOPS.md).

## Usage

```csharp
var services = new ServiceCollection();
services.AddLogging();
services.AddFlirty();
services.AddDbContext<FlirtyDbContext>(o => o.UseSqlite(connectionString));
var provider = services.BuildServiceProvider();

using var scope = provider.CreateScope();

// Variant A – via the facade:
var engine = scope.ServiceProvider.GetRequiredService<IFlirtyEngine>();
var result = await engine.StartDialogAsync("onboarding", externalUserKey: "user-42");

// Variant B – directly via the Mediator:
var sender = scope.ServiceProvider.GetRequiredService<ISender>();
var same = await sender.Send(new StartDialogCommand("onboarding", "user-42"));

Console.WriteLine(result.IsResumed ? "Resumed" : "Freshly started");
Console.WriteLine(result.CurrentQuestion.Text);

// Submit an answer to the current question → next question or completion:
var next = await engine.SubmitAnswerAsync(
    result.SessionId, result.CurrentQuestion.Id, value: "\"dev\"");

Console.WriteLine(next.IsCompleted ? "Dialog completed" : next.NextQuestion!.Text);

// Later (e.g. after a reload) restore the state including the answers given so far:
var state = await engine.ResumeDialogAsync(result.SessionId);
Console.WriteLine($"Status: {state.Status}, answers so far: {state.Answers.Count}");
Console.WriteLine(state.CurrentQuestion?.Text ?? "no open question (completed)");

// Correct an earlier answer → the downstream path is recomputed/invalidated:
var edited = await engine.EditAnswerAsync(
    result.SessionId, result.CurrentQuestion.Id, value: "\"pm\"");
Console.WriteLine($"Discarded downstream answers: {edited.InvalidatedAnswers}");
Console.WriteLine(edited.IsCompleted ? "Dialog completed" : edited.NextQuestion!.Text);
```

## Follow-up commands (EPIC 3)

The **loop runtime (#29)** – iteration collection per `CollectionKey`, break condition, editing an
iteration – is implemented; it extends Submit/Edit additively and is documented in [LOOPS.md](./LOOPS.md).
The **business answer validation (#30)** – `IAnswerValidator` + `ValidationRules` as a
pipeline behavior – is implemented and documented in [VALIDATION.md](./VALIDATION.md). With that,
EPIC 3 is complete.

## Verification

```pwsh
dotnet test tests/Flirty.Tests
```

The tests under `tests/Flirty.Tests/Runtime/` drive start, resume, submit, reading the
session state (`ResumeDialogQuery`) **and** editing an earlier answer
(`EditAnswerCommand`) against a real SQLite database through the full Mediator pipeline via
`IFlirtyEngine` (facade → `ISender` → handler → `IDialogStore`/`IExpressionEvaluator` → EF Core) and
cover branching (conditional/default transition), completion, the consecutive answer `Sequence`, the
chronological answer order when reading, the path recomputation including invalidation of downstream
answers and the reopening of completed sessions, the error cases as well as the DI registration.
