# ADR 0012 – The designer declares question-type descriptors at startup, and the semantic delta stays open

- **Status:** Accepted
- **Context issue:** #137 – Designer support for host-declared question types
- **Affected:** `src/Flirty.Designer` (`DesignerApp`, `Models`, `Services`, the test runner)

## Context

[ADR 0011](./0011-custom-question-types-on-an-open-base-type.md) let a host declare its own question type
with `o.AddQuestionType(...)` and an `IQuestionTypeValidator`. It left the designer with two limits, both
recorded as deliberate: a custom type shows its **raw key** instead of a display name, and such a question
is **not answerable** in the test runner at all. Both come from one fact — the designer is a separate
process that talks to the database, while the registry lives in the host's DI container.

**The issue's premise about the first half was overtaken by ADR 0011 itself, and measuring that changed the
whole shape of this decision.** #137 was written expecting a JSON Schema in `Question.ValidationRules`,
validated by an `IJsonSchemaValidator` the designer would also have. Neither exists: schema validation was
dropped when `JsonSchema.Net`'s licence was measured (MIT only up to 8.0.5), and `grep JsonSchema` over the
repository finds it exclusively as a *discarded alternative*. So the split #137 assumed — "structure the
designer can check, semantics it cannot" — does not exist. The real table is shorter:

| Concern | Lives in | Does the designer have it? |
|---|---|---|
| Well-formed JSON | `AnswerValidator.ValidateJson`, core | **Yes** — the designer runs the real engine |
| Semantics (`IQuestionTypeValidator`) | host code, host DI | No |
| Display name, sample answer | `FlirtyQuestionType`, host DI | No — until this decision |

That collapse sharpens the outcome rather than weakening it: **the delta is exactly the host validator, and
for a `Json` question without a key it is empty.** There, a designer test run validates *identically* to
production — a statement neither the issue nor ADR 0011 could make.

Two constraints carry over. A test run writes a **real** session and delivers real webhooks, so nothing may
be guessed. And the designer's own `AddFlirty()` is called once at startup without a provider, while the
connection profile is chosen per Blazor circuit.

## Decision

**The designer becomes a host in the sense of ADR 0011.** An optional `question-types.json` in the content
root — beside `connection-profiles.json`, gitignored for the same reason — is read during
`DesignerApp.ConfigureServices` and declared through the real `o.AddQuestionType(key, displayName, sample)`.
Everything downstream then reads the ordinary `FlirtyQuestionTypeRegistry`. No core change, no schema
change, no new public API, and it works with no host running.

**The declarations deliberately carry no validator.** `AddQuestionType` without one is a legitimate
declaration (ADR 0011), and a validator is code that lives in the host process. So the descriptors buy a
display name and a sample, and nothing else.

**The core stays the authority on validity.** The designer does not re-check the key charset, the sample
JSON or uniqueness; it calls `AddQuestionType` per entry and catches the `ArgumentException`. A bad entry is
skipped and reported, never thrown — the call site is `ConfigureServices`, where an exception is a designer
that will not start, over a display name. What was skipped is shown on the read-only `/question-types` page,
because a silently dropped entry is indistinguishable from one never written.

**A `Json` question is answerable in the test runner, through a raw JSON field** prefilled from the
descriptor's sample. Well-formedness is shown beside it as **advice** and gates nothing: the submit button
stays enabled on malformed input, so the refusal comes from the engine's own `AnswerValidator` — the message
a host application would produce. `QuestionTypeLabels.IsAnswerableInDesigner` is gone; a predicate that is
constantly true is the enumerating-instead-of-deriving shape #118 argued against. `AnswerInputModel.CanSubmit`
remains the single hard guard.

**Does a designer test run validate identically to the host?** *No for any declared key, yes for `Json`
without one.* The runner says so on screen, beside the control, **whenever a key is present — including when
it resolves**. A note only for unknown keys would tell an author that a known type is fully checked here,
which is the false impression ADR 0011 refused to create.

