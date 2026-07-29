# Loops: iterations, collections & break conditions

How the dialog runtime executes loops: **cycle detection**, **iteration counter**, **collecting per
iteration** into a collection, **break condition** and **editing within an iteration**.
Implemented in issue **#29** (EPIC 3 – dialog runtime). Reference:
[ARCHITECTURE.md](./ARCHITECTURE.md) §10, domain model in [DOMAIN-MODEL.md](./DOMAIN-MODEL.md),
expressions/context in [BRANCHING-EXPRESSIONS.md](./BRANCHING-EXPRESSIONS.md), runtime commands in
[RUNTIME.md](./RUNTIME.md).

## Overview

Loops arise **through the existing branching**: a `Transition` points at an **earlier** question and thereby
forms a cycle. The `LoopDefinition` marker sits only as a metadata layer on top of it – there is
**no separate runtime special path** (ARCHITECTURE §11.5). The entire loop logic is encapsulated by the internal
`LoopResolver`; it is used by the shared `TransitionResolver` (context build-up for submit **and** edit) and by the
`SubmitAnswerCommandHandler` (field assignment on persisting).

The marker has two effects:
1. **Runtime**: the answers of the loop range are collected per iteration into `CollectionKey`
   (instead of overwritten); `SessionAnswer.LoopInstanceId`/`IterationIndex` allow multiple answers per
   question (one per iteration).
2. **Designer**: the cycle is visualized as a loop block with a marked breaking question and checked for
   a missing/unreachable exit – since #41, see [below](#loops-in-the-designer).

## `LoopDefinition` (marker)

| Field | Meaning |
|---|---|
| `CollectionKey` | Key under which the answers collected per iteration lie in the expression context (e.g. `positions`). |
| `EntryQuestionId` | Entry question of the loop (target of the loop-back transition). |
| `BreakingQuestionId` | Question whose exit transition leaves the cycle. |

The **exit** is not a property of its own, but runs through the normal `Transition` mechanics: the
breaking question has (at least) one loop-back transition to the entry question and one
exit transition that leaves the cycle. Which one takes effect is decided by the break condition.

## Flow

The `LoopResolver` is built per pinned dialog version and derives its state exclusively from the
existing `SessionAnswer` rows (no additional session field):

1. **Body determination** (once per loop, from the transition graph): the loop range is
   `(reachable forward from entry) ∩ (reachable backward to breaking) ∪ {entry, breaking}`. The
   forward search stops at the breaking question (its loop-back/exit edges are not followed).
   As a result, branches that exit the cycle early (reachable forward, but with no path to breaking)
   and questions upstream of the cycle (which reach breaking, but are not reachable from entry) stay outside
   the body. A single-question loop (`Entry == Breaking`) yields `{entry}`.
2. **Iteration/instance assignment** (when persisting an answer to question `Q`, before appending):
   - `Q` in no body → `LoopInstanceId`/`IterationIndex` stay `null` (unchanged non-loop behavior).
   - First entry (no body answer of the loop present) → **fresh** `LoopInstanceId`, `IterationIndex = 0`.
   - Otherwise: active instance = instance of the most recent body answer, `maxIter` = the largest iteration index of that
     instance. When the **entry question** is answered again in the running iteration (loop-back), the
     next iteration begins (`maxIter + 1`); all other questions stay in the current iteration (`maxIter`).
   - Invariant: at most one answer per `(instance, question, iteration)`.
3. **Collection build-up** (for the `ExpressionContext`, built **after** persisting): per
   `CollectionKey` the `Value` of the entry question per iteration of the most recent instance, ordered by
   iteration index. Each `CollectionKey` is **always** bound (empty list as long as the loop has not yet
   been entered), so that expressions like `positions.Count > 0` are evaluable even before the first iteration.
   The **running** iteration automatically counts along: the context is built after
   persisting, so the entry answer of the current iteration already lies in the collection
   when the break condition is evaluated at the breaking question.
4. **Break condition**: at the breaking question the usual branching decides. The loop-back transition
   (to the entry question) and the exit transition are checked by `Priority`; the condition expressions
   see the collected collection and the `iterationIndex`.
5. **Then normal flow**: if the exit transition takes effect, the session leaves the cycle onto the
   downstream question; its answers again carry no loop fields (`LoopInstanceId`/`IterationIndex`
   = `null`).

## Break conditions

The break/loop-back condition is an ordinary `Transition.Expression` and sees the same variables
as any branching – additionally the loop collections and the iteration index:

```text
more == "yes"          // loop-back based on the answer of the breaking question
positions.Count < 2    // loop-back until two entries are collected (collection-driven)
iterationIndex < 3     // at most four iterations (index 0..3)
```

The values of the collection are – like all expression values – **raw JSON text** per iteration
(the entry answer), e.g. `positions = ["{\"title\":\"Dev\"}", "{\"title\":\"Lead\"}"]`. Details on the
binding and typing: [BRANCHING-EXPRESSIONS.md](./BRANCHING-EXPRESSIONS.md).

## Editing within an iteration

`EditAnswerCommand`/`IFlirtyEngine.EditAnswerAsync` carry an optional zero-based
`IterationIndex`:

