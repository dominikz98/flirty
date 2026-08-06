# ADR 0011 – Custom question types hang off one open base type, and the seam is created in DI

- **Status:** Accepted
- **Context issue:** #136 – EPIC 14, custom question types declared by the host
- **Affected:** `src/Flirty` (`Domain`, `Validation`, `DependencyInjection`, `Runtime/Admin`, `Persistence`)

## Context

A host that embeds Flirty could not define its own question type. `QuestionType` is a closed `public
enum` with fixed ordinals 0–5, persisted as `int`, and it ships inside the published NuGet package.
Whoever needed a colour picker, a composite input or a rating slider had three options: fork the repo,
replace `IAnswerValidator` wholesale and reimplement all six built-in types by hand, or disguise the type
as `FreeText` and guess the control from the question `Key` by convention.

**The finding that shapes the decision is a measurement of the built-in path.** The skill
`flirty-question-type` promised that a new question type is three steps: append the enum value, add a
`case` in `AnswerValidator`, return a result instead of throwing. Measured, `QuestionType.<Member>`
appears **98 times across 15 files** in `src` alone — the designer's codec, form model, labels,
expression prediction, input control, palette and graph builder, the MCP tool schemas, and a
hand-maintained copy of the enum in JavaScript. The three steps are what the **core** costs; the other
twelve files are what a type costs to become *usable*.

That is the constraint. An extension point has to be **cheaper** than the built-in path, not more
expensive. So the enum must not be widened once per future type.

Two further constraints come from decisions already recorded. A published dialog version is immutable
([ADR 0005](./0005-immutable-published-dialog-version.md)), so anything that can fail at *submit* time on
a published dialog is unrepairable — the author cannot edit the graph to fix it. And `AnswerValidator` is
`sealed` with only `private static` helpers and is registered as a **singleton** documented as stateless,
while a host validator plainly needs services: an `HttpClient` for an external check, options, or the
scoped `FlirtyDbContext` for "does this SKU exist?".

## Decision

**One new built-in type with an open shape, `QuestionType.Json = 6`, and every host-declared type maps
onto it** — selected by a new nullable column `Question.CustomTypeKey`. A colour input is
`Type = Json, CustomTypeKey = "color"`. `Json` **without** a key is a valid question in its own right: an
answer as an arbitrary JSON document. Existing ordinals are untouched; the member is appended.

**The load-bearing invariant: ignoring the key is always safe.** If a registration is absent, `Json`
validates well-formedness — the most permissive meaningful check — and logs one warning. `"#ff0000"` is a
valid JSON string, so it passes and binds as a string. Nothing throws, because a throw here would be an
error on a published dialog that nobody can repair.

**The seam is created in DI, not in the class.** `AnswerValidator` stays untouched and keeps its seven
built-in arms as the single owner of the built-in types. A scoped `CustomQuestionTypeAnswerValidator`
decorates `IAnswerValidator`, resolves the key against `FlirtyQuestionTypeRegistry` and calls the
registered `IQuestionTypeValidator` **out of the request scope** — so a host validator shares the
`FlirtyDbContext` with the handler. The decorator is registered **only if at least one type was
declared**, gating by absence as `AllowMigrations()` and the webhook list already do. A host that does not
use the feature therefore gets **no lifetime change at all**; `IAnswerValidator` stays exactly the
singleton it was.

**Structure runs before semantics.** The decorator calls the inner validator first and dispatches only if
it passed, so a host validator is never handed a value the built-in check already refused.

**The authoring guard is one rule:** `CustomTypeKey` set ⇒ `Type == Json`, enforced on create and update
via `IValidatableObject` (→ 400). Whether the key is actually *declared* is deliberately **not** checked:
an undeclared key is not an error, and the registry belongs to the host process — a second consumer of the
same database may well have declared it.

## Discarded alternatives

- **`Custom = 6` as a sentinel instead of `Json`.** The obvious spelling, and worse in one specific way:
  a `Custom` without a registered key is nonsense and has to throw at submit — on a published dialog, an
  unrepairable failure. A `Json` without a key is a feature. That is also what makes the first stage
  shippable and testable without a single custom type existing.