## Discarded alternatives

- **Descriptors per connection profile** instead of one set per designer. Better scoping in theory: two
  profiles may belong to two applications. But it needs its own store, its own editing UI and a second
  concept in the profile format, and it buys only cosmetics — the delta is unchanged either way. Its real
  cost lands in a file format that is expensive to revise. If a designer is ever pointed at two unrelated
  applications, revisit this; today the profiles of a project are dev/staging/production of *one*.
- **Descriptors in the database** (#137's route B). One source of truth for host and designer, but a second
  schema change plus three migrations, and it turns the declaration from code into data while a validator
  stays code regardless. ADR 0011 already parked "descriptors as a general mechanism" as its own EPIC.
- **Asking a running host over HTTP** (#137's route C): an HTTP twin of `flirty_question_type_list` plus a
  base URL per connection profile. The issue calls this "the only route that closes the semantic delta". It
  does not: a *list* endpoint returns descriptors, exactly what a file returns. It buys no capability over a
  file, and costs a new public endpoint on a published package — permanent — plus a reachable host, plus the
  file as a fallback anyway.
- **Closing the delta for real** — route C plus a `POST .../question-types/{key}/validate` and a proxy
  `IQuestionTypeValidator` registered in the designer. This is the only design that would work, and it is
  much larger than it looks: two new public endpoints on a shipped package, and a **per-profile** registry,
  because `FlirtyQuestionTypeRegistry` is a startup singleton with an `internal` constructor while the
  profile is chosen per circuit. And it still degrades to the stated delta whenever no host is reachable, so
  the note has to exist regardless. Weighed against a delta that is one sentence on screen, it does not earn
  itself.
- **Generating a form from the type.** Nothing to generate from: there is no schema, and if there were, JSON
  Schema expresses far more than a form can render (`oneOf`, `if`/`then`, `$ref`, conditional `required`). A
  generator would silently honour a subset and the author could not tell which parts were dropped.
- **Validating well-formedness as a submit gate** rather than as advice. It reads as helpful and is a
  regression: the designer would author the refusal message, and a test run would stop showing what a host
  application shows. The whole value of the runner is that the engine answers.
- **A second validation implementation in the designer.** A second truth that would drift, and #137 rules it
  out explicitly.
- **Loading host assemblies to get the real validator.** Plugin loading: version coupling, an untrusted-code
  surface and `AssemblyLoadContext` lifetime problems — for a capability the route above reaches over HTTP.

## Consequences

**Positive.** No core code, no schema change, no new public API and no new project; the feature is one
optional file and reuses the seam ADR 0011 already built, which is also why the designer's knowledge is the
*same* `FlirtyQuestionTypeRegistry` a host has rather than a parallel model. It works offline, and with no
file at all the designer behaves exactly as it did after #136 — the fallback arms of `Describe` and
`Choices` are the unchanged #136 code, so that invariant is structural rather than promised.

**Negative.** A type is declared twice: as code in the host, as data in the designer, with nothing keeping
them in step — a renamed display name simply goes stale. There is **one** descriptor set for all connection
profiles, which is wrong the moment two profiles belong to two applications. And as soon as one descriptor
exists, `IAnswerValidator` becomes **scoped** in the designer, because that is what registering the
decorator does (ADR 0011); harmless here, since the gateways already open a fresh scope per operation, but
it is a lifetime change in a Blazor Server app and worth knowing.

**Open.** The semantic delta is not closed and this decision does not close it. What it does is make the
delta *statable*: exactly one thing is missing, it is named on screen where the answer is given, and the one
case with no delta at all (`Json` without a key) is named too. Should closing it ever become worth the two
endpoints, this ADR is superseded rather than amended — the descriptor file would then be the offline
fallback it already is.

Details: [DESIGNER.md](../DESIGNER.md), [VALIDATION.md](../VALIDATION.md).
