# Answer validation: `IAnswerValidator` (pipeline behavior)

How a submitted answer is validated **semantically** – based on the question type
(`Question.Type`) and the optional rules configured per question (`Question.ValidationRules`),
implemented as a Mediator `IPipelineBehavior` that takes effect **before** the runtime handlers. Implemented in
issue **#30** – EPIC 3 – dialog runtime. Reference: [ARCHITECTURE.md](./ARCHITECTURE.md) §7,
runtime flow in [RUNTIME.md](./RUNTIME.md), Mediator basics in [MEDIATOR.md](./MEDIATOR.md).

## Overview

Until now the base pipeline validated only **declaratively** (DataAnnotations, `[Required]`) via the
`ValidationPipelineBehavior`. The **semantic** check – "does the value fit the question type and the
configured rules?" – has, since #30, been the job of the `IAnswerValidator`, invoked from the
`AnswerValidationPipelineBehavior`. An invalid answer is rejected **before** it is persisted
(`SubmitAnswerCommand`) or before the path is recomputed (`EditAnswerCommand`).

```
ISender.Send(SubmitAnswerCommand)
  └─ LoggingPipelineBehavior            (logs)
       └─ ValidationPipelineBehavior    (DataAnnotations: [Required] against null/empty)
            └─ AnswerValidationBehavior  (semantic: type + ValidationRules)  ← #30
                 └─ SubmitAnswerCommandHandler
```

## `IAnswerValidator`

```csharp
public interface IAnswerValidator
{
    AnswerValidationResult Validate(Question question, string value);
}
```

- A pure, **stateless** service (default `AnswerValidator`, registered as a singleton – analogous to the
  `IExpressionEvaluator`). No DB access: it receives the already-loaded `Question` (incl. options
  and `ValidationRules`) and the raw JSON answer value.
  **Unless the host declared a custom question type** (`o.AddQuestionType(...)`, see below): then a
  scoped `CustomQuestionTypeAnswerValidator` decorates it and the `IAnswerValidator` registration
  becomes `Scoped`, so a host validator can be resolved out of the request scope. Without a
  declaration nothing about the lifetime changes – the decorator is registered only on one.
- Returns a structured `AnswerValidationResult` (`IsValid` + `Errors`) instead of throwing –
  consistent with the `ExpressionValidationResult` of the expression path.
- Throws `InvalidOperationException` on a **misconfiguration** of the question (unknown type, invalid
  `ValidationRules` JSON, invalid regex pattern) – distinct from value errors.

### Value format (tolerant)

The answer value is **raw JSON text** (like `SessionAnswer.Value`). The validator reads it – like the
`DynamicExpressoExpressionEvaluator` – tolerantly: valid JSON is interpreted with its type, and if the
text is not valid JSON, it counts unchanged as a string (e.g. `"\"dev\""` **and** `dev` are treated
the same for a choice).

## Validation per `QuestionType`

| Type | Valid when … |
|---|---|
| `FreeText` | any text; additionally the rules `minLength`/`maxLength`/`pattern` |
| `Number` | a JSON number or a numeric string; additionally the rules `min`/`max` |
| `Boolean` | JSON `true`/`false` or `"true"`/`"false"` |
| `Date` | parseable as an ISO-8601 date (`DateTimeOffset`/`DateOnly`, invariant) |
| `SingleChoice` | the value matches exactly one `AnswerOption.Value` of the question |
| `MultiChoice` | a JSON array of strings; every entry a known `AnswerOption.Value` |
| `Json` | the value is a **well-formed JSON document** – object, array, string, number, boolean or `null`. That is the whole built-in contract; none of the `ValidationRules` applies. Semantics on top of it come from a host-declared custom type (see below) |

## `ValidationRules` (JSON schema)

`Question.ValidationRules` carries optional JSON (camelCase, read case-insensitively). All fields
are optional; a missing field means "no constraint". The rules are **type-scoped** –
inapplicable rules are ignored.

