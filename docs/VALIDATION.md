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
| Question misconfigured (invalid `ValidationRules` JSON / regex pattern / type) | `InvalidOperationException` |
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
without persistence/invalidation, a valid answer runs through, plus the DI registration.