- **A base type per descriptor (`color` on `FreeText`).** One degree of freedom too many: the descriptor
  would have to carry a `BaseType`, the authoring-time check would have to compare it, and two hosts could
  disagree about what the base of `color` is. With `Json` the guard is trivial and total.
- **`FreeText` as the base.** It would be a lie: `Type` would claim a shape that does not hold, and the
  expression path would bind a composite answer as a string.
- **Widening the enum per type** (`Color = 6`, `Address = 7`, …). Impossible for a *consumer* of a
  published package, and measured at ~15 files per type even for us.
- **`o.UseAnswerValidator<T>()`, replacing the whole validator.** Still costs the caller all seven
  built-in types, which is the very trap this EPIC exists to remove.
- **A second dispatch point in `AnswerValidationPipelineBehavior`**, which already holds the scope and
  already loads the question. It would make `IAnswerValidator` *half* a truth and give "validate this
  answer" two homes — the shape #118 argued against: a list that enumerates instead of deriving. This way
  `IAnswerValidator` stays the single entry point, including for a host that calls it directly.
- **A `Schema` field in `ValidationRules` validated against JSON Schema.** Scoped in by an amendment to
  the issue on the premise that `JsonSchema.Net` is MIT. Measured against the published nuspecs, that
  holds only up to **8.0.5**: from **9.0.0** the binary release carries the Open Source Maintenance Fee
  EULA with `requireLicenseAcceptance=true` and a fee obligation above roughly US$10,000 annual revenue.
  Because `Flirty` is a published package, that obligation would flow transitively to **every** consumer,
  including one that never authors a `Json` question. The three ways around it are all more expensive
  than the feature: freeze on 8.0.5 with no upstream fixes and no exit if an advisory lands; a fourth
  opt-in package, which downgrades the capability to opt-in anyway; or pass the fee on silently. So
  structure stays where the issue originally put it — **in the custom type's own validator**, which is
  code the host already owns and can express anything in.
- **`StringComparer.OrdinalIgnoreCase` for the key**, copied from `FlirtyMcpOptions.AddTarget`. That
  comparer is there *because route values are case-insensitive*, a reason which does not exist here. The
  key is a persisted column value whose collation the engine does not control; ordinal is the one
  behaviour identical on SQLite, PostgreSQL and SQL Server. Since the declaration charset forbids
  uppercase, no two declared keys can differ only by case, so nothing is lost.
- **Descriptors in the database instead of in code.** It would remove the host/designer duplication, but
  it is a second schema change and turns the declaration from code into data — and a validator is code
  regardless. Weighed in #137.

## Consequences

**Positive.** A host declares a type in one call and implements one interface; nothing else in the engine,
the HTTP surface or the MCP surface needs to know it exists. The expression path needed **no** change at
all — `ConvertElement` already derives CLR types from the JSON shape, so an object binds as a
`Dictionary` and an array as a `List`. A host that ignores the feature pays nothing, not even a lifetime
change. Because an unknown key degrades rather than throws, two consumers of one database may declare
different subsets.

**Negative.** `IQuestionTypeValidator`, `FlirtyQuestionType` and `FlirtyQuestionTypeRegistry` are three
new **public** surfaces of a shipped package — the registry has to be public because `Flirty.Mcp` reads
it, and `Flirty` exposes internals only to `Flirty.Tests`. They can never change shape again. With a
declaration present, `IAnswerValidator` becomes **scoped**, which invalidates the flat sentence "the
validator is a stateless singleton" that the guides and the skill carried; both now state the condition.
And the designer deliberately offers no input control for such a question, which is a documented limit
rather than a mechanism (#137).

**Open.** A dialog author reading a field out of a JSON object must write
`address["city"] as string == "Berlin"` or `address["city"].Equals("Berlin")`. The indexer is statically
`object`, so the naive `==` is a **reference** comparison that compiles cleanly and silently evaluates to
`false`. This is ordinary C# semantics, which is exactly why nothing can warn about it; it is pinned by a
test and stated in [BRANCHING-EXPRESSIONS.md](../BRANCHING-EXPRESSIONS.md).

Details: [VALIDATION.md](../VALIDATION.md).