| Field | Type | Applies to | Meaning |
|---|---|---|---|
| `minLength` | int | `FreeText` | minimum length (characters) |
| `maxLength` | int | `FreeText` | maximum length (characters) |
| `min` | number | `Number` | smallest permitted value (inclusive) |
| `max` | number | `Number` | largest permitted value (inclusive) |
| `pattern` | string | `FreeText` | regular expression (partial match via `Regex.IsMatch`; anchor it in the pattern for a full check, e.g. `^…$`) – with a timeout (ReDoS protection) |

```json
{ "minLength": 2, "maxLength": 50, "pattern": "^[A-Za-z ]+$" }
```

You do not have to write the JSON by hand: the **question editor** of the Blazor designer maintains the rules
type-dependently via input fields and translates the `pattern` already on save (see
[DESIGNER.md → Validation rules](./DESIGNER.md#validation-rules)).

## Custom question types declared by the host (#136)

`QuestionType` is a closed enum inside the published package, so a consumer cannot append to it. The
extension path therefore does not widen the enum: a host declares its own types on top of the one
open-shaped built-in type `Json`, and a question selects one by carrying its key in
`Question.CustomTypeKey`. Rationale and the discarded alternatives:
[ADR 0011](./adr/0011-custom-question-types-on-an-open-base-type.md).

```csharp
services.AddFlirty(o => o
    .UseSqlite(connectionString)
    // With a validator: the generic overload registers it as scoped.
    .AddQuestionType<ColorAnswerValidator>("color", "Colour picker", sample: "\"#ff0000\"")
    // Without one: JSON well-formedness is then the whole check.
    .AddQuestionType("address", "Postal address", sample: """{"street":"","city":""}"""));
```

```csharp
public sealed class ColorAnswerValidator : IQuestionTypeValidator
{
    // Resolved from the REQUEST scope, so scoped dependencies work - including FlirtyDbContext.
    public ColorAnswerValidator(IHttpClientFactory clients) { … }

    public AnswerValidationResult Validate(Question question, string value)
        => IsHexColour(value)
            ? AnswerValidationResult.Valid
            : AnswerValidationResult.Invalid($"'{value}' is not a colour in the form #rrggbb.");
}
```

A question is then authored as `Type = Json, CustomTypeKey = "color"` – over HTTP, over MCP
(`flirty_question_create`) or in the designer. Four rules hold, and each of them is a decision rather
than an implementation detail:

- **Structure before semantics.** The built-in `Json` check runs first; a custom validator is never
  handed a value that is not well-formed JSON.
- **An unknown key degrades, it does not throw.** If the question names a type this host did not
  declare, the answer is validated as plain JSON and **one warning** is logged. That is deliberate: a
  published dialog version is immutable ([ADR 0005](./adr/0005-immutable-published-dialog-version.md)),
  so a throw would be an error nobody could repair. It also lets two consumers of one database declare
  different subsets.
- **A type declared without a validator is legitimate**, not half-finished: it names a shape for
  clients (display name, sample) and leaves the checking at well-formed JSON. It logs nothing.
- **The key is compared ordinally** and may contain only `[a-z0-9-]`. An empty, malformed or duplicate
  key throws at **declaration** time, not at the first submitted answer.

`CustomTypeKey` is refused on any type other than `Json` (400, on create and update). Whether the key
is *declared* is deliberately not checked there – see the degradation rule above.

**Reading a JSON answer in a branching condition** needs no configuration: the expression engine
derives the CLR type from the JSON shape, so an object binds as a dictionary and an array as a list.
Mind the one sharp edge, which is C# semantics rather than a Flirty rule – see
[BRANCHING-EXPRESSIONS.md](./BRANCHING-EXPRESSIONS.md#json-answers-136):

```csharp
address["city"] == "Berlin"             // FALSE - the indexer is object, so this compares references
address["city"] as string == "Berlin"   // correct
address["city"].Equals("Berlin")        // correct, and reads better
```

## `AnswerValidationPipelineBehavior`

The behavior connects the validator and the Mediator pipeline:

1. Takes effect only for answer-submitting commands (`SubmitAnswerCommand`, `EditAnswerCommand`, recognized by the
   internal marker `IAnswerCommand`) with a non-empty `Value`.
2. Resolves **session → pinned dialog version → question** via the `IDialogStore`.
3. Calls `IAnswerValidator.Validate(question, value)`. On `IsValid == false` an
   `AnswerValidationException` is thrown (derives from `ValidationException` and carries `QuestionId`
   + `Errors`) before the handler runs.
4. **Defer rule:** if the question cannot be resolved (session/dialog/question missing), the behavior
   does not validate and only calls `next` – the canonical errors (`SessionNotFoundException`,
   `InvalidOperationException`, DataAnnotations `ValidationException`) remain solely the concern of the
   handler or of the `ValidationPipelineBehavior`.

### Registration (why closed)

`AddFlirty()` registers the behavior **closed per command type** (not open-generic) as
`Scoped`:

```csharp
services.AddSingleton<IAnswerValidator, AnswerValidator>();
services.AddScoped<IPipelineBehavior<SubmitAnswerCommand, SubmitAnswerResult>,
    AnswerValidationPipelineBehavior<SubmitAnswerCommand, SubmitAnswerResult>>();
services.AddScoped<IPipelineBehavior<EditAnswerCommand, EditAnswerResult>,
    AnswerValidationPipelineBehavior<EditAnswerCommand, EditAnswerResult>>();
```

The behavior needs the scoped `IDialogStore` (and therefore a registered `FlirtyDbContext`).
An open-generic registration would construct it for **every** message – even where no
`FlirtyDbContext` is present – and break resolution. Closed, it takes effect only for submit/edit;
`Scoped`, it shares the same context as the handler (`GetSessionAsync` returns it tracked → no
second query).

## Error cases

| Situation | Behavior |
|---|---|
| Value does not fit the type / unknown choice / rule violation | `AnswerValidationException` (from the pipeline, before the handler) |
| Value of a `Json` question is not well-formed JSON | `AnswerValidationException` – a **value** error (HTTP 400), not a misconfiguration |
| Custom question type refuses the value | `AnswerValidationException` with the host validator's own errors |
| `CustomTypeKey` names a type this host did not declare | **no** error: validated as plain JSON, one log warning |
| Question misconfigured (invalid `ValidationRules` JSON / regex pattern / type) | `InvalidOperationException` (in the WebAPI: HTTP **409**) |
| Session/pinned dialog/question not resolvable | no validation → canonical handler error |
| `null`/empty `Value` | `ValidationException` (DataAnnotations, before the semantic validation) |

## Verification

```pwsh
dotnet test tests/Flirty.Tests
```

`tests/Flirty.Tests/Validation/AnswerValidatorTests.cs` checks the validator in isolation (all types,
rules, membership, tolerant fallback, misconfiguration).
`tests/Flirty.Tests/Validation/AnswerValidationPipelineBehaviorTests.cs` drives the behavior end-to-end
through the full pipeline via `IFlirtyEngine` against SQLite: an invalid answer → `AnswerValidationException`
without persistence/invalidation, a valid answer runs through, plus the DI registration. Since #136 it
also drives a host-declared type end-to-end, including the degradation when the declaration is absent.
`tests/Flirty.Tests/Validation/CustomQuestionTypeAnswerValidatorTests.cs` covers the decorator in
isolation (dispatch, the order, the single warning on an unknown key, the case-sensitive lookup), and
the lifetime promises – "stays a singleton without a declaration", "resolved from the request scope" –
are pinned in `tests/Flirty.Tests/DependencyInjection/FlirtyServiceCollectionExtensionsTests.cs`.
