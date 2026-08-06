# Branching & Expressions: Expression engine

How Flirty evaluates the branching condition expressions – the abstraction `IExpressionEvaluator`
and the context model `ExpressionContext`. Implemented in issue **#22** (EPIC 2). Reference:
[ARCHITECTURE.md](./ARCHITECTURE.md) §7/§10/§11, model details in [DOMAIN-MODEL.md](./DOMAIN-MODEL.md).

## Overview

Branching and loops hang on **boolean condition expressions**:

- `Transition.Expression` – decides which transition from a question takes effect.
- `TriggerDefinition.Expression` – decides whether a trigger fires.

These expressions are evaluated via the replaceable engine `IExpressionEvaluator`
(namespace `Flirty.Expressions`). #22 only defines the abstraction; the sandboxed default engine
(#23), the compile check for the designer (#24) and the DI integration (#34) are described below in
their own sections.

## `IExpressionEvaluator`

```csharp
namespace Flirty.Expressions;

public interface IExpressionEvaluator
{
    bool Evaluate(string expression, ExpressionContext context);
}
```

- **Synchronous:** Evaluation is a pure in-memory operation (the default engine DynamicExpresso
  works synchronously) – hence no `async`/`CancellationToken`.
- **Null/empty expression:** A `null` or empty `Expression` counts, semantically, as
  *unconditionally matching*. This short-circuit handling belongs to the **runtime**, not to the
  evaluator; implementations may expect a non-empty expression.

## Context model `ExpressionContext`

The immutable `ExpressionContext` bundles the data of a running session that is visible at the
evaluation point in time. It maps the five building blocks named in ARCHITECTURE §7:

| Building block | Property | Type | Meaning |
|---|---|---|---|
| `answers` | `Answers` | `IReadOnlyDictionary<string, string?>` | answers indexed by `Question.Key` |
| Loop collections | `Collections` | `IReadOnlyDictionary<string, IReadOnlyList<string?>>` | answers collected per iteration, indexed by `LoopDefinition.CollectionKey` |
| `iterationIndex` | `IterationIndex` | `int?` | zero-based iteration index, `null` outside a loop |
| `now` | `Now` | `DateTimeOffset` | evaluation point in time |
| `session` | `Session` | `DialogSession` | the running session |

The values are **raw JSON text** – exactly as stored in `SessionAnswer.Value` (the format depends
on the question type). The typed deserialization (e.g. `"42"` → `int`) is done by the concrete
engine (#23); the context model itself deliberately stays untyped.

Collections that are not supplied are initialized as **empty, non-`null`** collections; `Session`
is mandatory (guarded via `ArgumentNullException`).

```csharp
var context = new ExpressionContext(
    session,
    now: DateTimeOffset.UtcNow,
    answers: new Dictionary<string, string?> { ["age"] = "42" },
    collections: new Dictionary<string, IReadOnlyList<string?>>
    {
        ["positions"] = ["{\"title\":\"Dev\"}", "{\"title\":\"Lead\"}"],
    },
    iterationIndex: null);
```

### Json answers (#136)

The CLR type an answer binds to is derived from the **JSON shape**, not from the question type. So a
`Json` question needed no evaluator change at all: an object binds as `Dictionary<string, object?>`, an
array as `List<object?>`, and a JSON string/number/boolean as that type. A host-declared custom question
type is a `Json` question, so the same holds for it – a colour stored as `"#ff0000"` binds as a `string`,
a composite address as a dictionary.

**One sharp edge, measured and pinned by a test.** A dictionary indexer is statically `object`, so:

```csharp
address["city"] == "Berlin"             // FALSE - object == object is a REFERENCE comparison
address["city"] as string == "Berlin"   // correct
address["city"].Equals("Berlin")        // correct, and reads better for a non-C# author
```

The first line compiles cleanly and silently evaluates to `false`. That is ordinary C# semantics rather
than a Flirty rule, which is exactly why nothing warns about it – the designer's live check accepts it
too, because it *is* a valid expression. When a branch on a JSON field never fires, this is the first
thing to look at.

## Example expressions

The default engine evaluates expressions like these (cf. ARCHITECTURE §10):

```text
age > 18                 // branching by numeric answer
positions.Count > 0      // break condition of a loop over the collected collection
```

## Default engine: `DynamicExpressoExpressionEvaluator` (#23)

The sandboxed default implementation of `IExpressionEvaluator` (namespace `Flirty.Expressions`)
builds on [DynamicExpresso](https://github.com/dynamicexpresso/DynamicExpresso). It is
**synchronous** and **stateless** (a fresh, isolated interpreter per evaluation) and therefore
usable as a singleton.

### Available expression variables

The context is mapped to flat top-level identifiers – matching expressions like `age > 18`:

| Identifier | Source | Typing |
|---|---|---|
| per `Question.Key` (e.g. `age`, `name`) | `Answers` | deserialized from JSON |
| per `CollectionKey` (e.g. `positions`) | `Collections` | list of iteration values (`.Count`, element access) |
| `now` | `Now` | `DateTimeOffset` |
| `iterationIndex` | `IterationIndex` | `int?` |
| `session` | `Session` | `DialogSession` |

Reserved identifiers (`now`, `iterationIndex`, `session`) are set last and therefore cannot be
shadowed by answer/collection keys of the same name.

### Typed deserialization

The values, present as raw JSON text, are taken over typed: JSON number → `long`/`double`,
JSON string → `string`, `true`/`false` → `bool`, array → list, object → dictionary. If a value
is not valid JSON (e.g. an unquoted choice key), it is used unchanged as a string. This way
`age > 18` (for `"42"`) and `name == "Ada"` (for `"\"Ada\""`) evaluate correctly.

### Sandbox (member whitelist, no raw C# `eval`)

> Why sandboxed and why this engine – including the discarded alternatives
> (Roslyn scripting, NCalc, a custom grammar): [ADR 0004](./adr/0004-sandboxed-expression-engine.md).
> In short: expressions come from the designer and live **in the database**, so they are data –
> anything executable here could be executed by anyone with write access to the configuration.

- Interpreter options strictly limited to `PrimitiveTypes | SystemKeywords`: literals, comparison,
  arithmetic and AND/OR operators. `CommonTypes` (`Math`, `Convert`, `Enumerable`) is **not**
  enabled.
- **Reflection stays blocked** (no `EnableReflection`), **assignments are disabled**
  (`EnableAssignment(AssignmentOperators.None)`). Accessible are only the injected variables and
  their instance members. Non-whitelisted types (e.g. `System.IO.File`) are unreachable.
- **Fail-loud:** syntax errors, unknown identifiers, sandbox violations and non-boolean results
  throw an `ExpressionEvaluationException` (which wraps the engine cause in `InnerException`,
  keeping the engine replaceable). The *compile check* on save (see
  [Validation / compile check](#validation--compile-check-24)) uses the same sandbox, but reports
  errors as a result instead of via an exception; short-circuiting `null`/empty expressions remains
  the runtime's job.

## Validation / compile check (#24)

For the designer the engine provides, alongside `Evaluate`, a **compile check**: `Validate`
**compiles** an expression (DynamicExpresso `Parse`) but **does not execute it**. This allows
expressions to be checked already **on save** and errors to be reported – without an exception, with
a structured result.

```csharp
public interface IExpressionEvaluator
{
    bool Evaluate(string expression, ExpressionContext context);

    // compiles, does not execute:
    ExpressionValidationResult Validate(string expression, ExpressionContext context);
}
```

`ExpressionValidationResult` carries:

| Property | Type | Meaning |
|---|---|---|
| `IsValid` | `bool` | whether the expression is compilable |
| `Error` | `string?` | human-readable error message (`null` when valid) |
| `ErrorPosition` | `int?` | zero-based error position in the expression (as far as reported), e.g. to underline in the designer |

The passed `ExpressionContext` supplies the available variables (and their types) for the check –
the validation uses **the same sandbox and variable binding as `Evaluate`**. This detects:

- **syntax errors** and invalid operator usage,
- **unknown identifiers** (variables the context does not know),
- **injection/sandbox violations** (reflection, non-whitelisted types like `System.IO.File`),
- a **non-boolean** result.

Behavior:

- A `null`/empty expression counts as **valid** ("unconditionally matching", consistent with the runtime).
- `Validate` **never throws** for a faulty expression – errors land in the result. The only exception:
  a `null` context (`ArgumentNullException`).
- Messages, except for **one** case, come unchanged from DynamicExpresso (including the position –
  "Unknown identifier 'xy' (at position 0)" tells the dialog author exactly the right thing). Only the
  message about **reflection access** is replaced: there the library advises turning reflection on via
  `Interpreter.EnableReflection()` – a hint to the embedder of the library, not to the user in the
  designer, and exactly opposed to the sandbox decision (#97). The case is recognized by the exception
  type `ReflectionNotAllowedException`, not by the (localized) message text. Where the library's limit
  lies: it kicks in on members that themselves return a reflective object again
  (`.Assembly`, `MethodInfo` …); a bare `GetType()` or `GetType().Name` passes through – no code can
  be executed from it, it stays at the type name.

```csharp
// Designer on save:
var result = evaluator.Validate(transition.Expression, context);
if (!result.IsValid)
{
    ShowError(result.Error, result.ErrorPosition);
}
```

### Sample context in the designer (#40)

When editing a transition the designer has **no running session** – the context for `Validate` is
therefore built from the dialog graph (`Flirty.Designer/Services/DesignerExpressionContext.cs`).
What matters here are the **types**, not the values: the sample value per question is the same raw
JSON text that the runtime stores in `SessionAnswer.Value`, and it is deserialized identically by the
engine (`FreeText → "Text"`, `Number → 0`, `Boolean → true`, `Date → "2026-01-01"`,
`SingleChoice →` first option value, `MultiChoice →` array of option values). A **date answer is thus
a string in the designer too** – `geburtstag < now` is rightly rejected, because it would fail at
runtime as well.

Loop collections are, as with the `LoopResolver`, **always** bound (before the first iteration as an
empty list), so that `skills.Count > 0` stays checkable; the `CollectionKey`s needed for that have
been delivered as reads by `GetDialogQuery` since #40 (`DialogDetail.Loops`). Keys that are not valid
identifiers or that are shadowed by `now`/`iterationIndex`/`session` are not bound by the designer and
are marked as unusable in its identifier reference.

For **string literals** the following holds: the engine parses C# escapes (`\"`, `\\`, `\n`, …), but
**not** `\u00XX` escapes. A value quoted via `JsonSerializer` is therefore not necessarily a valid
expression literal – its encoder writes a quotation mark as a Unicode escape, which the engine rejects
with "Invalid character escape sequence".

### Real bindings in the test run (#43)

The sample context answers "is the expression **compilable**?". Whether it also hits the **right**
thing is shown by the [test runner](./DESIGNER.md#test-runner-43): for every step of a real run it
displays the actual bindings (`Flirty.Designer/Services/RunExpressionContext.cs`) – answers per
question key, collected loop collections and the `iterationIndex`. This too is a **mirror** of the
core-internal `SessionExpressionContextBuilder` (which works on a `Dialog` entity with loaded
navigations, the designer only on navigation-free views); a test compares both at every step of a
run. If something is changed on the `SessionExpressionContextBuilder` or the `LoopResolver`, this
mirror – like `DesignerExpressionContext` and `LoopAnalyzer` – must be pulled along.

## Runtime consumption (#26)

The first runtime consumer of the engine is the `SubmitAnswerCommand` handler (#26, see
[RUNTIME.md](./RUNTIME.md#submitanswercommand)): after persisting an answer it evaluates the outgoing
`Transition`s of the question by `Priority` and picks the first matching one (otherwise the
`IsDefault` transition). Here – as described above – the **short-circuiting** of a `null`/empty
`Expression` (unconditionally matching) belongs to the runtime, not to the evaluator. The
`ExpressionContext` is formed from the existing `SessionAnswer`s (per question the last given answer,
indexed by `Question.Key`); since #26 the default engine is registered as a singleton in `AddFlirty()`.

Since the **loop runtime (#29)** the shared `TransitionResolver` additionally fills the two loop
building blocks of the context: `Collections` carries per `CollectionKey` the entry answer per
iteration (each `CollectionKey` is always bound – possibly an empty list – so that
`positions.Count > 0` is evaluable even before the first iteration), and `iterationIndex` reflects the
current iteration of the just-answered question (`null` outside a loop). Details in
[LOOPS.md](./LOOPS.md).

## DI integration & replacement (#34)

`AddFlirty()` registers the default engine as a **singleton** since #26 (it is stateless). Whoever
wants a different engine – e.g. NCalc – implements `IExpressionEvaluator` and replaces the default
registration:

```csharp
services.AddFlirty(o =>
{
    o.UseSqlite(connectionString);
    o.UseExpressionEvaluator<MyEvaluator>();   // replaces DynamicExpressoExpressionEvaluator
});
```

The custom type is registered as a singleton too. Both promises of this document are to be met:
`Evaluate` throws for non-evaluable expressions (fail-loud), `Validate` **only compiles** and reports
errors as a result. The short-circuiting of `null`/empty expressions remains the runtime's job – a
custom engine may expect a non-empty expression.
