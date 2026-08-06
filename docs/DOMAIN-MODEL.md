# Flirty – Domain model

> Reference for the POCO entities and enums in the core project `Flirty` (namespace `Flirty.Domain`).
> These types are the basis of persistence; the EF Core configuration (keys, indexes,
> JSON columns, relationships) happens in the `FlirtyDbContext` – see
> [Persistence configuration](#persistence-configuration-flirtydbcontext). Conceptual overview:
> [ARCHITECTURE.md](./ARCHITECTURE.md) §5 (configuration) and §6 (runtime/session state).

## Aggregates & navigations

The model has two aggregates with clear boundaries:

- **Configuration aggregate** – root `Dialog`. The dialog bundles its `Question`s,
  `Transition`s, `LoopDefinition`s, `TriggerDefinition`s and its `DialogLayout` rows; a
  `Question` bundles its `AnswerOption`s. Navigations mirror this tree (`Dialog.Questions`,
  `Question.Options`, …).
- **Runtime aggregate** – root `DialogSession`. The session bundles its `SessionAnswer`s
  (`DialogSession.Answers`).

**Deliberately without a navigation** – left as a scalar `Guid` to avoid ambiguous relationships or
ones that cross aggregate boundaries (explicit configuration happens in the DbContext where needed):

- Multiple references to the same entity: `Transition.FromQuestionId`/`TargetQuestionId`,
  `LoopDefinition.EntryQuestionId`/`BreakingQuestionId`, `TriggerDefinition.QuestionId`,
  `Dialog.StartQuestionId`, `DialogSession.CurrentQuestionId`.
- **Polymorphic reference**: `DialogLayout.ElementId` points at different entities per `ElementKind` and
  therefore cannot be a foreign key at all.
- **Runtime → configuration**: `DialogSession.DialogId` and `SessionAnswer.QuestionId`. Sessions pin,
  via `DialogId`/`DialogVersion`, exactly the version they started on and run to completion there
  (ARCHITECTURE.md §11.4). This is upheld by the immutability of published versions:
  graph changes are locked there, and evolution happens via a **new version**
  (`CreateDialogVersionCommand`) – see [RUNTIME.md § Version pinning](./RUNTIME.md#version-pinning)
  and [ADR 0005](./adr/0005-immutable-published-dialog-version.md). **Deleting** the
  version, by contrast, still breaks its sessions; that is why `DeleteDialogCommand` refuses it as long as
  sessions are running.

## Enums

| Enum | Values (ordinal) |
|---|---|
| `QuestionType` | SingleChoice(0), MultiChoice(1), FreeText(2), Number(3), Date(4), Boolean(5), Json(6) |
| `TriggerScope` | OnDialogStarted(0), AfterAnswer(1), AfterQuestion(2), OnDialogCompleted(3) |
| `TriggerKind` | InProcess(0), Webhook(1) |
| `LayoutElementKind` | Question(0) |
| `SessionStatus` | InProgress(0), Completed(1), Abandoned(2) |

## Configuration entities

| Entity | Properties | Navigations |
|---|---|---|
| `Dialog` | `Id`, `Key`, `Name`, `Description?`, `Version`, `IsPublished`, `StartQuestionId?`, `CreatedAt`, `UpdatedAt` | `Questions`, `Transitions`, `Loops`, `Triggers`, `Layout` |
| `Question` | `Id`, `DialogId`, `Key`, `Text`, `Type`, `CustomTypeKey?`, `Order`, `IsRequired`, `ValidationRules?` (JSON) | `Dialog`, `Options` |
| `AnswerOption` | `Id`, `QuestionId`, `Key`, `Label`, `Value`, `Order` | `Question` |
| `Transition` | `Id`, `DialogId`, `FromQuestionId`, `Expression?`, `TargetQuestionId`, `Priority`, `IsDefault` | `Dialog` |
| `LoopDefinition` | `Id`, `DialogId`, `CollectionKey`, `EntryQuestionId`, `BreakingQuestionId` | `Dialog` |
| `TriggerDefinition` | `Id`, `DialogId`, `Scope`, `QuestionId?`, `Kind`, `Config` (JSON), `Expression?` | `Dialog` |
| `DialogLayout` | `Id`, `DialogId`, `ElementKind`, `ElementId`, `X`, `Y` | `Dialog` |

`DialogLayout` (#102) holds the position an author chose for an element on the designer's graph canvas –
**pure display data** that the runtime never reads. Without a row, the auto-layout arranges it there;
that is at the same time the way back (`ResetDialogLayoutCommand` simply deletes the rows). The table
exists instead of two columns on `Question` because it keeps the graph entities free of display concerns
**and** because its write path thereby lies structurally outside the publish lock – rationale together with
the discarded alternatives in [ADR 0007](./adr/0007-layout-as-its-own-table.md).

## Runtime entities

| Entity | Properties | Navigations |
|---|---|---|
| `DialogSession` | `Id`, `DialogId`, `DialogVersion`, `ExternalUserKey`, `Status`, `CurrentQuestionId?`, `StartedAt`, `CompletedAt?` | `Answers` |
| `SessionAnswer` | `Id`, `SessionId`, `QuestionId`, `Value` (JSON), `AnsweredAt`, `Sequence`, `LoopInstanceId?`, `IterationIndex?` | `Session` |

`LoopInstanceId`/`IterationIndex` allow several answers per `QuestionId` (one entry per
loop iteration); outside a loop both are `null`.

## Conventions

- Ids: `Guid`. Timestamps: `DateTimeOffset`. Enum storage mapping happens in the DbContext.
- Mandatory strings as `required string`, optional ones as `string?`.
- Navigation collections initialized (`= []`), back references as `= null!` (set by EF).
- All types `sealed`; English XML docs on all public members (CS1591 = build error).

## Persistence configuration (`FlirtyDbContext`)

The `FlirtyDbContext` (namespace `Flirty.Persistence`, folder `src/Flirty/Persistence/`) is
**provider-agnostic**: it has only the options constructor
`FlirtyDbContext(DbContextOptions<FlirtyDbContext>)` and sets no provider. The
provider choice (SQLite/PostgreSQL/SQL Server) and the migrations per provider are implemented in issue #19
– details in [PERSISTENCE.md](./PERSISTENCE.md) (cf. [ARCHITECTURE.md](./ARCHITECTURE.md) §8).

- **DbSets only for the aggregate roots** – `Dialogs` and `DialogSessions`. The child entities are
  reached via their navigations or `Set<T>()` (mirrors the aggregate boundaries).
- **Fluent API configuration** – one `internal sealed`
  `IEntityTypeConfiguration<T>` class per entity under `Persistence/Configurations/`; applied via
  `ApplyConfigurationsFromAssembly`. The POCOs stay free of data annotations.
- **Enum storage as `int`** – `QuestionType`, `TriggerScope`, `TriggerKind`, `LayoutElementKind` and
  `SessionStatus` are mapped explicitly via `HasConversion<int>()` (guard matching the ordinal pinning
  of the domain tests).
- **JSON columns = plain text columns** – `SessionAnswer.Value`, `TriggerDefinition.Config`
  (mandatory) and `Question.ValidationRules` (optional) carry application-side serialized JSON and
  are stored as unbounded text columns (without `MaxLength`). Provider-native `json`/`jsonb`
  types are deliberately not set (the lowest common denominator of all providers; confirmed in #19).
  The schema of `Question.ValidationRules` (camelCase fields `minLength`/`maxLength`/`min`/`max`/
  `pattern`, type-scoped) has been evaluated since #30 by the `IAnswerValidator` – see [VALIDATION.md](./VALIDATION.md).
  The schema of `TriggerDefinition.Config` has been described since #42 by the public type
  `Flirty.Domain.TriggerConfig` (camelCase fields `url`/`name`; `url` is mandatory for `Kind = Webhook` and
  an absolute http/https address) – see [TRIGGERS.md](./TRIGGERS.md#trigger-definitions-on-the-dialog-42).
- **`Question.CustomTypeKey` is a bounded text column without an index** (#136). It names a question
  type the *host* declared with `o.AddQuestionType(...)` and is deliberately **not** a foreign key: that
  registry lives in host code, not in the database. So an unknown key is neither a dangling reference
  nor an error – the answer is then validated as plain JSON. It is capped at
  `PersistenceConstants.KeyMaxLength` like the other business keys, and carries no index of its own,
  because nothing queries by it (the lookup is the in-memory registry) and a unique index over a
  nullable column would behave differently on SQL Server than on SQLite/PostgreSQL. Only ever set
  together with `QuestionType.Json`, enforced by the admin commands – see
  [ADR 0011](./adr/0011-custom-question-types-on-an-open-base-type.md).
- **Scalar `Guid` references without a foreign key** – the references listed above under *Deliberately
  without a navigation* stay plain columns (no relationship, no shadow FK).
- **Cascading delete** – within both aggregates (`Dialog` → Questions/Options/Transitions/
  Loops/Triggers/Layout; `DialogSession` → Answers) via `OnDelete(Cascade)` with explicit
  `HasForeignKey`.

### Keys & indexes

| Entity | Key / index | Kind |
|---|---|---|
| `Dialog` | PK `Id`; `(Key, Version)` | **unique** (multiple versions per `Key` allowed) |
| `Question` | PK `Id`; `(DialogId, Key)` | **unique** |
| `AnswerOption` | PK `Id`; `(QuestionId, Key)` | **unique** |
| `Transition` | PK `Id`; `(DialogId, FromQuestionId, Priority)` | not unique (evaluation order) |
| `LoopDefinition` | PK `Id` | – |
| `TriggerDefinition` | PK `Id` | – |
| `DialogLayout` | PK `Id`; `(DialogId, ElementKind, ElementId)` | **unique** (one position per element) |
| `DialogSession` | PK `Id`; `(DialogId, ExternalUserKey)` | not unique (multiple sessions per user) |
| `SessionAnswer` | PK `Id`; `(SessionId, Sequence)` | not unique |

Indexed business keys (`Dialog.Key`, `Question.Key`, `AnswerOption.Key`,
`DialogSession.ExternalUserKey`) are limited to 256 characters so they stay indexable across all
providers. **No** unique index over `SessionAnswer(SessionId, QuestionId)`:
loop iterations allow several answers per question. Unique indexes are generally not placed over
`null`-able columns (divergent null semantics between SQL Server and SQLite/PostgreSQL).

> **Store timestamps UTC-normalized.** The PostgreSQL provider (Npgsql) maps
> `DateTimeOffset` to `timestamptz` and requires offset == UTC. Store timestamps as UTC accordingly.
