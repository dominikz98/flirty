---
name: flirty-question-type
description: Add a new question type (QuestionType) and/or an answer validation rule in Flirty. Use for "new QuestionType", "new question type", "validation rule", "ValidationRules", "extend answer validation", "IAnswerValidator".
---

# Add a new QuestionType + validation rule

The question type determines how a submitted answer is validated **at the business level**. Validation
runs as a Mediator `IPipelineBehavior` (`AnswerValidationPipelineBehavior`) **before** the submit/edit
handlers. Reference: `docs/VALIDATION.md`, `docs/DOMAIN-MODEL.md`.

## Prior art (read before writing)

- `src/Flirty/Domain/QuestionType.cs` – the enum (persisted as `int`).
- `src/Flirty/Validation/AnswerValidator.cs` – the type- and rule-based check.
- `src/Flirty/Validation/ValidationRules.cs` – JSON schema of the optional rules.
- `src/Flirty/Validation/AnswerValidationResult.cs` – structured result (`IsValid` + `Errors`).

## Existing types & rules

Types: `SingleChoice`, `MultiChoice`, `FreeText`, `Number`, `Date`, `Boolean`.
Rules (`Question.ValidationRules`, camelCase JSON, all optional): `minLength`/`maxLength`/`pattern`
(FreeText), `min`/`max` (Number). The answer value is **raw JSON text** and is read leniently (valid JSON
is typed, otherwise treated as a string).

## Steps

### New QuestionType

1. **Append** the value in `src/Flirty/Domain/QuestionType.cs` (the enum is `int`-persisted → do not
   change existing ordinal values).
2. Add a `case` for the new type in `AnswerValidator.cs`: define the valid-when logic, interpret the
   answer value via the existing lenient JSON reader.
3. On a value violation return an `AnswerValidationResult` with `Errors`, **do not** throw. Only a
   genuine **misconfiguration** of the question (unknown type, invalid regex/JSON) throws
   `InvalidOperationException`.

### New validation rule

1. Add a field in `ValidationRules.cs` (optional, camelCase, read case-insensitively; absent = "no
   constraint"). Keep the rule **type-scoped** – ignore it on unaffected types.
2. Wire it into `AnswerValidator.cs` for the affected types. For regex: with a **timeout** (ReDoS
   protection), partial match via `Regex.IsMatch` (anchor with `^…$` in the pattern for a full match).
3. Document the rule in the table in `docs/VALIDATION.md`.

## Important

- No DB access in the validator – it receives the already-loaded `Question` (incl. options +
  `ValidationRules`) and the raw value. It is **stateless** and registered as a singleton.
- New choice-like types check membership against the question's `AnswerOption.Value`.

## Tests

`tests/Flirty.Tests/Validation/AnswerValidatorTests.cs` – validator in isolation (new type/rule: valid,
invalid, lenient fallback, misconfiguration). `AnswerValidationPipelineBehaviorTests.cs` – end-to-end via
`IFlirtyEngine` against SQLite (invalid answer → `AnswerValidationException` **without** persistence).

## Definition of Done

English XML docs · `docs/VALIDATION.md` (type table or rule table) updated · tests green. If the domain
model/schema changes, additionally run the `flirty-ef-migration` skill.

## Verification

```pwsh
dotnet build Flirty.sln
dotnet test tests/Flirty.Tests
```
