# ADR 0004 – Sandboxed expression engine behind an abstraction

- **Status:** Accepted
- **Context issue:** #22 – `IExpressionEvaluator` + context model / #23 – DynamicExpresso implementation
  (sandbox); extended by #24 (compile check) and #34 (swap via option)
- **Affected:** `src/Flirty/Expressions/`, `src/Flirty/Runtime/` (transitions, triggers), `src/Flirty.Designer`

## Context

Branching and triggers hang on boolean condition expressions (`Transition.Expression`,
`TriggerDefinition.Expression`) like `age > 18` or `positions.Count > 0`. These expressions are written
by users in the **designer** and stored **in the database** – so they are *data*, not code, and are
evaluated at runtime against the answers of a running session.

That makes evaluation the **security-critical spot** of the engine: whatever can be executed here can be
executed by anyone with write access to the dialog configuration. Two further requirements come on top:
the designer must be able to check an expression **on save** (without running it – at that point there is
no session), and the values sit as **raw JSON text** in `SessionAnswer.Value`, so they still need to be
typed.

## Decision

Evaluation runs exclusively through the abstraction `IExpressionEvaluator`
(`src/Flirty/Expressions/IExpressionEvaluator.cs`) with two promises:

- `Evaluate(expression, context)` – evaluates and is **fail-loud** (`ExpressionEvaluationException`),
- `Validate(expression, context)` – **only compiles, does not execute**, and reports errors as a
  result (`ExpressionValidationResult` with message and position) instead of via an exception.

The default implementation is `DynamicExpressoExpressionEvaluator` based on
[DynamicExpresso](https://github.com/dynamicexpresso/DynamicExpresso), **sandboxed**:

- interpreter options strictly `PrimitiveTypes | SystemKeywords` – **without** `CommonTypes`
  (no `Math`, `Convert`, `Enumerable`),
- **no** reflection (`EnableReflection` is not called), **no** assignments
  (`EnableAssignment(AssignmentOperators.None)`),
- reachable are therefore **only** the injected context variables and their instance members;
  non-whitelisted types like `System.IO.File` do not exist for the expression.

The `ExpressionContext` is bound to **flat top-level identifiers** (one per `Question.Key`, one per
`CollectionKey`, plus `now`/`iterationIndex`/`session`) – matching the expressions users write.
Evaluation and compile check use the **same** interpreter setup; an expression the designer accepts is
therefore the same one the runtime sees.

## Discarded alternatives

- **Roslyn scripting / raw C# `eval`.** The full BCL on a string read from the database – that is remote
  code execution with extra steps. On top of that, noticeable compile costs per expression. Ruled out,
  regardless of the convenience gain.
- **NCalc.** Functionally viable and likewise sandboxed, but weaker at accessing .NET types and instance
  members (`positions.Count`, `session.StartedAt`), which would have bent the context model. Remains
  selectable as a replacement at any time via the abstraction – that is exactly what it is for.
- **A custom mini-grammar.** Maximally safe and maximally expensive: parser, error messages with position,
  type rules and documentation would all be homegrown – and for users the syntax would be unfamiliar.
- **Conditions as code in the host app** (delegates instead of expressions). Safe, but contradicts the
  goal of changing dialogs in the designer **without a deployment**; every new branch would be a release.

## Consequences

**Positive**

- The sandbox is an **allow list**: whatever was not bound is not reachable. That is auditable and stays
  so even as the stock of expressions grows.
- `Validate` lets the designer check **on save** – errors arise while configuring, not in the middle of a
  user's session.
- The evaluator is stateless (a fresh interpreter per evaluation) → registered as a **singleton**;
  swappable via `o.UseExpressionEvaluator<T>()`.

**Negative**

- The language scope is deliberately small. No `Math`, no LINQ – if the need grows, that is a deliberate
  extension of the whitelist, not a turning-on of `CommonTypes`.
- Values are raw JSON text and are typed by the engine. A **date is therefore a string** –
  `birthday < now` rightly fails. That is the most common stumbling block and belongs in every designer
  help.
- The designer must **mirror** the runtime context, because it has no session:
  `DesignerExpressionContext` (sample context, #40) and `RunExpressionContext` (real bindings in the test
  run, #43). Both mirrors have comparison tests against their core original – changes to the
  `SessionExpressionContextBuilder` or the `LoopResolver` must be pulled along there.
- A custom evaluator must keep **both** promises (Evaluate fail-loud, Validate compile only); the
  short-circuiting of `null`/empty expressions remains the runtime's job.

Details: [BRANCHING-EXPRESSIONS.md](../BRANCHING-EXPRESSIONS.md), designer side in
[DESIGNER.md](../DESIGNER.md).