- **`null`** (default) → as outside loops: the **earliest** answer of the question is edited
  (for a loop question, iteration 0). Backward-compatible.
- **set** → specifically the answer of the given iteration.

The overwrite leaves `Sequence`, `LoopInstanceId` and `IterationIndex` unchanged. The invalidation
of the downstream answers stays **Sequence-based** (all answers with a higher `Sequence` are
discarded) – this correctly captures the rest of the edited iteration **and** all subsequent iterations; the
user then walks through the later iterations again.

## Error cases

| Situation | Behavior |
|---|---|
| Two loop ranges overlap (nested/overlapping loops) | `InvalidOperationException` (not supported, see MVP limits) |
| Editing a non-existent iteration (`IterationIndex` with no matching answer) | `InvalidOperationException` |

The remaining error cases of submit/edit apply unchanged (see [RUNTIME.md](./RUNTIME.md)).

## Deliberately outside the MVP

- **Nested/overlapping loops** – rejected fail-loud (one body per question).
- **Loop re-entry after exit** – within a single session run exactly one instance is kept per loop;
  re-entering after leaving does not create a second instance.
- **Structured iteration objects** – collected per iteration is exactly the entry answer (one
  entry per iteration), not all answers of the range.
- **`CollectionKey` ↔ `Question.Key` collision** – designer convention: keep collection and question keys
  disjoint. The collision is flagged at both places (identifier reference of the
  branching editor since #40, warning of the loop editor since #41: the question key is shadowed by the
  collection of the same name) – it is not prevented. Uniquely enforced is only the
  `CollectionKey` **within** the loops of a dialog.

## Loops in the designer

Since **#41** the markers can be maintained in the designer: the "Loops" section in the dialog editor
plus the detail page `/dialogs/{dialogId}/loops/{loopId}` with a **loop block** (range questions, marked
breaking question, back-jumps and exits), an editable `CollectionKey` and warnings – among others
about a cycle **with no reachable exit** (infinite loop) and about **overlapping** ranges, which
make the `LoopResolver` fail already in the constructor. Details and the full warning catalog:
[DESIGNER.md](./DESIGNER.md#loop-editor-41).

The cycle itself is still created by the branching editor – a `Transition` to an earlier question, marked
there as a **back-jump**. Such a back-jump without a matching marker is flagged in the loop section as a
suggestion (without a marker the answers of the cycle would be overwritten instead of collected).

Under the UI lie the admin commands `Create/Update/DeleteLoopCommand` and – for hosts without a designer –
the endpoints `POST {prefix}/dialogs/{dialogId}/loops` and `PUT/DELETE .../loops/{loopId}` from
`Flirty.AspNetCore`. The `CollectionKey` must be unique per dialog (otherwise 409); without this check
two markers of the same name would silently overwrite each other in the collection binding. As a read,
`GetDialogQuery` delivers the markers since #40 (`DialogDetail.Loops`) – the branching editor needs the
`CollectionKey`s to be able to validate expressions like `skills.Count > 0` at all.

## Usage

```csharp
// Loop dialog: position (entry) -> more (breaking, yes/no) -> summary (after the loop)
// Transitions: position -> more (default);
//              more -> position  (expression "more == \"yes\"", priority 0, loop-back);
//              more -> summary   (default, priority 1, exit).
// LoopDefinition { CollectionKey = "positions", EntryQuestionId = position, BreakingQuestionId = more }.

var start = await engine.StartDialogAsync("loop", "user-1");
await engine.SubmitAnswerAsync(start.SessionId, positionId, "\"Dev\"");   // iteration 0
await engine.SubmitAnswerAsync(start.SessionId, moreId, "\"yes\"");        // loop-back
await engine.SubmitAnswerAsync(start.SessionId, positionId, "\"Lead\"");  // iteration 1
var afterMore = await engine.SubmitAnswerAsync(start.SessionId, moreId, "\"no\""); // exit -> summary

// Read the state including iterations:
var state = await engine.ResumeDialogAsync(start.SessionId);
foreach (var answer in state.Answers)
{
    Console.WriteLine($"{answer.QuestionKey} [Iter {answer.IterationIndex?.ToString() ?? "-"}] = {answer.Value}");
}

// Correct the first iteration specifically (discards downstream iterations):
var edited = await engine.EditAnswerAsync(start.SessionId, positionId, "\"Developer\"", iterationIndex: 0);
Console.WriteLine($"Discarded downstream answers: {edited.InvalidatedAnswers}");
```

## Verification

```pwsh
dotnet test tests/Flirty.Tests
```

The tests under `tests/Flirty.Tests/Runtime/` cover the loop runtime: `LoopResolverTests` checks
body determination (incl. single-question loop and overlap rejection), iteration/instance assignment,
collection build-up and iteration index in isolation; `LoopRuntimeTests` drives multiple iterations, the
leaving of the cycle, collection- and iteration-index-driven break conditions as well as the editing
of an iteration against a real SQLite database; `FlirtyEngineTests` plays a loop
end-to-end through `IFlirtyEngine`.
