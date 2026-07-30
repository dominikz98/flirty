# Designer (Blazor)

The **Flirty.Designer** is a Blazor Web App (server-interactive, .NET 10) for creating and editing
dialogs and for managing the database connections. It is part of **EPIC 7** (issues #37–#43,
milestone "M3 – Designer"; the Playwright E2E of the UI came with #46 in M4). Reference:
[ARCHITECTURE.md](./ARCHITECTURE.md) §4/§8, [PERSISTENCE.md](./PERSISTENCE.md).

> **Status:** EPIC 7 is implemented: **connection-profile management (multi-DB, #37)**, **dialog CRUD
> (#38)**, **question editor (#39)**, **branching editor (#40)**, **loop editor (#41)**,
> **trigger editor (#42)** and **test runner (#43)**; the UI is covered by Playwright E2E since **#46**.
> From **EPIC 11** (visual graph designer, #99) the **graph view (#101)**, the
> **layout persistence (#102)** and the **editing on the canvas (#103)** have been added – the canvas is
> thereby an editor, the form and list path remain equally available. The designer works through
> the engine's commands (via `ISender`), not directly past the `FlirtyDbContext`.

## Starting

```pwsh
dotnet run --project src/Flirty.Designer
```

Default ports: `http://localhost:5016` / `https://localhost:7173` (`Properties/launchSettings.json`).
The entry point is the start page; via the navigation you reach **Connections** (`/connections`) and
**Dialogs** (`/dialogs`).

## Connection-profile management (multi-DB, #37)

The designer can work against **multiple databases**. A *connection profile* bundles a
provider (`FlirtyDatabaseProvider`: SQLite / PostgreSQL / SQL Server) and the connection string.
On the **Connections** page (`/connections`) profiles can be:

- **created/edited/deleted** (name, provider selection, connection string),
- **tested** ("Test" → `Database.CanConnectAsync()`),
- **migrated** ("Migrate" → applies pending migrations via `Database.MigrateAsync()` and reports
  which ones were applied),
- **activated** – the active profile determines which database the designer (and, from #38, the
  admin commands) works against,
- **deleted** – confirmed inline in two stages, like all of the designer's delete actions. If the **active**
  profile is deleted, `ActiveConnectionProfile.Clear()` releases it in the running circuit too; without this
  step the designer would keep working, until the next full reload, against a profile that no longer exists
  in the management.

> **SQLite note:** "Test" only reports success once the file exists. With a fresh
> SQLite profile therefore **migrate** first (creates the file + schema), then test.

### Where the profiles are stored (security note)

Profiles are stored as **plaintext JSON** in `connection-profiles.json` in the designer's ContentRoot
(storage outside the Flirty database, because the profiles are what first establish the connection to it).
The file can contain **secrets** (passwords in connection strings) and is therefore excluded via
`.gitignore`. For a local developer tool this is deliberately kept simple – if
the designer is operated in a shared environment, a more secure store (user secrets, KeyVault
or similar) should be provided.

## Architecture of the profile selection

The core (`Flirty`) stays provider-agnostic. For the runtime selection it provides two
public building blocks since #37 (see [PERSISTENCE.md → Choosing the provider as a value](./PERSISTENCE.md#selecting-the-provider-as-a-value-37)):

- `FlirtyDatabaseProvider` (enum) and
- `DbContextOptionsBuilder.UseFlirtyProvider(provider, connectionString)` – sets provider **and**
  the matching `MigrationsAssembly` in one step.

On top of that the designer builds (`src/Flirty.Designer/`):

| Building block | Path | Task |
|---|---|---|
| `ConnectionProfile` | `Models/ConnectionProfile.cs` | Profile model (Id, Name, Provider, ConnectionString). |
| `IConnectionProfileStore` / `JsonConnectionProfileStore` | `Services/` | CRUD + default profile, persisted as JSON. |
| `ActiveConnectionProfile` | `Services/ActiveConnectionProfile.cs` | Holds the active profile (scoped = per circuit). |
| `FlirtyDesignerDbContextFactory` | `Services/` | `IDbContextFactory<FlirtyDbContext>` against the **active** profile. |
| `ConnectionProfileOperations` | `Services/` | Test-connection / migration status / migrate for an **arbitrary** profile. |
| `ConnectionProfileContextBuilder` | `Services/` | Builds a `FlirtyDbContext` from a profile via `UseFlirtyProvider`. |
| Page `ConnectionProfiles.razor` | `Components/Pages/` | UI (`/connections`), server-interactive. |
| `FlirtyAdminGateway` | `Services/` | Runs the admin commands per operation in a fresh DI scope (#38). |
| `FlirtyRuntimeGateway` | `Services/` | The same for the runtime operations of the test runner (#43). |

### DI wiring (`DesignerApp`)

The entire composition lives in `src/Flirty.Designer/DesignerApp.cs`
(`ConfigureServices(WebApplicationBuilder)` + `Configure(WebApplication)`); `Program.cs` only calls
both. The reason for the extraction is the Playwright E2E (#46), which hosts the same setup in-process –
the same pattern as `WebSampleApp` in `Flirty.Samples.Web`.

The designer calls **`AddFlirty()` without a provider** (engine/admin/mediator, but no fixed
`FlirtyDbContext`). Instead the context is created per active profile via the factory:

```csharp
builder.Services.AddFlirty();                                   // Engine without a hard-wired provider

builder.Services.AddSingleton<IConnectionProfileStore>(sp => new JsonConnectionProfileStore(
    Path.Combine(sp.GetRequiredService<IWebHostEnvironment>().ContentRootPath, "connection-profiles.json")));
builder.Services.AddSingleton<ConnectionProfileOperations>();
builder.Services.AddScoped<ActiveConnectionProfile>();
builder.Services.AddScoped<IDbContextFactory<FlirtyDbContext>, FlirtyDesignerDbContextFactory>();
builder.Services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<FlirtyDbContext>>().CreateDbContext());
builder.Services.AddScoped<FlirtyAdminGateway>();               // Admin CRUD, #38

builder.Services.AddScoped<DesignerTriggerLog>();               // Test runner, #43
builder.Services.AddScoped<FlirtyRuntimeGateway>();
builder.Services
    .AddFlirtyHandler<DialogStartedNotification, DesignerTriggerLogHandlers.DialogStarted>()
    .AddFlirtyHandler<AnswerSubmittedNotification, DesignerTriggerLogHandlers.AnswerSubmitted>()
    .AddFlirtyHandler<QuestionAnsweredNotification, DesignerTriggerLogHandlers.QuestionAnswered>()
    .AddFlirtyHandler<DialogCompletedNotification, DesignerTriggerLogHandlers.DialogCompleted>();
```

The second-to-last line binds the (scoped) `FlirtyDbContext` to the active profile – so the
admin commands automatically run against the chosen database. If **no** profile is active, the
factory throws an understandable `InvalidOperationException`.

### Referencing the migration assemblies

`Flirty.Designer.csproj` references **all three** `Flirty.Migrations.{Sqlite,PostgreSql,SqlServer}`.
With a `ProjectReference` the NuGet bundling of the migration DLLs does not take effect (see
[PERSISTENCE.md](./PERSISTENCE.md)), so they must be referenced explicitly so that "Migrate"
works for every provider.

## Dialog CRUD (#38)

Two pages, both server-interactive:

| Route | Component | Content |
|---|---|---|
| `/dialogs` | `Components/Pages/Dialogs.razor` | List (key, name, version, status, entry question, changed) + inline form "New dialog" + text filter over key/name. |
| `/dialogs/{id:guid}` | `Components/Pages/DialogEditor.razor` | Edit metadata, choose entry question, publish/unpublish, delete. |

> The detail page is deliberately called **`DialogEditor`** and not `DialogDetail`: the generated
> component type would otherwise shadow the identically named view type `Flirty.Runtime.Admin.DialogDetail`.

Both pages use exclusively the engine's admin commands
(`CreateDialogCommand`, `UpdateDialogCommand`, `DeleteDialogCommand`, `PublishDialogCommand`,
`UnpublishDialogCommand`, `ListDialogsQuery`, `GetDialogQuery` from `src/Flirty/Runtime/Admin/`).
The form model `Models/DialogFormModel.cs` mirrors their `[Required]` annotations, so the
`DataAnnotationsValidator` reports violations already in the browser.

Rules that the UI makes visible:

- A new dialog is created as a **draft** (`Version = 1`, `IsPublished = false`, without an entry question).
- **Publish** is disabled as long as no entry question is set *and saved* –
  `PublishDialogCommand` would otherwise abort with `InvalidOperationException`.
- If the graph has **open warnings**, the "Publication & deletion" section repeats them and asks
  back before publishing. Reason: once published the graph is locked – a configuration mistake that
  slipped through (say a conditional transition without a default) then costs a new version, while
  running sessions already run into the runtime's 409 (#97). Meant are **all** warning kinds:
  transitions, loops, the missing entry question and the **reachability** of the questions. The source is
  therefore `DialogGraphModel.AllWarnings` via `GraphWarningList.Describe` and not a single
  analyzer – until #118 `GraphWarnings()` held only the `TransitionWarningAnalyzer`, an **unreachable**
  question could therefore be published without a confirmation, even though the graph clearly showed it. The defect
  was not the one missing warning, but the hand-picked selection: every further warning kind would have
  fallen out again. The price for that is one `DialogGraphBuilder.Build` per load of the editor – it
  lives in a field (`_graph`), **never** in the markup, otherwise the whole layout would run on every click.
- A **published** dialog is locked: the editors for questions, transitions, loops, triggers
  and the entry question are disabled, a banner names the two ways out (create a new version
  or unpublish). Name and description remain editable. Details below under
  [Versioning](#versioning-95).
- **Delete** asks back in two stages **inline** (no JS `confirm`, which would otherwise block the
  Playwright E2E from #46) and removes the whole graph via a DB cascade.
- The entry-question selection lists the questions from `GetDialogQuery`. As long as there are none, it is
  disabled – questions are created in the **question editor** (next section).

### Why a gateway instead of `@inject ISender`

`FlirtyAdminGateway` (`Services/FlirtyAdminGateway.cs`) runs **every** admin message in its
**own DI scope**:

```csharp
var result = await Admin.ExecuteAsync((sender, token) => sender.Send(new ListDialogsQuery(), token));
if (!result.Success) { _error = result.Error; return; }
```

In Blazor Server a DI scope corresponds to a **circuit**. The scoped `FlirtyDbContext` would thus live
the whole session – it would be pinned to the profile that was active on first access (a later
profile switch would remain ineffective), its change tracker would fill up, and the non-thread-safe context
would be shared by parallel render paths. One scope per operation solves all three points; the active
profile of the circuit is passed into the child scope via `ActiveConnectionProfile.Adopt(...)`.

The gateway returns an `AdminResult<T>` (`Success` / `Value` / `Error`) instead of exceptions, so that an
error produces a message and does not kill the circuit. The mapping mirrors the
`FlirtyExceptionEndpointFilter` from `Flirty.AspNetCore` (not-found → validation → conflict) and adds to
database errors the hint to **migrate** the active profile (typical with a fresh SQLite file).

## Versioning (#95)

A **published** dialog version is immutable – the engine rejects graph changes to it with
`DialogPublishedException` (→ 409), so that running sessions do not break (rationale and discarded
alternatives: [ADR 0005](./adr/0005-immutable-published-dialog-version.md), mechanics:
[RUNTIME.md § version pinning](./RUNTIME.md#version-pinning)). The designer mirrors this rule,
instead of letting the user run into the error message:

- Every page knows a property `Editable` (`_detail is not null && !_detail.Dialog.IsPublished`).
  The mutation buttons of all graph sections hang on it – creating, sorting (↑/↓), deleting,
  saving in the detail editors as well as the selection of the entry question. **Viewing stays possible:**
  "Edit" still navigates into the detail pages, only saving is locked there.
- The banner on the published dialog names the two ways and offers **"Create new version"**
  (`CreateDialogVersionCommand`): that clones the graph as a draft with the next version number and
  switches directly into its editor. From then on you work on the draft – the page change is intentional.
- **Publishing** the new version retires the previously productive one (per key at most
  one version is published). In the dialog list the versions stand as separate rows, sorted by
  key and version.
- The **delete** section shows the number of running sessions (`CountActiveSessionsQuery`) and offers
  "End running sessions" (`AbandonDialogSessionsCommand`, status `Abandoned`, answers remain).
  Without this step the engine refuses the deletion, because the sessions would survive it and afterwards be
  neither resumable nor readable.
- **Exception: the canvas positions.** `SetDialogLayoutCommand`/`ResetDialogLayoutCommand` do not run
  under the lock – a published dialog can therefore be arranged clearly, even though its
  graph is locked. This is not a gap, but the edge of the scope: the positions live
  in their own table, which does not belong to the graph ([ADR 0007](./adr/0007-layout-as-its-own-table.md)).

> **The test runner is not affected by this:** it starts a concrete version via `StartDialogVersionAsync` –
> including a draft. That is exactly what it is there for (#43): to play through a new version *before*
> it is published.

## Question editor (#39)

Questions are maintained in two stages: the **list** hangs in the dialog editor, the **details** of a question
(validation, answer options) have their own page.

| Route | Component | Content |
|---|---|---|
| `/dialogs/{id:guid}` | `DialogEditor.razor`, "Questions" section | Table (position, key, text, type, required, option count, entry badge), inline form "New question", sorting via ↑/↓, deleting with inline confirmation. |
| `/dialogs/{dialogId:guid}/questions/{questionId:guid}` | `QuestionEditor.razor` | Metadata (key, text, type, required), validation rules, answer options, delete question. |

> This page too is deliberately called **`QuestionEditor`** and not `QuestionDetail` – otherwise the
> generated component type would shadow the view type `Flirty.Runtime.Admin.QuestionDetail` (same trap as with
> `DialogEditor`).

Used are exclusively the admin commands `Create/Update/DeleteQuestionCommand` and
`Create/Update/DeleteAnswerOptionCommand` (via `FlirtyAdminGateway`). The `QuestionEditor` loads its
state with **one** `GetDialogQuery`: it delivers questions including options and, on top, the
dialog metadata for the title, the entry badge and the publication hint.

### Order

The ↑/↓ buttons write the **position index** as the new `Order` – not just swap the two values.
This repairs, along the way, duplicate or gapped `Order` values where a swap would remain
ineffective (there is deliberately no unique index on `Order`, only `{DialogId, Key}` is unique).
All affected `UpdateQuestionCommand`s run in **one** `ExecuteAsync` call, thus in the same
DI scope with a shared error path. For answer options the same holds.

### Validation rules

`Question.ValidationRules` is a JSON column; authoritative is the public core type
`Flirty.Validation.ValidationRules` (`minLength`, `maxLength`, `min`, `max`, `pattern`, see
[VALIDATION.md](./VALIDATION.md)). The form model `Models/QuestionFormModel.cs` maps it onto
input fields and uses it directly as the serialization type – the schema is **not** duplicated.

- **Type-scoped:** the engine evaluates lengths/patterns only for `FreeText` and min/max only for `Number`.
  The UI switches accordingly, and only the rules that fit the current type are saved –
  after a type change no ineffective ballast remains in the JSON.
- **Patterns are translated on save** (`new Regex(...)` with the same 250 ms timeout as in the
  `AnswerValidator`). An invalid expression is rejected with a German message, instead of only hitting at
  runtime as an `InvalidOperationException` when validating an answer. Likewise, swapped
  bounds (`MinLength > MaxLength`, `Min > Max`) are caught.
- **If no rules are set**, `null` is saved – no empty `{}` in the column.
- **Raw-JSON fallback:** if the saved JSON contains fields that `ValidationRules` does not know, or if
  it is not a valid JSON object, the editor shows a text field with the raw JSON instead of the individual
  fields (plus a warning). The input is only checked for readability and passed through unchanged – a
  save must not silently discard foreign fields.

### Answer options

The options section appears for `SingleChoice`/`MultiChoice` – and additionally always when there are still
options present, so that after a type change orphaned options stay visible and deletable
(with a hint that they are ineffective). A choice type **without** options is warned about: against an empty
option list no answer is valid. The *value* is saved and validated; the *label* is
pure display text for the host UI.

### Interplay with the dialog editor

- After creating, the view stays in the list (quick capturing of several questions); validation and
  options are maintained afterwards in the question editor.
- Question operations reload the graph, but do **not** overwrite the metadata form –
  otherwise just-typed, unsaved changes there would be lost. Only the selection of the
  entry question is reconciled, if the chosen question fell away on the server side.
- `DeleteQuestionCommand` removes referencing transitions along with it and resets an entry question
  pointing at it; the UI hints at this, and "Publish" then locks again afterwards.

## Branching editor (#40)

Transitions (`Transition`) are maintained in two stages like the questions: the **list** hangs in the dialog editor,
the **condition** of a branch has its own page with live validation.

| Route | Component | Content |
|---|---|---|
| `/dialogs/{id:guid}` | `DialogEditor.razor`, "Transitions (branching)" section | Per source question a table (position, condition, target, default/back-jump badge), warnings, ↑/↓, deleting with inline confirmation, inline form "New transition" (also per group via "+ Transition"). |
| `/dialogs/{dialogId:guid}/transitions/{transitionId:guid}` | `TransitionEditor.razor` | Source/target question, default flag, condition with live validation, snippet inserter, identifier reference, delete. |

> This page too is deliberately called **`TransitionEditor`** – `TransitionDetail` would shadow the identically named
> view type from `Flirty.Runtime.Admin` (same trap as with `DialogEditor`/`QuestionEditor`).

Used are exclusively `Create/Update/DeleteTransitionCommand` (via `FlirtyAdminGateway`); the
state comes from **one** `GetDialogQuery`. The **priority** is not typed directly: ↑/↓ writes
the position index **within the source question** as the new `Priority` (all updates in one
gateway call) – the same pattern as with questions and options. If you switch the source question in the editor,
the transition gets the next free priority of the new group, instead of silently colliding with an existing
transition.

### Live validation via the sample context

The condition is compiled (not executed) on **every input** via `IExpressionEvaluator.Validate(...)`
and its status shown green/red – with a `^` line under the error location when a position is reported. On save,
the same check runs **blockingly**: an invalid expression would otherwise only surface
in a running session (`ExpressionEvaluationException` in the middle of the dialog).

For this, `Services/DesignerExpressionContext.cs` builds a **sample context** – the counterpart to the
core-internal `SessionExpressionContextBuilder`, only without a session:

| Question type | Sample value (raw, as JSON) | Type in the expression |
|---|---|---|
| `FreeText` | `"Text"` | `string` |
| `Number` | `0` | `long` |
| `Boolean` | `true` | `bool` |
| `Date` | `"2026-01-01"` | **`string`** (as at runtime – no comparison with `now` possible) |
| `SingleChoice` | first option value as a JSON string | `string` |
| `MultiChoice` | JSON array of the option values | list (`.Count`, `.Contains`) |

Authoritative are the **types**, not the values: they mirror exactly the deserialization of the
`DynamicExpressoExpressionEvaluator` (see [BRANCHING-EXPRESSIONS.md](./BRANCHING-EXPRESSIONS.md)).
Loop collections are – as by the `LoopResolver` at runtime – **always** bound (before the first
iteration as an empty list), so that `skills.Count > 0` is checkable; for that `GetDialogQuery` delivers the
loop markers as reads since #40 (`DialogDetail.Loops`).

Non-referenceable keys are shown in the reference table as **"not usable"** instead of
silently missing: keys that are not valid identifiers (`vor-name`), and those that
are shadowed by the reserved context variables `now`/`iterationIndex`/`session` (the evaluator
sets them last).

> The error message comes from the expression engine (DynamicExpresso) and is **English**
> ("Unknown identifier 'rolle' (at index 0)"). The designer frames it in German, instead of translating it –
> so it stays consistent with the engine output and survives an engine swap.

### Snippet inserter

The expression stays a text field (no reverse parsing). Below it an inserter of
**variable / operator / value** assembles a snippet and appends it via `&&`/`||`. The offered
operators depend on the value kind (number: `== != > >= < <=`; list: `count >`, `count ==`,
`contains`), and the comparison value is quoted type-correctly. The quoting deliberately does **not** run via
`JsonSerializer`: its `\u00XX` escapes are rejected by the engine's parser ("Invalid character escape
sequence") – only the C# escapes that DynamicExpresso knows are produced.

### Warnings (non-blocking)

The transition list mirrors the rules of the `TransitionResolver` and reports configurations that
take effect differently at runtime than intended:

- **No default and no unconditional transition** → if no condition matches, the session aborts.
- **Multiple defaults** → only the topmost takes effect.
- **Default with a condition** → the condition is not evaluated (the resolver does not check it).
- **Unconditional transition with successors** → it always takes effect, the following ones are never checked.
- **Back-jump** (target does not lie after the source question) → badge; the marker for it is maintained by the
  [loop editor](#loop-editor-41).
- **Question without outgoing transitions** → hint "the dialog ends after this question".
- **Orphaned transitions** (source question no longer exists) are made visible and can be
  deleted. They do not arise via the designer – but the admin API deliberately does not check question references.

## Loop editor (#41)

Loops are **branching + marker**: the transitions form the cycle, the `LoopDefinition` only lays the
metadata layer over it (details: [LOOPS.md](./LOOPS.md)). The designer therefore maintains exclusively the
**marker** – the cycle is created in the branching editor.

| Route | Component | Content |
|---|---|---|
| `/dialogs/{id:guid}` | `DialogEditor.razor`, "Loops" section | Table (collection, entry, breaking, range size, warning badge), deleting with inline confirmation, inline form "New loop" and the suggestions from unmarked back-jumps. |
| `/dialogs/{dialogId:guid}/loops/{loopId:guid}` | `LoopEditor.razor` | Loop block, `CollectionKey`, entry/breaking question, warnings, delete. |

> This page too is deliberately called **`LoopEditor`** – `LoopDetail` would shadow the identically named view type from
> `Flirty.Runtime.Admin` (same trap as with `DialogEditor`/`QuestionEditor`/`TransitionEditor`).

Used are `Create/Update/DeleteLoopCommand` (via `FlirtyAdminGateway`), the state comes from
**one** `GetDialogQuery`. New in #41 are also the REST endpoints
(`POST {prefix}/dialogs/{dialogId}/loops`, `PUT|DELETE .../loops/{loopId}`) and `Loops` in the
`DialogDetailResponse` – until then the markers were only reachable read-only.

The `CollectionKey` must be **unique within the dialog**; the command handler checks that (409 at the REST layer).
Without this check two markers with the same name would silently overwrite each other at runtime – `LoopResolver`
builds the collections into a dictionary, the last-built marker would win.

Question references the admin API deliberately does **not** check (as with `Transition`); the designer instead points
this out. Conversely, since #41 `DeleteQuestionCommand` cleans up referencing markers along with it – like already the
transitions – so that no marker stays on a deleted question.

### Loop block

`Services/LoopAnalyzer.cs` derives the **loop range** from the transition graph and mirrors thereby
the precomputation of the core-internal `LoopResolver`:
`(forward from entry, stop at breaking) ∩ (backward to breaking) ∪ {entry, breaking}`. The resolver itself
is not reusable – it is `internal` and works on a `Dialog` entity with navigations,
while the designer only has `DialogDetail` (the same delineation as `DesignerExpressionContext` ↔
`SessionExpressionContextBuilder`). Against a drift `LoopAnalyzerTests` secures it, by comparing
both implementations on the same graph.

The range questions are shown in dialog order with the badges **entry**/**breaking**; below the
breaking question its transitions stand separated as **↩ back-jump** (target inside the range) and **⇥ exit**
(target outside), each with condition and link into the transition editor.

### Warnings (non-blocking)

| Situation | Why it counts |
|---|---|
| Entry/breaking question does not (any longer) belong to the dialog | The marker points into the void and collects nothing. |
| No back-jump breaking → entry | No cycle arises at all; the next iteration only starts via the **entry question**. |
| **No exit** from the range | Infinite loop – the core warning from #41. |
| **Exit unreachable** | An unconditional non-default back-jump stands before every exit (or the topmost default points back into the range): by the rules of the `TransitionResolver` the back-jump always takes effect. Also an infinite loop. |
| Overlapping loop ranges | The `LoopResolver` already throws in the constructor – **every** session against the dialog aborts. |
| `CollectionKey` shadows a question key, or is not a valid identifier / is reserved | The question or the collection is not referenceable in conditions. The check shares `DesignerExpressionContext.IsBindable`/`IdentifierNote` with the identifier reference of the branching editor. |

### Suggestions from back-jumps

Back-jump transitions without a matching marker are listed by the dialog editor as a hint – without a marker
the runtime **overwrites** the answers of the cycle, instead of collecting them per iteration. A click opens
the creation form pre-filled: entry question = target of the back-jump, breaking question = its
source question, `CollectionKey` = question key plus `_list` (`skill` → `skill_list`, `topping` →
`topping_list`). Deliberately a plain suffix, **not** an `s`-pluralization: the latter produces nonsense
for keys whose stem does not pluralize with `s`, while `_list` reads cleanly for any key (#116). If the
suggestion collides with an existing question/collection key, or is not a valid identifier, the field
stays empty – a silent fallback name would be harder to trace than an empty required field.

## Trigger editor (#42)

Triggers are the **back channels** of a dialog into the host application (details:
[TRIGGERS.md](./TRIGGERS.md)). The designer maintains them as `TriggerDefinition` rows on the dialog; the
engine delivers webhook triggers itself since then – so what is configured is no longer merely documented.

| Route | Component | Content |
|---|---|---|
| `/dialogs/{id:guid}` | `DialogEditor.razor`, "Triggers" section | Table (timing, question, channel, target, condition), deleting with inline confirmation, inline form "New trigger". |
| `/dialogs/{dialogId:guid}/triggers/{triggerId:guid}` | `TriggerEditor.razor` | Timing + question reference, channel + configuration, condition with live validation, delete. |

> This page too is deliberately called **`TriggerEditor`** – `TriggerDetail` would shadow the identically named view type
> from `Flirty.Runtime.Admin` (same trap as with `DialogEditor`/`QuestionEditor`/
> `TransitionEditor`/`LoopEditor`).

Used are `Create/Update/DeleteTriggerCommand` (via `FlirtyAdminGateway`), the state comes from
**one** `GetDialogQuery`. New in #42 are, besides the CRUD, also the REST endpoints
(`POST {prefix}/dialogs/{dialogId}/triggers`, `PUT|DELETE .../triggers/{triggerId}`) and `Triggers` in the
`DialogDetailResponse`. There is **no order** here – `TriggerDefinition` has no
`Order`/`Priority`, all matching triggers fire; the list is only stably sorted (timing, channel,
configuration).

### Configuration (`Config`)

The column's JSON is read and written via the public core type **`Flirty.Domain.TriggerConfig`** –
the same pattern as `ValidationRules` in the question editor (#39), thus **no** schema duplicate in the
designer. `Models/TriggerFormModel.cs` maps the fields onto two inputs:

- **Target URL** (`url`) – only visible for channel *Webhook* and mandatory there; checked on save
  via `TriggerConfig.TryValidate(kind, …)`, thus with the **same** rule as in the command handler.
- **Event name** (`name`) – optional, delivered on delivery as the header `X-Flirty-Trigger`.

If the saved JSON contains unknown fields (or is not an object), the editor switches to a
**raw-JSON field** and passes the text through unchanged – otherwise the save would silently discard foreign
fields (pattern from #39).

### Timing and question reference

The question reference belongs exclusively to `AfterQuestion`: there it is mandatory (only after this question does
the trigger fire), for all other timings it must be empty. Both are enforced by `CreateTriggerCommand`/
`UpdateTriggerCommand` via `IValidatableObject` – the existing `ValidationPipelineBehavior` runs the
check (at the REST layer: HTTP 400). The UI shows the selection accordingly and normalizes the
value (`TriggerFormModel.NormalizedQuestionId()`), instead of relying on the error message.

As with transitions and loops, the admin API does not check the question **reference** itself; conversely, since #42
`DeleteQuestionCommand` cleans up referencing triggers along with it, so that none stays on a deleted question
and never fires again.

### Condition

The condition uses **unchanged** `DesignerExpressionContext` from #40 – `TriggerDefinition.Expression`
runs over the same engine and the same sample context as `Transition.Expression`. Correspondingly there is
here too live checking with caret position, snippet inserter and identifier reference; saved is
**only** a valid expression.

Two hints the editor gives additionally:

- **At dialog start** no answers are present yet. A condition on a question key cannot be evaluated
  at runtime – the error is logged and the trigger does not fire.
- **Channel `InProcess`** delivers nothing: the notification is published anyway, it is handled by
  a handler of the host app (`AddFlirtyHandler<T, THandler>()`). The entry names the intent.

## Test runner (#43)

The test runner plays a dialog through **with the real engine** – reachable via "Play through" in the
dialog editor or directly under `/dialogs/{dialogId}/test` (`DialogTestRunner.razor`). It is the
acceptance feature of EPIC 7: questions, branching, loops and triggers can be tried out with it,
without building a host app.

Since #104 it has **two views of the same run**: the history list described here and the graph
with the taken path (§ [Test run in the graph](#test-run-in-the-graph-104)). Everything under this
heading applies to both – the view only changes what is shown of the run.

### Playing through drafts

The runner starts via the core API **`IFlirtyEngine.StartDialogVersionAsync(dialogId, …)`** (#43,
see [RUNTIME.md](./RUNTIME.md#startdialogversioncommand-43)) instead of via `StartDialogAsync(dialogKey, …)`.
The difference is the whole point: `StartDialogAsync` resolves over the domain key and starts
only **published** dialogs – a draft would not be testable, and "publish briefly to test"
would arm it for real users. Everything from the start on is unchanged: the session pins its
`DialogId`, submit/resume/edit load their dialog version anyway independently of publication.

The only prerequisite is a set (and saved) **entry question**; without it
"Play through" is disabled.

### The run is real

A test run is no simulation – it writes into the database of the active profile and fires triggers.
The runner shows both at the top as a banner:

- A real `DialogSession` arises along with `SessionAnswer` rows. The user key is fresh per run
  and carries the prefix **`designer-test-`** – so test sessions are recognizable in the database
  and a new run begins guaranteed anew, instead of resuming the still-open session of the last run
  (resume). It is **not** cleaned up: the engine deliberately knows no deletion of sessions.
- **Webhook** triggers configured on the dialog are actually delivered via HTTP (since #42, see
  [TRIGGERS.md](./TRIGGERS.md)). So before a test run against productive targets, check the URL.

### History, iterations and editing

After every step the runner reads the state anew via `ResumeDialogAsync` – one source for history,
current question and expression context. The history shows per answer the question key, the readable value
(option **label** instead of the raw value, `true` → "Yes") and – the core of the acceptance criterion – for
loop answers a badge **`Iteration n`**; answers of the same `LoopInstanceId` are set off as a range.

Every row can be **edited** (`EditAnswerAsync`). The iteration index is passed along, so that
within a loop exactly the clicked iteration is hit and not the earliest; the
message names how many downstream answers were discarded in doing so.

### Expression context

The "Expression context" panel shows **what the conditions are currently computing with**: per question the last
given answer, per loop the collected values and the `iterationIndex` – all as raw JSON text,
exactly as in the engine's `ExpressionContext`. This makes it traceable why a transition took effect.

> **Reading `iterationIndex` correctly:** it means the index of the **last given** answer to the open
> question, not the upcoming iteration (semantics of `LoopResolver.ResolveIterationIndex`). Therefore
> it stands only in the context panel and deliberately **not** as "current iteration" on the current question –
> there it would be misleading.

### Trigger log

The "Triggers" panel lists at the top what the engine published during the run (timing/`TriggerScope`, question,
short description), below it the `TriggerDefinition`s configured on the dialog. `InProcess` entries are
explicitly named there as "the engine does not deliver this itself".

### Building blocks

| Building block | Path | Task |
|---|---|---|
| `DesignerGateway` | `Services/DesignerGateway.cs` | Shared base of both gateways: fresh DI scope per operation, `Adopt` pass-through, error mapping (`GatewayResult<T>`). |
| `FlirtyRuntimeGateway` | `Services/FlirtyRuntimeGateway.cs` | Runs the `IFlirtyEngine` calls; extends the mapping with `DialogNotFound`/`SessionNotFound`/`AnswerValidation`. |
| `AnswerValueCodec` | `Services/AnswerValueCodec.cs` | **Single** source of the JSON contract per `QuestionType` (encode, display, read back). |
| `RunExpressionContext` | `Services/RunExpressionContext.cs` | Mirrors the core `SessionExpressionContextBuilder` onto `DialogDetail` + `ResumeDialogResult`. |
| `DesignerTriggerLog` (+ `…Handlers`) | `Services/` | Collects the published notifications; four `INotificationHandler<T>` write into it. |
| `AnswerInputModel`, `AnswerChoice` | `Models/` | Input state and selection option (`public`, because `[Parameter]` of the component). |
| `AnswerInput` | `Components/AnswerInput.razor` | Input field per question type – shared by the current question and the edit mode. |
| Page `DialogTestRunner.razor` | `Components/Pages/` | The page (`/dialogs/{dialogId}/test`). |

Two traps that surfaced during the build and hold when extending:

- **The log must be adopted into the child scope.** Because every engine step runs in a fresh scope,
  the notification handlers are constructed there too. Without `DesignerTriggerLog.Adopt` (pattern
  of `ActiveConnectionProfile.Adopt`) they would write into a throwaway instance, and the panel would stay
  permanently empty.
- **The encoding belongs in exactly one place.** `AnswerValueCodec` is bindingly aligned with the
  core `AnswerValidator`; `DesignerExpressionContext` derives its sample values from it,
  so that expression validation and test run do not drift apart.

## Graph view (#101)

The page `/dialogs/{id}/graph` (`Components/Pages/DialogGraph.razor`) shows the same dialog as a
**graph** instead of as a form stack – linked from the dialog list and from the head of the dialog editor.
Nodes are movable (#102, § layout persistence), since #103 the canvas is also an **editor**
(§ editing on the canvas), and the test runner shows its run on the same picture since #104
(§ test run in the graph). Stages 1–4 of **EPIC 11** (#99); decisions in
[ADR 0006](./adr/0006-canvas-technology-in-the-designer.md) (canvas technology),
[ADR 0007](./adr/0007-layout-as-its-own-table.md) (layout as its own table) and
[ADR 0008](./adr/0008-gestures-on-the-canvas.md) (gestures on the canvas).

The data source is the existing `GetDialogQuery` via the `FlirtyAdminGateway`; writing happens
exclusively via the existing admin commands – positions via
`Set`/`ResetDialogLayoutCommand`, graph changes via the same `Create`/`Update`/`Delete` commands that
the list view also calls. There is no canvas CRUD.

| Building block | Location | Task |
|---|---|---|
| `GraphLayout` | `Services/` | Auto-layout ("Sugiyama-light"), purely geometric – and the incorporation of saved positions. |
| `DialogGraphBuilder` | `Services/` | Joins graph, warnings, loops and triggers into the drawing model. |
| `TransitionWarningAnalyzer` | `Services/` | The transition warnings – the **same** source as the list. |
| `GraphWarningList` | `Services/` | The text version of all graph warnings for the list view and the publish confirmation (#118). |
| `DialogGraphModel` | `Models/` | Nodes, edges, frames, markers, selection. |
| `GraphMetrics`, `SvgFormat` | `Models/` | Dimensions and culture-safe number formatting respectively. |
| `GraphNodeCard`, `GraphInspector` | `Components/` | Node content and detail panel respectively. |
| `DialogGraph.razor.js` | `Components/Pages/` | Pan/zoom the view and drag nodes – client-side. |

### What the graph shows – and why exactly that

Not everything that sounds like a "building block" is a node in the domain model. Whoever builds loops and triggers as
freely draggable tiles invents a second model alongside the domain:

| Concept | Entity | On the canvas |
|---|---|---|
| Question | `Question` | **Node** – the only real one |
| Transition | `Transition` | **Edge**, labeled with condition and evaluation position |
| Loop | `LoopDefinition` | **Range frame** around the *computed* body – no node of its own |
| Trigger | `TriggerDefinition` | **Chip** on the node or on a scope marker |

Two properties are what first make the representation honest:

- **There are no implicit edges.** `TransitionResolver.ResolveTransitionTarget` returns `null` when
  a question has no outgoing transitions – that is the **regular completion**, not a "continue with the
  next question by `Order`". The graph is thereby fully described by the `Transition`s.
  Therefore a question without an outgoing edge carries the badge *completion* and a doubled bottom edge:
  without a marking it reads like a missing connection.
- **The loop body is derived, not stored.** The frame is the bounding box over the
  `LoopAnalyzer` body – so existing logic that mirrors the core-internal `LoopResolver`.

Additionally the view marks the **entry question** and every question to which from there **no path**
leads. If the entry question is missing entirely, reachability is not determinable – then it stays at *one*
warning on the dialog, instead of coloring every node red.

### Warnings hang on the causing element

`GraphWarning` (`Models/`) is the shared type of both views: the same finding, additionally assigned to an
element (`Question`, `Transition`, `Loop` or `Dialog`). The rules lay privately in
`DialogEditor.razor` until #101 and have moved unchanged into the `TransitionWarningAnalyzer`; `LoopAnalyzer`
delivers its findings likewise located (`LoopInsight.TargetedWarnings`, with `Warnings` as a computed
text view).

**The wordings are a contract.** The dialog editor shows them unchanged, the publish confirmation counts them,
the E2E suite searches within them. `TransitionWarningAnalyzerTests` nails all four full texts,
`GraphWarningListTests` the text version including the prefix.

The list that the dialog editor builds from it lives in `Services/GraphWarningList.cs` (`Describe`) and
not in the `@code` block – there it would not be testable. It sets **only** the prefix: questions and transitions
carry the question key (`GraphWarning.QuestionId` is already, for an edge, its source question),
a loop marker its `CollectionKey`, and a warning on the **dialog** stays without a prefix – its
causer is the dialog itself. This exact case was the trap: until #118 the list accessed
`QuestionId!.Value` hard, a dialog or loop warning would have crashed it.

It is located by causer, not by find location: "No default transition" and "Multiple defaults" are
properties of the **group** and hang on the question; "condition is not evaluated" and "always takes
effect" hang on **their** transition. With the shadowed loop exit, the shadowing back-jump carries
the warning – it is the edge to be changed.

### Auto-layout: deterministic, otherwise worthless

`GraphLayout.Compute` layers by breadth-first search from the entry question, takes backward edges out of the
acyclic set and reduces crossings by barycenter. Saved positions do not exist here yet
– those are brought by stage 2 (#102).

The same graph **must** yield the same coordinates, otherwise E2E selectors wobble. Three promises carry
that, and all three are test cases in `GraphLayoutTests`:

1. **Only lists to the outside**, never a set or a dictionary – their iteration order is not
   guaranteed.
2. **Sort keys end with a unique ordinal**, so they are a total order and not reliant on
   the stability of `OrderBy`. The ordinal comes from `(Order, Id)` and **not** from the
   Guid alone: `CreateDialogVersionCommand` assigns each question a new Guid on cloning (ADR 0005),
   a guid-based layout would reshuffle on every new dialog version.
3. **Coordinates arise only from integer layer and column values**, never from a
   barycenter. Floating-point averages determine the *order*, not the position – otherwise the
   last decimal places would hang on the computation order and the promise would hold only most of the time.

**Without dummy nodes.** A full Sugiyama pulls placeholder chains through skipped layers.
They would be needed here only for back-jumps – and those run anyway in a channel to the right of the graph
past it, not between the nodes. At the target size of around 30 nodes they save not a single crossing,
but cost a second node kind in the model, in the rendering and in the selection.

Multiple transitions between the same two questions remain distinguishable via three independent features:
lateral fan offset (hits both the anchor point *and* the control points), an own label at its own
anchor point, an own `aria-label`.

### The canvas itself

Four promises from ADR 0006 are redeemed here – they hold for every extension:

- **Moving, zooming and dragging a node run in the JS module**, not in C#. The designer is
  Blazor *Server*; every Blazor event is a SignalR round-trip. Between `pointerdown` and
  `pointerup` **no** message goes to the server – `invokeMethodAsync` stands in
  `DialogGraph.razor.js` exclusively in `onPointerUp`. Whoever adds a call in `onPointerMove` there
  breaks an acceptance criterion of #102.
- **The `transform` on `.graph-viewport` belongs to the JS.** C# never renders it – otherwise the next
  re-render (say a selection) would reset pan and zoom.
- **Edges are drawn before the nodes.** The wide, invisible hit path that makes the thin line
  graspable would otherwise lie over the node center and swallow the click (measured in the spike).
  For the same reason the loop frame has `fill="none"` and `pointer-events: stroke`: it encloses
  everything and would otherwise catch every click inside it.
- **The canvas sets `data-canvas-ready`** once the module is bound. That is the **first**
  `data-` attribute in the designer and deliberately an exception – see § Tests.

Two points that count when extending:

- **Numbers in SVG only via `SvgFormat.N`.** The display culture is configurable (`en-US` by default);
  under a comma-decimal culture an interpolated `double` coordinate writes `12,5`, and since the comma is
  a *separator* in the path syntax, it silently becomes a wrong number sequence – no exception, only a
  wrong picture.
- **The model is computed once after loading into a field.** Called from a markup method
  (like `GraphWarnings()` in the dialog editor) the whole layout would run again on every render,
  thus on every click.

### Layout persistence: moving nodes (#102)

An auto-layout arranges, but it is not the author's arrangement. Nodes are therefore movable,
and the position lives in the table **`DialogLayout`** on the dialog – not as two columns on
`Question` and not in a file next to `connection-profiles.json`. Rationale along with discarded
alternatives: [ADR 0007](./adr/0007-layout-as-its-own-table.md).

The flow of a drag gesture:

1. `pointerdown` on `.graph-node` pre-notes the drag – **without** `preventDefault` and without
   pointer capture. Up to the threshold of **4 px** the gesture stays a click, otherwise every
   slightly wobbly click would swallow the selection.
2. From the threshold the module writes the node's `transform` directly and dims the adjacent
   edges (`.graph-edge.is-stale`, found via `data-from`/`data-to`). The paths are **not** recomputed in the
   browser: their geometry arises in `GraphLayout.Route` and is tested there – a
   second source for it would be more expensive than the inaccuracy of one drag long.
3. `pointerup` swallows the immediately following `click` (otherwise every drag would select the node
   additionally) and sends **exactly one** message: `MoveNodeAsync(questionId, x, y)`.
4. C# writes `SetDialogLayoutCommand`, adopts the returned layout into the **buffered**
   `DialogDetail` and rebuilds the drawing model from it – no second `GetDialogQuery` per gesture. Now
   edges and loop frames match exactly again.

Four things that count in doing so:

- **Screen → user coordinates via `viewport.getScreenCTM().inverse()`.** The matrix also contains
  the scaling that the `viewBox` produces relative to the SVG's CSS width. Whoever only divides by the
  zoom factor omits it – the node would then run faster or slower than the pointer depending on the window
  width.
- **Saved positions take effect in exactly one place:** at the end of `GraphLayout.Render`, where the
  node boxes arise. Layering, edge shape, barycenter and channel assignment stay with the auto-layout.
  A drag thereby changes only the position of one node – never the drawing shape of an edge and never the
  arrangement of the others. The drawing surface grows along, otherwise a far-dragged node would jut out of the
  `viewBox`.
- **The commit renders the same value that the JS wrote.** The module rounds to whole pixels and
  `SvgFormat.N` formats whole numbers without decimals – therefore the node does not jump on the re-render.
- **Moving works even for a published dialog.** The layout commands deliberately do not run
  under `DialogEditGuard`; coordinates do not touch session semantics. The publish lock of the
  graph editors (§ versioning) stays unchanged.

"Reset layout" in the toolbar discards all rows of the dialog
(`ResetDialogLayoutCommand`) – afterwards the auto-layout takes effect again. The button appears only if
positions are saved at all, and asks back inline like every destructive action in the designer.
A node with its own position carries a bar at the right edge (`.is-pinned`; the
loop membership marks the left one) and in the `aria-label` the addition "own position".

### Editing on the canvas (#103)

Since stage 3 the canvas is an editor. The carrying rule: **every gesture calls the same admin command
that the list view also calls.** There is no canvas CRUD, no new core command and no
schema change – rationale and discarded alternatives in
[ADR 0008](./adr/0008-gestures-on-the-canvas.md).

| Gesture | Entry point | Commands |
|---|---|---|
| **Drag** a building block from the palette | `onPaletteUp` → `CreateQuestionAtAsync` | `CreateQuestionCommand` + `SetDialogLayoutCommand` |
| **Activate** a palette entry | `@onclick` → `AddQuestionAsync` | `CreateQuestionCommand` (without position – the auto-layout places it) |
| Drag from the **port** onto a node | `endLink` → `ConnectAsync` | `CreateTransitionCommand` |
| Drag from the port **into the void** | `endLink` → `ConnectToNewQuestionAsync` | `CreateQuestionCommand` + `SetDialogLayoutCommand` + `CreateTransitionCommand` |
| Activate the port, then choose a node | `StartLink` + `SelectQuestion` | `CreateTransitionCommand` (the pointerless way) |
| Save a question's header fields | Inspector panel | `UpdateQuestionCommand` |
| ↑/↓ on the outgoing edges | Inspector panel | several `UpdateTransitionCommand` |
| Toggle "Default" | Inspector panel | `UpdateTransitionCommand` |
| Target/condition of a transition | Inspector panel | `UpdateTransitionCommand` |
| "Mark as loop" on the back-jump | Inspector panel | `CreateLoopCommand` |
| Create a trigger (question or dialog) | Inspector panel | `CreateTriggerCommand` |
| "Set as entry question" (#105) | Inspector panel | `UpdateDialogCommand` |
| Delete (question, transition, marker) | Inspector panel, two stages | `Delete*Command` |

The computation rules behind it lay privately in the `@code` block of `DialogEditor.razor` until #103 and were thereby
covered by no test. They now live in `Services/GraphEditing.cs` (`NextOrder`, `NextPriority` per
source question, `Reorder`) and `Services/LoopAnalyzer.cs` (`IsBackJump`, `UnmarkedBackJumps`) – and are
used by **both** views. Newly added was `QuestionFormModel.SuggestKey`: unlike
`LoopFormModel.SuggestCollectionKey`, which deliberately returns empty on a collision, here nothing may come out
empty – the suggestion carries a gesture that writes immediately.

#### The entry point belongs on the surface (#105)

The one row of the table that does **not** touch the graph: "Set as entry question" in the
question panel writes the dialog metadata via `UpdateDialogCommand`. Retrofitted during the build of the
canvas E2E, because the creation flow was incomplete without it: an author could build the whole graph from
gestures, but had to leave it for a single field – and the graph warned the whole time
about exactly that ("No entry question set", `DialogGraphBuilder`), without offering a way there.

No breach of the publish lock, but its normal case: the guard in `UpdateDialogCommand` takes effect
**exactly when** `StartQuestionId` changes (name and description stay free on a
published version). The button therefore carries the panel's usual `Locked`
(`!Editable || Busy`) and `RunGestureAsync` checks `requireEditable` – the lock becomes a non-triggerable
action instead of an error message. If the node is already the entry, the button is omitted
entirely; the "Role" line above already says so.

#### After a mutation there is a reload

`MoveNodeAsync` computes the new position locally (ADR 0007: the layout command returns the
**complete** layout). Every **graph** change, by contrast, reloads. The reason is not the
entities, but the **warnings**: `TransitionWarningAnalyzer` and `LoopAnalyzer` compute over the
whole `DialogDetail`, a new transition can lift a warning on a *different* question. On top, `DeleteQuestionCommand`
cleans up transitions, markers, triggers and layout rows along with it – rebuilding this cascade locally
would be the second truth that the issue forbids. The deleted items are counted as a
difference before/after the reload and reported ("… – 2 transitions, 1 trigger removed along with it"), and
`ReconcileSelection()` discards a selection whose element no longer exists.

#### Gestures are not idempotent

A double drop created two questions. Therefore two locks, and both are necessary:

- **Client:** every message runs through `send()` in the JS module, which locks until the return of the .NET
  method. **The promise of `invokeMethodAsync` is the receipt** – Blazor Server fulfills it once
  the call is through. A hanging circuit leaves the canvas locked; that is the right order
  of evils (locked instead of created twice), and the real disconnect is handled by the reconnect modal.
- **Server:** `RunGestureAsync` exits early on `_busy`. The client lock is bypassable, the
  server gate is the invariant. Conversely taken alone, it would silently swallow the second *legitimate*
  gesture of a fast user. `MoveNodeAsync` hangs on the gate too since #103.

A residual window remains: the promise resolves before the render batch is applied. A click in
this sub-frame window works on old DOM – caught by the server gate.

#### Read mode instead of a conflict message

For a published dialog, ports are **not rendered at all**, the palette is `disabled`, and the
hint offers "Create new version" (→ graph of the new version, not into the list: whoever works
here wants to keep working here). The JS module learns the state via `data-editable` on the `<svg>` –
an attribute that **C# owns and the module only reads**, fresh on every gesture. An `attach` option
would be frozen. This is the flip side of the ADR-0006 rule "what the JS sets, C# never renders", not its
breach. Moving stays allowed and does not run into the 409.

#### Port, rubber band and preview

The source port is a **sibling** of the node card, not a child: `<button>` in `<button>` is
invalid HTML, and the outer one would swallow click and focus. It sits at the bottom-edge center – exactly
where `GraphLayout.Route` starts a forward edge; the affordance thus does not lie about the
geometry. In the `pointerdown`, `.graph-port` is checked **before** `.graph-node`, otherwise the
move drag would swallow the connection gesture – and, as everywhere, without `preventDefault`, so that the click (the
pointerless way) carries.

Rubber band and drop preview are C#-rendered, empty placeholders (`.graph-rubber`, `.graph-ghost`);
the module only sets their geometry and empties them again. DOM created via `createElement` in a
container managed by Blazor would throw the renderer off over the child indices on the next diff.
Both need `pointer-events: none` – the target hit test runs via `document.elementFromPoint` (after
`setPointerCapture` the `event.target` is the capture element), and without the rule it would hit the rubber
band.

**The lock against the follow-up click sits on the respective element**, not always on the canvas: after a
palette drag the `click` fires on the palette entry. If `swallowNextClick` listened there on the canvas, every
drag would additionally create the click question – two questions from one gesture.

**The empty dialog now renders a drawing surface.** Until #103 a hint replaced the canvas as long as
there were no questions – onto a non-existent surface nothing can be dragged. The hint stands
above it, and `GraphMetrics.MinCanvasWidth`/`MinCanvasHeight` give a usable lower bound (previously
80 × 80 px). With that the flag `_canvasAttached` was dropped too: the truth is `_module`, and on a
dialog switch it is cleanly `detach`ed.

#### The expression editor is a component

Input field, live status, error location, snippet inserter and identifier reference stood in the
`TransitionEditor` and in the `TriggerEditor` line for line the same – except for one sentence. They now live in
`Components/ExpressionField.razor` (`@bind-Value`, `EmptyHint`/`EmptyMeaning`, `ShowBuilder`), which
the inspector panel also uses. The **blocking** check before saving stays with the caller: the
component displays, the caller decides. The parameter set consists exclusively of public
types (`DialogDetail`) – `ExpressionVariable` stays `internal`, because it does not stand in the parameter
list.

**Side finding:** `.expr-status` and `.expr-caret` lay scoped in `TransitionEditor.razor.css`, but were
used by the `TriggerEditor` too since #42 – there the live status was thus **unstyled**. Both rules
now live globally in `wwwroot/app.css`.

#### The inspector panels work without `EditForm`

`GraphQuestionPanel` and `GraphTransitionPanel` bind raw `<input>`/`<select>` with `@oninput` instead of
`InputText`/`InputSelect` in an `EditForm`. This is not a matter of style, but a finding measured on the running
panel:

- **`onchange` loses inputs.** The preset binding only delivers the value on leaving the
  field. But the panel is rebuilt after *every* gesture (the reload replaces `Detail`) – on
  saving the old value then silently stood in the command.
- **The submit of an `EditForm` did not arrive in the panel.** It presupposes a stable form lifecycle
  that a panel in an `@if` branch over changing selections does not have.

The required check is thereby taken over by `SaveAsync` in the panel – like the cross-field rules of the trigger form,
which likewise run before the command. The command checks anyway again. An `@key` on both panels binds
the instance to the edited element, so that a begun draft survives every re-render of the same selection
and is deliberately discarded on the switch.

### Test run in the graph (#104)

Stage 4 makes a picture out of the runner's history list: the test runner (§ test runner) has, under
`/dialogs/{id}/test`, **two views of the same run** – "History" (the list, unchanged) and "Graph"
(the canvas with the taken path). The toggle stands above the "Current question" card; a deep link
`?view=graph` opens the graph view directly (this is how the graph editor links "Play through").

**It is no second runner.** Start, answer, editing and `ResumeDialogAsync` lie unchanged in
`DialogTestRunner.razor`; the "Current question/result" card stands **outside** the toggle and is
rendered in both views. Thereby "the list-based runner stays equally usable"
is structurally true instead of promised – there is only one choreography, and a switch of the view does not touch the
run (a begun edit also stays the same). The hint **"The run is real"** stands
above the toggle too: the graphical presentation must not look more harmless than the run is.

| Building block | Location | Task |
|---|---|---|
| `GraphRunAnalyzer` | `Services/` | Derives the run state from `DialogDetail` + `ResumeDialogResult` + trigger log. |
| `GraphRunOverlay` & co. | `Models/GraphRunModel.cs` | Visits, taken edges, loop state, events. |
| `GraphRunCanvas` | `Components/` | The canvas of the run view (binds the JS module too). |
| `GraphRunInspector` | `Components/` | Answers per iteration, bindings and events **at the selected node**. |

#### The path is derived, not logged

The engine does **not** record which transition took effect: `SessionAnswer` carries no
`TransitionId`, and `QuestionAnsweredNotification` names only the next *question*. The path therefore arises
from the answer sequence – two consecutive answers form the pair *(from, to)*, the last answer
together with the open question the last pair.

From this follows a limit that the surface names instead of hiding it: **if several transitions lie between the same
two questions, it is not decidable which one took effect.** Then all are marked
(dashed instead of solid), the inspector says "ambiguous", and the `aria-label` names the reason.
Recomputing the evaluation would be not only another mirror of the core `TransitionResolver`, but
an impossible one: it would need the expression values from *back then*.

The pleasant side effect: **an edit recomputes the path without code of its own.** `EditAnswerCommand`
discards the downstream answers, the derivation shrinks along – even if the new path takes a different
branch.

#### What stands at the node

- **Visited** means: answered in this run. The card then shows, instead of the configuration line
  (type/required/options), the **value of the last answer**; the type stands in the inspector. The reason is the fixed
  card height – it clips overflow, both side by side would lose one of the two pieces of information.
- **Open** carries the badge "▶ open". A node can be open *and* visited: in a loop the
  same question is asked again.
- **Iteration n** comes from the iteration index of the last answer. A cycle **without** a loop marker
  produces no index – there stands "n× answered", because "iteration" would there simply be wrong.
- **Published triggers** hang as a `⚡` chip on the triggering node (source: `DesignerTriggerLog`, as in
  the list's log) and **flash once**. Bundled is per point in time ("⚡ Answer 2×"): a
  loop question collects two events *per* iteration, unbundled the chip row would burst the card.
  The individual events with their time stand in the inspector. Events without a question reference (completion) – and those
  to a meanwhile-deleted question – the inspector shows dialog-wide.
- The flash runs via `@keyframes` on the **creation** of the chip element; therefore the chips carry
  deliberately **no** `@key` (a new event shall be a new element), and `prefers-reduced-motion`
  gets the same statement statically.

#### Iteration count and bindings

The loop frame carries the count of iterations of the **most recent** loop instance – the same selection
that the core `LoopResolver` makes for the collection – and is drawn solid as long as the
open question lies in its range.

The expression bindings (`RunExpressionContext`, § expression context) stay inspectable, but **at the
selected node** instead of only globally: its own answer, the collection of every enclosing loop –
and `iterationIndex` **only at the open question**. It means the index of the last given answer to
*exactly this* question; shown at another node it would claim something false. For editing per
iteration the inspector lists the node's answers with their badge and each a button
"Edit" – the same `EditAnswerAsync` operation as in the list, including the iteration index.

#### The graph is not editable here – moving is

A running session works on exactly this graph; changing it out from under it is the trap that made #95
expensive. The run view therefore renders no palette and no ports and sets
`data-editable="false"` on the `<svg>`. **Moving stays allowed** (`SetDialogLayoutCommand`, guard-free,
ADR 0007) and is the only writing way of this view.

Unlike the editor page, here the **component** `GraphRunCanvas` binds the JS module (the same
`DialogGraph.razor.js`) and passes the finished drag up as `NodeMove`: the canvas belongs to it, and
the back channel would otherwise be an `ElementReference` that the page would have to pass through. Without a palette in the DOM and
without rendered ports, only moving, zooming and panning run in the module – `MoveNodeAsync` is thereby
the only message that comes from there.

### Inspector and accessibility

The inspector was in stage 1 a pure read view with a jump; since #103 it edits the selected
element. Embedded thereby are **not** the `@page` editors – `QuestionEditor` & co. have their own
`PageTitle`, their own heading and their own back link –, but own panels
(`GraphQuestionPanel`, `GraphTransitionPanel`) that call the same commands. The boundary runs along
the **data form**: scalar fields in the panel, everything with its own substructure or raw-JSON fallback
(answer options, validation rules, trigger condition) in the full editor, which "… edit →" opens.

The form models stay `internal` and **private state** of the panel; across the
component boundary only the result goes (`Models/GraphEdits.cs`: `QuestionEdit`, `TransitionEdit`,
`TransitionMove`, `LoopDraft`, `TriggerDraft`). The reason is CS0053 – Razor generates components as
`public`, an `internal` type on a `[Parameter]` breaks the build under `TreatWarningsAsErrors`.
A side effect is the clearer responsibility: **panel = form, page = commands**, and thereby exactly one
place for gesture locks and error path.

The inspector is at the same time the **keyboard path to everything that is a pointer gesture on the surface**:
connecting via a selection list, and the evaluation order is maintained only here anyway – a
position on the canvas must carry no semantics, otherwise cleaning up changes the behavior.

A pure canvas would be a regression compared to the forms. Therefore:

- Nodes are real `<button>`s in a `<foreignObject>`. Thereby focus ring, Enter/space and
  screen-reader role come from the platform instead of from hand work. Blazor carries that: its
  namespace check excludes `foreignObject` explicitly, child elements arise in the HTML namespace.
- **The tab order is the flow** – nodes are rendered by layer and column. That is a
  promise to the rendering, not a coincidence.
- Edges are **not** focusable (45 tab stops would be a desert), but readable and via the
  transition lists of the inspector fully reachable by keyboard.
- Every node carries a full `aria-label`; before the canvas stands a hidden
  summary ("3 questions, 3 transitions, 1 loop, no warnings").
- The `<svg>` has `role="group"`, **not** `role="application"` – the latter hijacks the
  screen-reader navigation.
- Contrast ≥ 4.5:1 also for strokes (WCAG demands there only 3:1; the finding from #95 sits deep), and
  **never color alone**: entry, completion and unreachability additionally carry a badge and an
  own contour form.

The list and form path stays fully preserved – the canvas is additional, not a replacement.

## Conventions

- Blazor components under `Components/` (pages in `Components/Pages/`), server-interactive render mode
  (`@rendermode InteractiveServer` on interactive pages).
- Shared UI primitives (`.editor`, `.field`, `.input`, `.btn`, `.data-table`, `.badge`, `.msg`,
  `.banner`, `.empty`, `.back`, `.confirm`, `h1 .badge` …) live **globally** in
  `wwwroot/app.css`; the `*.razor.css` files contain only
  page-specifics anymore. New editor pages use these classes, instead of duplicating them.
- UI texts and docs **English**. The designer is `IsPackable=false` → CS1591 is here **not** an error,
  XML docs are optional (the remaining warnings stay errors via `TreatWarningsAsErrors` though).
- **Display culture fixed to `en-US`** (`DesignerApp.DisplayCulture`, set as
  `CultureInfo.DefaultThreadCurrentCulture`). Without this fixing the formatting would follow the culture
  of the host – dates and numbers would then vary from machine to machine. Deliberately
  via the process culture instead of `RequestLocalization`: in Blazor Server the circuit renders, not an
  HTTP request. Answer **values** stay untouched by it – those are encoded invariantly by `AnswerValueCodec`.
- All rules of the `*.razor.css` files apply only to the HTML elements **of the own** component:
  CSS isolation does not hand its scope attribute to child components. Styles for rendered components
  (`<NavLink>` &c.) therefore belong globally into `wwwroot/app.css` – see the comment in
  `NavMenu.razor.css`, where exactly that had made the navigation links unreadable.
- **The reading width applies to all pages – except where a canvas stands.** `main.flirty-content`
  (`MainLayout.razor.css`) caps at **1100 px**, which is right for text and form columns. For the
  graph editor it was the limiting factor: at 1100 px the palette (12 rem) + canvas (base 640 px) +
  inspector (340 px) with spacings do not fit side by side – the threshold lies at **1204 px** –, the
  inspector slipped via `flex-wrap` under the palette, while on a 2560 px window over
  1400 px stayed empty on the right (#118). The cap therefore falls exactly when there is a canvas in the content:

  ```css
  main.flirty-content:has(.graph-layout) { max-width: none; }
  ```

  Three points about it are intentional. **First** `:has()` and **no** second layout on the page: the
  test runner renders `.graph-layout` only in the graph branch of its toggle, its history list thereby stays
  readable at 1100 px – a `@layout` on the page could not distinguish that, and upward
  (child → ancestor) there is no cascade in Blazor. **Second** globally in `wwwroot/app.css` instead of in
  `MainLayout.razor.css`: `.graph-layout` arises in another component, CSS isolation does not reach its
  scope attribute there. **Third** the specificity `(0,2,1)` against the scoped rule
  `.flirty-content[b-…]` with `(0,2,0)` – therefore `main` in front. Untouched stay the coupled
  heights `.graph-canvas-host { height: 70vh }` / `.graph-inspector { max-height: 70vh }` and
  `GraphMetrics.MinCanvasWidth` (that is a statement about the **user space**, not about the CSS width).
- **Numbers in SVG attributes exclusively via `SvgFormat.N`.** The display culture is configurable and
  applies also when rendering: under a comma-decimal culture an interpolated `double` coordinate becomes
  `12,5`, and because the comma in the SVG path syntax is a *separator*, a wrong number sequence arises
  from it – no exception, no
  message, only a wrong picture. Affects `d`, `transform`, `viewBox`, `x`/`y`, `width`/`height`.
- **What runs client-side stays client-side.** Drag and zoom gestures belong in a collocated
  `*.razor.js` module (pattern: `ReconnectModal.razor.js`, `DialogGraph.razor.js`); between `pointerdown`
  and `pointerup` no message goes to the server (ADR 0006). Attributes that this module sets
  (`transform` on `.graph-viewport`), C# may **never** render – the next re-render would otherwise reset them.
- Timestamps UTC.

## Tests

The service logic is checked via **xUnit** in `tests/Flirty.Tests` (the test project references the
designer; internals via `InternalsVisibleTo("Flirty.Tests")`):

- `Persistence/FlirtyDatabaseProviderExtensionsTests` – core mapping provider → EF provider + MigrationsAssembly.
- `Designer/JsonConnectionProfileStoreTests` – CRUD, copy semantics and persistence of the profiles.
- `Designer/ConnectionProfileOperationsTests` – test-connection and migrate against a SQLite temp DB.
- `Designer/FlirtyAdminGatewayTests` – admin CRUD over the real DI stack against a SQLite temp DB:
  creating/listing, error mapping (key conflict, unknown dialog, missing profile, non-migrated
  database), – as a regression – that a **profile switch takes effect immediately**, the question
  flows from #39 (create a question with options, swap the order in *one* operation, reset of the
  entry question on deletion), the transition flows from #40 (create/delete, reassign priorities in *one*
  operation) and the loop flows from #41 (create/change/delete, conflict on a duplicate
  `CollectionKey`, removal of the marker along with a deleted question).
- `Designer/LoopAnalyzerTests` – the loop analysis (#41): range determination including a one-question loop,
  partitioning into back-jumps/exits, every warning rule individually – and as a core probe the comparison with the
  core `LoopResolver` on the same graph (no drift of the mirrored computation).
- `Designer/DesignerExpressionContextTests` – the sample context of the expression validation (#40), checked
  against the **real** engine: valid expressions per question type, loop collection without iteration, a typo
  with position, shadowed/invalid keys and the type-correct quoting of the snippet inserter.
- `Designer/QuestionFormModelTests` – the mapping between input fields and rule JSON (#39):
  type-scoped serialization, camelCase without null values, raw-JSON fallback for unknown fields,
  rejected patterns/bounds and – as a core probe – that the engine's `AnswerValidator` actually applies the produced
  JSON.
- `Designer/TriggerFormModelTests` – the mapping between input fields and `Config` JSON (#42):
  reading/writing over the core type, raw-JSON fallback including preservation of foreign fields, the channel-dependent
  URL check and the normalization of question reference and expression.
- `Designer/FlirtyRuntimeGatewayTests` – the test runner (#43). The core probe is the **acceptance criterion
  in test form**: create a dialog including a loop via the admin commands and play it through **without publication**
  with two iterations (incl. the expected `IterationIndex` values and loop instance). On top of that
  the targeted editing of one iteration and the error mapping (invalid answer without a raw GUID,
  unknown session/dialog version, missing profile).
- `Designer/AnswerValueCodecTests` – the encoding of the answer values (#43), checked against the **real**
  `AnswerValidator`: the JSON form per question type, invariant number literals despite a decimal comma, the
  passing-through of unreadable inputs to the engine, the display (label instead of raw value) and the
  reversibility of `Decode`/`Encode` for the edit mode.
- `Designer/RunExpressionContextTests` – the live bindings of the run (#43), as a core probe compared at **every**
  step of a real run against the core `SessionExpressionContextBuilder` (no
  drift of the mirrored computation), on top of that the collected collection and the semantics of the
  `iterationIndex`.
- `Designer/DesignerTriggerLogTests` – the trigger log (#43): that the notifications, despite a fresh
  scope per step, land in the adopted log of the circuit, order/scope assignment, `Clear()` and
  that admin operations log nothing.
- `Designer/TransitionWarningAnalyzerTests` – the transition warnings (#101), which until then lay privately in the
  `DialogEditor`: every rule individually, the location at the node or at the edge – and as a
  core probe, that all four **wordings are unchanged**. List view, publish confirmation and
  E2E suite hang on it.
- `Designer/GraphWarningListTests` – the text version of these warnings (#118). The core is the
  **completeness**: that an unreachable question stands in the list (the finding of the issue) and that
  every warning is named after its causer – question via its key, loop marker via
  its `CollectionKey`, dialog warning **without** a prefix (exactly the case at which the old
  `QuestionId!` access would have crashed). On top of that a second wording contract including order.
- `Designer/GraphLayoutTests` – the auto-layout (#101). The core is the **determinism**, checked against
  the three sources from which it usually breaks away: hash iteration order (compute twice),
  newly assigned Guids (build the same graph twice – the test that survives `CreateDialogVersionCommand`)
  and the global order of the transitions. On top of that layering, broken-up backward edges,
  unreachable components, freedom from overlap, fanned-out multi-edges, crossing reduction and
  the number format under a comma-decimal display culture. For #102 the saved positions are added: they overwrite the
  computed position without changing the layer, the edges follow along, the drawing surface grows around
  a far-dragged node, a row without a question is skipped – and without a row the result is
  identical to the pure auto-layout (the proof that "Reset layout" really resets).
- `Designer/DialogGraphBuilderTests` – the drawing model (#101): markers for entry, completion and
  unreachability, warnings on the causing element, loop frame over the `LoopAnalyzer` body,
  triggers on question or scope marker, separately shown orphaned transitions and the
  `aria-label` description of every node; on top of that (#102) the loop frame over a moved
  question.
- `Designer/GraphRunAnalyzerTests` – the run state over the graph (#104), played with the **real
  engine**: visited nodes, open question and taken edges (including the counter-check that back-jump and
  exit stay unmarked), the iteration count of the loop and its leaving, parallel transitions as
  **ambiguous** – and as a core probe of the acceptance criterion, that an `EditAnswerCommand` recomputes the path
  and switches the branch in doing so. On top of that the trigger assignment (node, dialog-wide, `freshFrom`) without an
  engine, because it hangs on the log.
- `Designer/GraphEditingTests` – the computation rules of the gestures (#103), which previously lay privately in the `@code` block:
  `NextOrder`, `NextPriority` **per source question** and `Reorder` (position index → `Priority`,
  including the repair of duplicate values).
- `Designer/DesignerTestHost` – no test, but the shared DI stack (mirror of `DesignerApp`)
  and the SQLite temp database for the gateway tests. If `DesignerApp.ConfigureServices` changes, that
  is the one place to bring along.

On top of that, in the core come the counterparts: `Domain/TriggerConfigTests` (the schema itself) and
`Runtime/DialogTriggerDispatchTests` – the end-to-end proof that a webhook trigger configured in the designer
is actually delivered when running through a dialog (real engine, real SQLite DB,
HTTP spy).

```pwsh
dotnet test tests/Flirty.Tests
```

### Playwright E2E of the UI (#46, canvas #105)

The UI itself is checked in `tests/Flirty.E2E` in the **browser** – the same mechanics as with the
chat UI of the web sample (#45/#47). The canvas came along in stages: #101–#104 each delivered a
smoke test, **#105** completes the coverage.

- `DesignerAppFixture` hosts `DesignerApp` in-process on a free Kestrel port and creates beforehand an
  **active** connection profile against a freshly migrated SQLite temp database (profile file and DB
  lie in a temp ContentRoot, not in the repo).
- `DesignerE2ETests.Create_and_save_a_dialog_with_branching_and_a_loop` – the acceptance criterion
  of the issue: create a dialog → three questions → answer options in the question editor → entry question → three
  transitions → condition `more == "yes"` including **live validation** → mark a loop over the
  back-jump suggestion → publish. A concluding **reload** re-renders everything from
  the database and thus proves the persistence.
- `DesignerE2ETests.Test_run_plays_the_loop_through_with_the_real_engine` – the test runner (#43)
  on the same (unpublished) dialog: two iterations, exit, completion; checked are the
  `Iteration 2` badge of the history and the collected collection in the expression context.
- `DesignerE2ETests.Graph_view_shows_the_flow_and_leads_into_the_question_editor` – the smoke test of the
  graph view (#101): the canvas binds its JS module, draws three nodes and three edges, marks
  entry and completion, frames the loop and hangs the trigger chip on exactly the question after which it
  fires; the selection opens the inspector and leads into the existing question editor.
- `DesignerE2ETests.Graph_node_move_survives_the_reload` – the smoke test of the
  layout persistence (#102) on the **published** dialog: drag a node, reload (the server renders the
  position from the database), "Reset layout" – afterwards the node lies again on its
  auto-layout position. That the dialog is published proves at the same time the guard exception from
  ADR 0007: no error message appears.
- `DesignerE2ETests.Graph_palette_and_port_create_questions_and_a_transition`,
  `…Graph_inspector_edits_a_question_a_transition_and_deletes_with_the_cascade` and
  `…Graph_gestures_are_disabled_on_a_published_dialog` – the gestures and the read mode from #103:
  palette drag and port connection, the complete inspector path (save fields, connect, toggle default,
  delete with a visible cascade), the **list parity** and "moving works, 409 stays
  out".
- `DesignerE2ETests.Test_run_in_the_graph_highlights_the_path_taken` – the test run in the graph (#104)
  on the same (unpublished) dialog: toggle, start the run, two iterations; checked are
  visited nodes with their answer value, the open question, the number of taken edges after every
  step, "2 iterations" on the loop frame and the `⚡` chip on the node. Afterwards the inspector path
  (bindings and answers per iteration at the selected node), an **edit** that lets the path shrink visibly
  – and at the end toggling twice: "History" shows the same run, "Graph" binds the canvas
  anew. Switching back is at the same time the probe that releasing the JS binding does not tear the circuit.
- `DesignerE2ETests.Graph_creation_flow_on_the_canvas_survives_publishing_and_the_reload` – the
  creation flow from #105, exclusively via gestures: drag a building block from the palette, drag **twice from the port
  into the void** (question *and* transition from one motion – the only branch that runs through "no node under
  the pointer"), condition `choice == "yes"` in the inspector including **live validation**, the
  second edge as the default, set the entry question **on the node**, move a node, publish in the dialog editor
  – and after the **reload** everything comes from the database, including the position
  (compared is the `transform`, which stands in *user coordinates* and is thereby independent of the
  scaling of the SVG).
- `DesignerE2ETests.Graph_inspector_creates_a_trigger_and_a_loop_at_the_cycle` – the two gestures that #103
  had left open: mark a **back-jump** created via the port as a loop over the suggestion *at the edge*
  (the collection key is pre-filled with `choice_list`) and create a trigger at exactly
  one question – the chip afterwards hangs there and not on all. At the end reload and
  list parity in "Loops" and "Triggers".

A few points that save time when extending the suite:

- **The host needs `ApplicationName = "Flirty.Designer"` and `EnvironmentName = "Development"`.**
  Only so does the `StaticWebAssetsLoader` find the `*.staticwebassets.runtime.json` (it loads it via
  `Assembly.Load(ApplicationName)`) and `MapStaticAssets()` the matching `endpoints.json`. If they are missing,
  `_framework/blazor.web.js` is not served, the circuit never comes about and **every click
  fizzles**.
- **After every page change the first interaction is unreliable.** The page is at first only
  pre-rendered; until the circuit has taken it over, clicks and inputs fizzle silently. A
  usable JS signal for it does not exist – `window.Blazor.reconnect` is set and the
  `<!--Blazor:…-->` boot markers are gone *before* events arrive (measured). Therefore
  `InteractWhenReadyAsync` runs the first – **idempotent** – interaction in a retry loop.
- **The canvas does not use `InteractWhenReadyAsync`, but waits for `data-canvas-ready`.** The
  retry pattern presupposes idempotency, and that does not hold on a canvas: a repeated drag
  would move twice, a repeated zoom step would zoom twice. The attribute is set by the JS module
  once it is bound – and because `OnAfterRenderAsync` does not run at all during prerendering, it is
  at the same time the proof that the circuit has taken over the page. It is the **first**
  `data-` attribute in the designer; the rest of the suite addresses via role, heading, field `id` and
  CSS class.
- **A drag needs `ScrollIntoViewIfNeededAsync` and `page.Mouse`, not `DragToAsync`.** Two traps
  in one: `DragToAsync` uses the HTML5 drag-and-drop API, which does not trigger at all on an SVG canvas with
  pointer events – and mouse coordinates are **window-relative**, while the
  canvas host stands 70 vh tall below header, hint and toolbar. Without the scrolling, the
  gesture aims past a node of the lower layers into the void, without any error message; the test then
  fails on the effect, not on the cause. It is dragged over several `Mouse.MoveAsync` steps,
  so that the module's 4-px threshold is exceeded as with a real gesture.
- **A node contains two buttons since #103** – the card and the source port. `GetByRole(Button)`
  within `.graph-node` is thereby a strict-mode violation; it is addressed via
  `.graph-node-card` or `.graph-port`. That is the kind of break that a new affordance triggers with
  inevitability: the existing test from #101 was hit by it and had to be brought
  along.
- **The palette gesture runs over the same mechanics as the node drag** (`DragToCanvasAsync` →
  `DragBetweenAsync`), even though the palette HTML is *outside* the SVG: it deliberately uses
  pointer events instead of HTML5 DnD (ADR 0008), so that there is one event model and one lock.
- **A DOM value does not prove that Blazor saw the input.** If the first interaction fizzles on
  a freshly rendered field, the typed value stands nonetheless in the DOM – until the next render overwrites it with
  the bound value. Whoever checks `ToHaveValueAsync` in this window sees success and
  saves the old value; the test then goes red **under load** and green alone. Reliable is only an
  effect that the server produced (here: the node with the new key) – therefore the repeated
  unit comprises filling **and** saving.
- **A gesture that locks its own trigger must not stand in `InteractWhenReadyAsync`** – unless
  the effect check comprises the whole sequence. The save button is `disabled` for the duration of the
  request; a repetition of only the click runs into a disabled button and waits until
  the timeout.
- **Behind every gesture stands a server-produced effect – no wait time.** The lock `send()`
  in the JS module discards a second gesture **silently** as long as the first runs (see § *gestures are not
  idempotent*): a motion triggered too early leaves no error message, only a missing
  effect. Usable effects are node and edge counts, `is-pinned`, `is-start`, the edge label
  or the wording in `.banner.ok`. If a canvas test goes red, the first question is therefore *"which gesture
  was silently discarded?"* – not the assertion that failed.
- **A non-idempotent action needs a visible precondition instead of a repetition.** When
  connecting via `#inspectorConnect` that is the "Connect" button, which becomes operable exactly when the
  server knows the chosen target: repeated is only the *choosing* (harmless), clicked is afterwards
  **once**. Without this intermediate step the test from #103 hung on a race – the selection in the list
  could be overtaken by the re-render of the node selection and discarded in doing so (the `@key` on the panel replaces
  the whole instance), and the click ran into a disabled button. Noticed in #105 and brought
  along there.
- **`SelectNodeAsync` needs the expected key if a question panel is already open.** Otherwise
  `#inspectorKey` is already visible, and the retry loop holds a fizzled click for successful.
  The field content, by contrast, says *which* question the server is currently showing.
- **On the canvas one aims in fractions of the surface, not in pixels.** The SVG scales its
  `viewBox` into the 70 vh tall host, whose width is shared by palette and inspector and which since #118 hangs on
  the window instead of on the reading width – how many screen pixels a node is wide depends thus on the
  window **and** on the cut. A fixed pixel value landed with a different
  cut on a node instead of beside it, and the drag into the void would silently become a connection
  (`DragToCanvasFractionAsync`). It must be released anyway **inside** the canvas box – outside
  the gesture is deliberately an abort (`insideCanvas` in the module).
- **Edges are chosen via the list in the inspector, not by clicking `.graph-edge-hit`.** The
  hit path is a Bézier whose bounding-box center need not lie on the stroke – Playwright
  then aimed beside it and failed on the actionability instead of on the matter. The list is anyway the
  keyboard path (`SelectOutgoingEdgeAsync` addresses `ol.graph-inspector-list`, because the *incoming*
  edges stand in a `ul`).
- **"Publish" needs `Exact = true`.** Without it the name also matches "Yes, publish", and
  the locator violates strict mode. Both steps belong in *one* repeated unit
  (`PublishFromEditorAsync`): with open graph warnings the editor asks back, otherwise not – and after
  publishing none of the buttons is visible anymore, so the unit is idempotent.
- **The palette order is that of the enum.** `.graph-palette-item` `.First` is
  `QuestionType.SingleChoice` (key suggestion `choice`), `.Nth(2)` `FreeText` (`text`). For text filters
  on nodes these are the usable pairs: `choice`/`choice2` would hit both nodes as a substring, and
  the type text of the card plays along ("Single choice (SingleChoice)" contains *choice*, "Free text
  (FreeText)" contains *text*).

```pwsh
pwsh tests/Flirty.E2E/bin/Release/net10.0/playwright.ps1 install chromium   # once
dotnet test tests/Flirty.E2E
```

## Roadmap (EPIC 7 / EPIC 11)

**EPIC 7 – Designer (completed):** #37 connection profiles ✅ → #38 dialog CRUD UI ✅ →
#39 question editor ✅ → #40 branching editor ✅ → #41 loop editor ✅ → #42 trigger editor ✅ →
#43 test runner ✅ → #46 designer E2E ✅.

**EPIC 11 – Visual graph designer (#99, completed):** #100 spike canvas technology ✅ (ADR 0006) →
#101 graph view (read-only) ✅ → #102 layout persistence + moving ✅ (ADR 0007) →
#103 editing on the canvas ✅ (ADR 0008) → #104 test run in the graph ✅ →
#105 Playwright E2E of the canvas ✅.
