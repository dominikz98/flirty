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

**Two paths, and the first one is almost always the right one.** Path A is what a *host* does and costs
one call plus one class. Path B extends the *engine* and is measurably expensive – `QuestionType.<Member>`
appears 98 times across 15 files in `src`. Path B exists for a type that is genuinely universal
(a seventh built-in), not for "our app needs a colour picker".

### Path A – a host wants its own question type (do NOT touch the enum)

1. Declare it in `AddFlirty`, with or without a validator:
   ```csharp
   services.AddFlirty(o => o
       .AddQuestionType<ColourAnswerValidator>("color", "Colour picker", sample: "\"#ff0000\"")
       .AddQuestionType("address", "Postal address", sample: """{"street":"","city":""}"""));
   ```
   Keys are `[a-z0-9-]`, compared **ordinally**; an empty, malformed or duplicate one throws at
   **declaration** time.
2. Implement `IQuestionTypeValidator` (`src/Flirty/Validation/`). It is resolved from the **request
   scope**, so it may take scoped dependencies – `IHttpClientFactory`, `IOptions`, even the
   `FlirtyDbContext` the handler uses. Return a failing `AnswerValidationResult` for a value error;
   throw only for a real misconfiguration.
3. Author the question as `Type = Json` with `CustomTypeKey = "color"` – over HTTP, MCP
   (`flirty_question_create`) or in the designer. A key on any other type is a 400.
4. Render it in your own UI. The engine stores an opaque JSON value and reports the
   `customTypeKey` on the question view; **which control to show is the host's business**. Worked
   examples: `src/Flirty.Samples.Web/ColourAnswerValidator.cs` (scalar) and `AddressAnswerValidator.cs`
   (composite), with the matching `customTypes` map in `wwwroot/app.js`.
5. **Optional, for the Blazor designer** (#137): mirror the declaration as data in
   `question-types.json` in the designer's ContentRoot, so it shows the display name instead of the raw
   key, offers the type in the dropdowns and the canvas palette, and prefills the sample in the test
   runner:
   ```json
   { "questionTypes": [ { "key": "color", "displayName": "Colour picker", "sample": "\"#ff0000\"" } ] }
   ```
   Read once at startup. It buys **cosmetics and a prefill, never your validator** – that is code in
   your process, so a designer test run checks well-formed JSON and says so beside the input field.
   Details: `docs/DESIGNER.md` § *Host-declared question types*, ADR 0012.

Four rules that hold, each a decision rather than an implementation detail:

- **Structure before semantics** – the built-in `Json` well-formedness check runs first, so a validator
  never sees malformed input.
- **An unknown key degrades**, it does not throw: validated as plain JSON plus one log warning. A
  published dialog cannot be edited (ADR 0005), so a throw would be unrepairable – and this is what lets
  two consumers of one database declare different subsets.
- **A type without a validator is legitimate**, not half-finished: it names a shape for clients and
  leaves the checking at well-formed JSON.
- **The lifetime changes only when you declare something.** With at least one declaration
  `IAnswerValidator` becomes scoped (a decorator); without, it stays the plain singleton. That applies to
  the designer too, as soon as it has a descriptor file.

Reference: `docs/VALIDATION.md` § *Custom question types declared by the host*, ADR 0011 and ADR 0012.

### Path B – a new built-in `QuestionType` (expensive, measure before starting)

1. **Append** the value in `src/Flirty/Domain/QuestionType.cs` (the enum is `int`-persisted → do not
   change existing ordinal values). Extend the ordinal pinning in `tests/…/Domain/DomainModelTests.cs`.
2. Add a `case` in `AnswerValidator.cs`: define the valid-when logic, interpret the answer value via
   the existing lenient JSON reader.
3. On a value violation return an `AnswerValidationResult` with `Errors`, **do not** throw. Only a
   genuine **misconfiguration** of the question (unknown type, invalid regex/JSON) throws
   `InvalidOperationException` (over HTTP: **409**, not 500).
4. Then the part the old version of this skill omitted, and which is the actual cost:
   - **Designer** (~9 sites, all with a silent fallback): `Services/AnswerValueCodec.cs` (encode,
     describe, decode), `Models/QuestionTypeLabels.cs`, `Services/DesignerExpressionContext.cs`
     (`SampleJson`, `KindOf`, `TypeLabelOf`, `NoteFor`, `ExampleFor`), `Models/QuestionFormModel.cs`
     (`SuggestKey`, the rule gate), `Models/AnswerInputModel.cs`, `Components/AnswerInput.razor`
     (+ `InputType`), `Services/DialogGraphBuilder.cs`.
   - **Samples**: the hand-duplicated enum in `src/Flirty.Samples.Web/wwwroot/app.js` (HTTP serializes
     the ordinal) plus its branch sites, and `src/Flirty.Samples/AnswerEncoder.cs`.
   - **MCP prose**: the `[Description]` of the `type` parameter, `FlirtyMcpInstructions`,
     `FlirtySessionTools`' value contract – none of which any test can see.
   - **Docs**: `docs/VALIDATION.md`, `DOMAIN-MODEL.md`, `ARCHITECTURE.md`, `BRANCHING-EXPRESSIONS.md`,
     `MCP.md`, `DESIGNER.md`, `GETTING-STARTED-*`.
   The golden test `QuestionTypeLabelsTests.Every_question_type_has_a_label_of_its_own` catches the
   cheapest of these omissions; the rest are silent.
5. If the type needs a new column, run the `flirty-ef-migration` skill – all three provider sets.

### New validation rule

1. Add a field in `ValidationRules.cs` (optional, camelCase, read case-insensitively; absent = "no
   constraint"). Keep the rule **type-scoped** – ignore it on unaffected types.
2. Wire it into `AnswerValidator.cs` for the affected types. For regex: with a **timeout** (ReDoS
   protection), partial match via `Regex.IsMatch` (anchor with `^…$` in the pattern for a full match).
3. Document the rule in the table in `docs/VALIDATION.md`.

## Important

- No DB access in the **built-in** validator – it receives the already-loaded `Question` (incl. options
  + `ValidationRules`) and the raw value. `AnswerValidator` is stateless and registered as a singleton.
  A **host** validator (`IQuestionTypeValidator`) is the deliberate opposite: it is scoped and may
  reach a database. That is why declaring one turns the `IAnswerValidator` registration scoped.
- New choice-like types check membership against the question's `AnswerOption.Value`. A custom type
  receives `question.Options` too and may read them – so "the engine does not evaluate options" is not
  the same as "these options are useless".
- A JSON answer binds by **shape**: an object as `Dictionary<string, object?>`, an array as a list, a
  scalar as itself. In a branching condition the indexer is typed `object`, so
  `address["city"] == "Berlin"` compiles and is always `false` – write `as string ==` or `.Equals(…)`.

## Tests

`tests/Flirty.Tests/Validation/AnswerValidatorTests.cs` – validator in isolation (new type/rule: valid,
invalid, lenient fallback, misconfiguration). `AnswerValidationPipelineBehaviorTests.cs` – end-to-end via
`IFlirtyEngine` against SQLite (invalid answer → `AnswerValidationException` **without** persistence).

For path A: `CustomQuestionTypeAnswerValidatorTests.cs` (dispatch, ordering, the single warning on an
unknown key) and `FlirtyServiceCollectionExtensionsTests.cs` (the lifetime promises – "stays a singleton
without a declaration", "resolved from the request scope"). A host's own validator is tested like any
other service; the sample's two live in `tests/Flirty.Tests/Samples/WebSampleTests.cs`. For the designer
descriptors (#137): `Designer/QuestionTypeDescriptorFileTests.cs`, `Designer/DesignerQuestionTypesTests.cs`
and `Designer/DesignerAppQuestionTypesTests.cs`.

## Definition of Done

English XML docs · `docs/VALIDATION.md` (type table or rule table) updated · tests green. If the domain
model/schema changes, additionally run the `flirty-ef-migration` skill. For **path A**, if the type should
be visible in the designer, the descriptor entry from step 5. For **path B** additionally: the
designer, sample and MCP-prose sites listed above, and the golden test
`QuestionTypeLabelsTests.Every_question_type_has_a_label_of_its_own` staying green – it is the one guard
that turns the cheapest silent omission into a failure.

## Verification

```pwsh
dotnet build Flirty.sln
dotnet test tests/Flirty.Tests
```
