---
name: flirty-designer
description: Build or extend the Blazor designer (Flirty.Designer) – dialog/question/answer/branching/loop/trigger configuration, graph canvas, multi-DB connection profiles. Use for "designer", "Blazor UI for dialogs", "dialog editor", "branching editor", "graph view", "canvas", "connection profile", "designer E2E", "EPIC 7", "EPIC 11", issues #37–#43, #46 and #99–#105.
---

# Build / extend the Blazor designer

> **Status: EPIC 7 (issues #37–#43) fully implemented** – connection-profile management (#37),
> dialog CRUD (#38), question editor (#39), branching editor (#40), loop editor (#41), trigger editor
> (#42) and test runner (#43); `docs/DESIGNER.md` describes all seven. The UI has been covered by
> Playwright E2E since **#46**. **EPIC 11** (visual graph designer, #99) is likewise **fully
> implemented**: technical spike (#100 → ADR 0006), **graph view (#101)**, **layout persistence
> (#102 → ADR 0007)**, **editing on the canvas (#103 → ADR 0008)**, **test run in the graph (#104)** and
> the **canvas E2E (#105)**. This skill is the **guardrail** for extensions: the intended architecture
> and the conventions to stick to when implementing.
> Reference: `docs/DESIGNER.md`, `docs/ARCHITECTURE.md` §4/§8/§10, `docs/BACKLOG.md` EPIC 7/11,
> `docs/adr/0006-canvas-technology-in-the-designer.md`, `docs/adr/0008-gestures-on-the-canvas.md`.

## Current state (verified)

- `src/Flirty.Designer/Flirty.Designer.csproj`: `Microsoft.NET.Sdk.Web`, references `..\Flirty` **and
  all three** `..\Flirty.Migrations.{Sqlite,PostgreSql,SqlServer}` (for multi-DB migrate), plus
  `InternalsVisibleTo("Flirty.Tests")` and `InternalsVisibleTo("Flirty.E2E")`;
  `BlazorDisableThrowNavigationException=true`.
- `DesignerApp.cs`: the **entire** composition (`ConfigureServices(WebApplicationBuilder)` +
  `Configure(WebApplication)`); since #46 `Program.cs` only calls both – this is how the E2E hosts the
  same setup in-process (pattern like `WebSampleApp`). New services/middleware belong there, not in
  `Program.cs`. Contents: `AddRazorComponents().AddInteractiveServerComponents()` +
  `MapRazorComponents<App>().AddInteractiveServerRenderMode()` → **Blazor Web App, server-interactive**,
  since #37 **`AddFlirty()` (parameterless)**; the `FlirtyDbContext` is created per active connection
  profile via `FlirtyDesignerDbContextFactory : IDbContextFactory<FlirtyDbContext>`.
- **Connection profiles (#37):** `Models/ConnectionProfile.cs`, `Services/IConnectionProfileStore` +
  `JsonConnectionProfileStore` (JSON in the ContentRoot, gitignored), `ActiveConnectionProfile` (scoped,
  with `Activate`/`Adopt`), `ConnectionProfileOperations` (test-connection/migrate),
  `ConnectionProfileContextBuilder`; UI under `Components/Pages/ConnectionProfiles.razor`
  (`/connections`) + `Components/Layout/NavMenu.razor`.
- **Dialog CRUD (#38):** `Services/FlirtyAdminGateway.cs` (+ `AdminResult<T>`),
  `Models/DialogFormModel.cs`, pages `Components/Pages/Dialogs.razor` (`/dialogs`) and
  `Components/Pages/DialogEditor.razor` (`/dialogs/{id:guid}`). Shared UI classes (`.editor`,
  `.field`, `.input`, `.btn`, `.data-table`, `.badge`, `.msg`, `.banner`, `.empty`, `.back`, `.confirm`,
  `h1 .badge`) live **globally** in `wwwroot/app.css`; `*.razor.css` contains only page-specific bits.
- **Question editor (#39):** `Models/QuestionFormModel.cs` (metadata + rule JSON ⇄ input fields, with a
  raw-JSON fallback), `Models/AnswerOptionFormModel.cs`, `Models/QuestionTypeLabels.cs` (type-name
  labels, `UsesOptions`), page `Components/Pages/QuestionEditor.razor`
  (`/dialogs/{dialogId:guid}/questions/{questionId:guid}`) and the "Questions" section in
  `DialogEditor.razor` (list, inline create, ↑/↓, delete).
- **Branching editor (#40):** `Models/TransitionFormModel.cs`, `Models/ExpressionVariable.cs`,
  `Services/DesignerExpressionContext.cs` (sample context + identifier reference + snippet inserter),
  page `Components/Pages/TransitionEditor.razor`
  (`/dialogs/{dialogId:guid}/transitions/{transitionId:guid}`) and the "Transitions (branching)" section
  in `DialogEditor.razor` (grouped per source question, ↑/↓, warnings, inline create). For this the core
  also delivers `DialogDetail.Loops` (`LoopDetail`).
- **Loop editor (#41):** `Models/LoopFormModel.cs`, `Models/LoopInsight.cs`, `Services/LoopAnalyzer.cs`
  (range detection + warnings), page `Components/Pages/LoopEditor.razor`
  (`/dialogs/{dialogId:guid}/loops/{loopId:guid}`) and the "Loops" section in `DialogEditor.razor` (list,
  inline create, suggestions from unmarked back jumps). For this the core gained the **loop CRUD**
  (`Create/Update/DeleteLoopCommand`, `IDialogAdminStore.GetLoopAsync` / `LoopCollectionKeyExistsAsync` /
  `GetLoopsReferencingQuestionAsync`) and in `Flirty.AspNetCore` `Dtos/Admin/LoopDtos.cs`, the
  `.../loops` endpoints and `Loops` in `DialogDetailResponse`.
- **Trigger editor (#42):** `Models/TriggerFormModel.cs`, `Models/TriggerLabels.cs`, page
  `Components/Pages/TriggerEditor.razor` (`/dialogs/{dialogId:guid}/triggers/{triggerId:guid}`) and the
  "Triggers" section in `DialogEditor.razor` (list, inline create; **no** sorting – the entity has no
  `Order`/`Priority`). For this the core gained: `TriggerConfig` (public schema of the `Config` column),
  `Create/Update/DeleteTriggerCommand`, `IDialogAdminStore.GetTriggerAsync` /
  `GetTriggersReferencingQuestionAsync`, `Triggers` in `DialogDetail`, in `Flirty.AspNetCore`
  `Dtos/Admin/TriggerDtos.cs` + `.../triggers` endpoints – **and the runtime delivery** in the
  `WebhookNotificationHandler` (`IDialogStore.GetTriggersForSessionAsync`).
- **Test runner (#43):** core command `StartDialogVersionCommand` + `IFlirtyEngine.StartDialogVersionAsync`
  (`src/Flirty/Runtime/`), in the designer `Services/DesignerGateway.cs` (shared base, `GatewayResult<T>`),
  `Services/FlirtyRuntimeGateway.cs`, `Services/AnswerValueCodec.cs`, `Services/RunExpressionContext.cs`,
  `Services/DesignerTriggerLog.cs` + `DesignerTriggerLogHandlers.cs`, `Models/AnswerInputModel.cs` +
  `Models/AnswerChoice.cs`, `Components/AnswerInput.razor` and the page
  `Components/Pages/DialogTestRunner.razor` (`/dialogs/{dialogId}/test`), linked from the `DialogEditor`.
- **Graph view (#101):** the reading page `Components/Pages/DialogGraph.razor`
  (`/dialogs/{id}/graph`) with collocated `DialogGraph.razor.js` (pan/zoom – the **first JSInterop** in
  the designer), the components `Components/GraphNodeCard.razor` and `Components/GraphInspector.razor`,
  the services `Services/GraphLayout.cs` (Sugiyama-light), `Services/DialogGraphBuilder.cs` and
  `Services/TransitionWarningAnalyzer.cs` (extracted from `DialogEditor.razor`) as well as the models
  `Models/GraphWarning.cs`, `DialogGraphModel.cs`, `GraphLayoutResult.cs`, `GraphMetrics.cs`,
  `SvgFormat.cs`. **No core code** – the data source stays `GetDialogQuery`. Linked from `Dialogs.razor`
  and the header of the `DialogEditor`.
- **Layout persistence (#102):** the only stage of EPIC 11 with a **schema change**. New in the core:
  `Domain/DialogLayout.cs` + `Domain/LayoutElementKind.cs`, `Dialog.Layout`,
  `Persistence/Configurations/DialogLayoutConfiguration.cs` (unique index over
  `(DialogId, ElementKind, ElementId)`), `IDialogAdminStore.GetLayoutAsync` /
  `GetLayoutsReferencingElementAsync`, `Runtime/Admin/SetDialogLayoutCommand.cs` (batch upsert) and
  `ResetDialogLayoutCommand.cs` – **both without `DialogEditGuard`** (ADR 0007) –, `DialogLayoutDetail` /
  `DialogLayoutEntry` + `DialogDetail.Layout`, one migration `AddDialogLayout` in **all three**
  migration projects, in `Flirty.AspNetCore` `Dtos/Admin/DialogLayoutDtos.cs`,
  `PUT`/`DELETE .../dialogs/{id}/layout` and `Layout` in `DialogDetailResponse`. Plus the two manual
  branches: `CreateDialogVersionCommand` clones the rows via the `questionIdMap`, `DeleteQuestionCommand`
  cleans up referencing ones. In the designer: saved positions in `GraphLayout.Render`, `IsPinned` on
  `GraphNodePosition`/`GraphNode`, node drag in the JS module plus `[JSInvokable] MoveNodeAsync` and
  "Reset layout" in `DialogGraph.razor`.
- **Editing on the canvas (#103):** **no core code, no schema change** – the gestures call the existing
  admin commands (ADR 0008). New in the designer: `Components/GraphPalette.razor`,
  `Components/ExpressionField.razor` (the expression editor extracted from
  `TransitionEditor`/`TriggerEditor`, now used by both), `Components/GraphQuestionPanel.razor` and
  `GraphTransitionPanel.razor` (editing branches of the inspector), `Services/GraphEditing.cs`
  (`NextOrder`, `NextPriority`, `Reorder`), `Models/GraphEdits.cs` (public payloads of the panels),
  `QuestionFormModel.SuggestKey`, `LoopAnalyzer.IsBackJump`/`UnmarkedBackJumps` (pulled out of the
  `DialogEditor`, which now calls them), `GraphMetrics.PortSize`/`MinCanvasWidth`/`MinCanvasHeight`,
  `GraphElementKind.Trigger`, source port in `GraphNodeCard`, four `[JSInvokable]` in `DialogGraph.razor`
  (`CreateQuestionAtAsync`, `ConnectAsync`, `ConnectToNewQuestionAsync`, `MoveNodeAsync`) and in the JS
  module `send()`, `beginLink`/`endLink`, the palette gesture.
- **Test run in the graph (#104):** likewise **no core code and no schema change**. The test runner
  (`/dialogs/{id}/test`) gets a second view of the same run (toggle "History"/"Graph", deep link
  `?view=graph`); start/submit/edit stay in `DialogTestRunner.razor`, the "Current question" card sits
  outside the toggle and applies to both. New in the designer:
  `Services/GraphRunAnalyzer.cs` (derives the path from the **answer sequence** – the engine logs no
  `TransitionId`; parallel transitions are therefore *ambiguous*), `Models/GraphRunModel.cs`
  (`GraphRunOverlay`, `GraphRunVisit`, `GraphRunAnswer`, `GraphRunEdgeUse`, `GraphRunLoopState`,
  `GraphRunTrigger`), `Components/GraphRunCanvas.razor` (binds the **existing** `DialogGraph.razor.js`
  and reports moves as `NodeMove`), `Components/GraphRunInspector.razor` (answers per iteration,
  bindings, events at the selected node), run state as a `[Parameter]` on `GraphNodeCard`,
  `NodeMove` in `Models/GraphEdits.cs` and `RunExpressionSnapshot` now `public` (CS0053).
- **Canvas E2E (#105):** the browser coverage of the canvas (two new tests, see below) – and the only
  feature addition of the stage: **"Set as entry question"** in the `GraphQuestionPanel`
  (`SetStart` → `GraphInspector.SetStartQuestion` → `DialogGraph.SetStartQuestionAsync` →
  `UpdateDialogCommand`). Before, the entry point could only be set in the dialog editor, even though the
  graph warned about its absence. No core code: the command's guard takes effect exactly when
  `StartQuestionId` changes, so the button carries the panel's usual `Locked`.
- **Acceptance findings (#118):** two fixes from the manual pass wrapping up EPIC 11, both **without**
  core code. New `Services/GraphWarningList.cs` – the `DialogEditor`'s publish confirmation now reads
  **all** graph warnings (`DialogGraphModel.AllWarnings`) instead of only those of the
  `TransitionWarningAnalyzer`; an unreachable question could previously be published without a
  confirmation. Plus the one CSS rule `main.flirty-content:has(.graph-layout)` with which the graph pages
  lift the 1100-px reading width. Both detailed below under *Conventions*.
- **Tests:** `tests/Flirty.Tests/Designer/` (`JsonConnectionProfileStoreTests`,
  `ConnectionProfileOperationsTests`, `FlirtyAdminGatewayTests`, `QuestionFormModelTests`,
  `DesignerExpressionContextTests`, `LoopAnalyzerTests`, `TriggerFormModelTests`,
  `FlirtyRuntimeGatewayTests`, `AnswerValueCodecTests`, `RunExpressionContextTests`,
  `DesignerTriggerLogTests`, `TransitionWarningAnalyzerTests`, `GraphWarningListTests`, `GraphLayoutTests`,
  `DialogGraphBuilderTests`, `GraphEditingTests`, `GraphRunAnalyzerTests`; shared DI stack in
  `DesignerTestHost`) plus in the core
  `Domain/TriggerConfigTests`, `Runtime/DialogTriggerDispatchTests`,
  `Runtime/StartDialogVersionCommandHandlerTests` and `Runtime/DialogLayoutTests`. Plus the browser
  coverage in `tests/Flirty.E2E/` (`DesignerAppFixture`, `DesignerE2ETests`, shared browser session in
  `PlaywrightSession`).

## Guardrails for the implementation

1. **Work through the engine, not around the DbContext.** The designer uses the existing admin
   commands/queries via `ISender` – **not** `FlirtyDbContext` or `IDialogAdminStore` directly.
   Available in `src/Flirty/Runtime/Admin/`:
   - Dialogs: `ListDialogsQuery`, `GetDialogQuery`, `CreateDialogCommand`, `UpdateDialogCommand`,
     `DeleteDialogCommand`, `PublishDialogCommand`, `UnpublishDialogCommand`.
   - Questions: `Create/Update/DeleteQuestionCommand`. Options: `Create/Update/DeleteAnswerOptionCommand`.
     Transitions (branching): `Create/Update/DeleteTransitionCommand`. Loops:
     `Create/Update/DeleteLoopCommand`. Triggers: `Create/Update/DeleteTriggerCommand`.
   - Views (navigation-free) in `AdminModels.cs`: `DialogSummary`, `DialogDetail`, `QuestionDetail`,
     `AnswerOptionDetail`, `TransitionDetail`, `LoopDetail`, `TriggerDetail`.
   - DI: `AddFlirty(...)` registers `IDialogAdminStore`; add it in the designer's `Program.cs`
     (incl. provider choice per connection profile).

   **Concretely, since #38: always via `FlirtyAdminGateway`, never `@inject ISender`.**
   ```csharp
   var result = await Admin.ExecuteAsync((sender, token) => sender.Send(new ListDialogsQuery(), token));
   if (!result.Success) { _error = result.Error; return; }
   ```
   The gateway opens a fresh DI scope per operation (in Blazor Server a scope otherwise lives the whole
   circuit → the `FlirtyDbContext` would stay pinned to the first-used profile, the change tracker would
   fill up, and the non-thread-safe context would be shared) and returns an `AdminResult<T>` with a
   readable error message instead of an exception that kills the circuit.

2. **Multi-DB via connection profile (#37) — IMPLEMENTED.** Provider + connection string managed locally
   as profiles; at runtime opened against the active profile via `IDbContextFactory<FlirtyDbContext>`
   (impl. `FlirtyDesignerDbContextFactory`). The provider→`MigrationsAssembly` mapping is supplied by the
   public core API `FlirtyDatabaseProvider` + `DbContextOptionsBuilder.UseFlirtyProvider(...)` (details:
   `docs/DESIGNER.md`, `docs/PERSISTENCE.md`). Do not duplicate – reuse this API.

3. **Validate expressions on save (#40/#42 implemented).** Compile/check branching conditions and
   trigger expressions via `IExpressionEvaluator.Validate(...)` before saving – the engine is sandboxed
   (no `eval`), see `docs/BRANCHING-EXPRESSIONS.md`. The same principle is already applied by #39 for
   validation **patterns**: `QuestionFormModel.TryBuildValidationRules` compiles the regex with the same
   250-ms timeout as the `AnswerValidator`, instead of deferring the error to runtime.

   **The context for this is supplied by `DesignerExpressionContext` (#40) – reuse, do not rebuild.**
   It binds a sample value per question whose **type exactly matches the runtime binding** (number →
   `long`, date → **string**, multi-choice → list) and every loop collection as an empty list.
   #42 uses it **unchanged**: `TriggerDefinition.Expression` runs through the same engine.
   Two pitfalls: do **not** quote string literals via `JsonSerializer` (the parser rejects its
   `\u00XX` escapes), and the engine's error message is raw/technical – supply your own message rather
   than passing it through.

   **Always serialize business JSON via the core type, not via a duplicate.** #39 uses
   `Flirty.Validation.ValidationRules` directly (camelCase, `WhenWritingNull`); if stored JSON contains
   unknown fields, the editor falls back to a raw-JSON text field instead of silently discarding them on
   save. #42 does the same with `Flirty.Domain.TriggerConfig` (`url`/`name`) for
   `TriggerDefinition.Config` – including a raw-JSON fallback.

4. **Loops are branching + marker (#41 implemented).** A cycle arises from a `Transition` back to an
   earlier question; `LoopDefinition` (CollectionKey/Entry/Breaking) makes it visible. The branching
   editor draws it as a **back-jump** badge, the marker is maintained by the loop editor. See
   `docs/LOOPS.md`.

   **Mirror the core's graph analyses, do not import them.** `Services/LoopAnalyzer.cs` recomputes the
   loop range (`(forward from Entry, stop at Breaking) ∩ (backward to Breaking) ∪ {Entry, Breaking}`),
   because `LoopResolver` is `internal` and needs a `Dialog` entity with navigations – the designer only
   has `DialogDetail`. Same demarcation as `DesignerExpressionContext` ↔ `SessionExpressionContextBuilder`.
   **Mandatory here:** a test that compares both implementations on the same graph (`LoopAnalyzerTests`,
   body indirectly via `LoopResolver.ResolveAssignment`), otherwise they drift apart silently.

   **Warnings mirror the resolver rules, not intuition.** "Exit unreachable" follows the
   `TransitionResolver` exactly: the first matching non-default wins (an empty expression always
   matches), otherwise the topmost default. Further cases: no exit, no back-jump, overlapping ranges (the
   `LoopResolver` then already throws in the constructor – **every** session aborts) and shadowing
   `CollectionKey`s (share the check via `DesignerExpressionContext.IsBindable`/`IdentifierNote`, do not
   duplicate).

   **FK-free references need cleanup.** `LoopDefinition` references questions without a foreign key –
   `DeleteQuestionCommand` therefore removes referencing markers along with it, like the transitions.
   Uniquely enforced is only the `CollectionKey` per dialog (`LoopCollectionKeyExistsAsync` →
   `InvalidOperationException` → 409); without it two markers of the same name would silently overwrite
   each other in the collection binding.

5. **Triggers are back channels – and really fire since #42 (`docs/TRIGGERS.md`).** Until then
   `TriggerDefinition` was dead configuration; now the core `WebhookNotificationHandler` reads, per
   notification, the triggers of the session dialog (`IDialogStore.GetTriggersForSessionAsync`) as well
   and delivers `Kind = Webhook`. Notes for extensions:
   - `Kind = InProcess` delivers **nothing** (host-app handler) – name it in the UI, do not hide it.
   - **Best-effort is mandatory:** unreadable `Config`, a missing URL and non-evaluable conditions are
     logged and skipped. Never throw – the handler runs in the scope of submit/edit.
   - Cross-field rules belong in the command (`IValidatableObject` → `ValidationException` → 400), not
     only in the UI: `AfterQuestion` needs a `QuestionId` exactly there, `Webhook` an absolute URL.
   - As with loops: FK-free question references are cleaned up by `DeleteQuestionCommand`.

6. **Test runner (#43) – implemented.** A dialog run over `IFlirtyEngine` against the active profile,
   each step in a fresh scope (`FlirtyRuntimeGateway`, base `DesignerGateway`). Notes:

   **Drafts need their own start.** `StartDialogCommand` resolves via the business key and starts only
   **published** dialogs. For the runner, `StartDialogVersionCommand` (start of a specific `DialogId`,
   independent of publication) was therefore added – deliberately **without** an ASP.NET endpoint: over
   HTTP the publish status stays the production barrier. Everything from the start on worked unchanged,
   because the session pins its `DialogId`.

   **The run is real.** It writes `DialogSession`/`SessionAnswer` into the profile's database and
   delivers configured webhooks. Per run a fresh `ExternalUserKey` with prefix `designer-test-`
   (otherwise resume applies instead of a new start); no cleanup happens – the engine has no concept of
   deleting sessions. Both belong visibly in the UI, not in a footnote.

   **One contract, one place.** `AnswerValueCodec` is the single source of the JSON encoding per
   `QuestionType` (the core `AnswerValidator` is authoritative); `DesignerExpressionContext.SampleJson`
   derives its sample values from it, so expression validation and test run do not diverge.

   **Scoped state must be adopted.** The `DesignerTriggerLog` is – like `ActiveConnectionProfile` –
   passed into the child scope via `Adopt`; otherwise the `INotificationHandler<T>` constructed there
   would write into a throwaway instance. Applies to **any** further circuit state a gateway needs (hook:
   `DesignerGateway.Prepare`).

   **`iterationIndex` is not a progress counter.** It means the index of the most recently *given* answer
   to the open question (`LoopResolver.ResolveIterationIndex`), not the upcoming iteration – shown as the
   "current iteration" at the current question it would be wrong. The exact indices are on the history
   entries (`SessionAnswerView.IterationIndex`).

   **`[Parameter]` forces `public`.** Razor generates components as `public` classes; `internal`
   parameter types fail with CS0053. That is why `AnswerInputModel`/`AnswerChoice` are the only designer
   models that are `public` (the designer is `IsPackable=false`, no package API arises).

7. **The canvas belongs to the browser (ADR 0006, implemented since #101).** The designer is Blazor
   **Server** – every Blazor event is a SignalR round-trip. The spike for #100 measured what that means
   for a drag gesture: 40 px behind the pointer and 68 messages if the movement runs in C#, versus 0 px
   and 2 messages for the hand-built version. From that follow four rules for **every** canvas extension:

   - **Between `pointerdown` and `pointerup` no message goes to the server.** Movement runs in the
     collocated `*.razor.js` module; only the release calls a command.
   - **Attributes the JS module sets, C# must never render.** The `transform` on `.graph-viewport`
     belongs to the module – a single rendered `transform`, and the next re-render (say a selection)
     resets pan and zoom.
   - **Edges are drawn before the nodes.** Their wide, invisible hit path would otherwise sit over the
     node center and swallow the click. For the same reason the loop frame has `fill="none"` and
     `pointer-events: stroke`. And the `pointerdown` handler has an early `return` on operable elements –
     **without** `preventDefault()`, otherwise Blazor's `@onclick` fizzles.
   - **The canvas sets `data-canvas-ready`** as soon as the module is bound. `InteractWhenReadyAsync`
     does not carry here (see conventions).

   **Numbers in SVG attributes exclusively via `SvgFormat.N`.** The display culture is configurable
   (`en-US` by default) and applies to rendering too: under a comma-decimal culture an interpolated
   `double` coordinate becomes `12,5`, and because the comma is a *separator* in path syntax, a wrong
   number sequence arises – without an exception, without a message, only with a wrong picture.

   **Auto-layout must be deterministic**, otherwise E2E selectors wobble: only lists to the outside
   (never set or dictionary), sort keys end with a unique ordinal (total order instead of borrowed
   `OrderBy` stability), coordinates only from integer layer/column values. The ordinal comes from
   `(Order, Id)` and **not** from the Guid: `CreateDialogVersionCommand` assigns each question a new Guid
   on cloning, a guid-based layout would reshuffle on every new dialog version.

   Node contents are **Razor components in a `<foreignObject>`** – Blazor's namespace check excludes
   `foreignObject`, so child elements arise in the HTML namespace. That makes nodes real `<button>`s:
   focus ring, Enter/space and screen-reader role come from the platform.

8. **Canvas positions live in `DialogLayout`, not on the graph (ADR 0007, implemented since #102).**
   Pattern for every further gesture that writes a position:

   - **Write path without `DialogEditGuard`.** Coordinates do not touch session semantics; a published
     dialog must remain arrangeable. Because `DialogLayout` is its own table, this is not a bypass of the
     publish lock but its boundary. Whoever adds a further layout command sets **no** guard there – and
     writes a test that nails this down.
   - **Dragging in three steps:** `pointerdown` notes the start (no `preventDefault`, no capture) →
     threshold **4 px**, only above it does the click become a drag → `pointerup` swallows the following
     `click` and calls `invokeMethodAsync` **once**. `invokeMethodAsync` belongs exclusively in
     `onPointerUp`.
   - **Screen → user coordinates via `viewport.getScreenCTM().inverse()`**, never via the zoom factor
     alone: the matrix also contains the `viewBox` scaling relative to the CSS width.
   - **Geometry stays in C#.** During the drag, adjacent edges are dimmed (`data-from`/`data-to` on the
     edge `<g>`), not recomputed in the browser – otherwise there would be two sources for the same
     routing logic.
   - **Saved positions take effect only in `GraphLayout.Render`.** Layering, edge shape, barycenter and
     channel assignment stay with the auto-layout: a drag changes a position, not the structure. The
     drawing surface grows to include moved nodes.
   - **The commit does not reload.** The `DialogDetail` lives in a field; the command's response replaces
     `Layout` in it, and the model is rebuilt locally – a `GetDialogQuery` per gesture would be a second
     round-trip for data one already has.
   - **A new `LayoutElementKind` costs two manual branches:** the clone in `CreateDialogVersionCommand`
     (kind-aware, discard non-mappable rows) and the cleanup in `DeleteQuestionCommand`. Both are today
     each secured by a test in `Runtime/DialogLayoutTests` – for the new kind two more are added.

9. **Canvas gestures write – and are not idempotent (ADR 0008, since #103).** Pattern for every further
   gesture:

   - **One gateway call per gesture**, containing all needed commands in dependency order (the layout row
     needs the Guid of the new question). One DI scope, one error path – but **no** transaction: each
     handler saves itself. If a follow-up command fails, it is reported and **not** compensated (deleting
     a just-created question because of a layout hiccup would be the costlier mistake).
   - **After a graph mutation, reload** (`RunGestureAsync` → `LoadAsync`), do not advance locally.
     `Rebuild` stays with the layout path, whose command returns the full state. The reason is the
     graph-wide computed warnings and the co-cleanup of `DeleteQuestionCommand`. Then
     **`ReconcileSelection()`** – a selection on a deleted element otherwise renders an empty inspector
     branch.
   - **Two locks, both needed.** Client-side `send()` in the module; **the promise of `invokeMethodAsync`
     is the receipt**, a second back channel would be a place to forget. Server-side, an early exit on
     `_busy` in **every** `[JSInvokable]` – an interop call sees no rendered `disabled`. A direct
     `invokeMethodAsync` call next to `send()` undermines the lock.
   - **`data-editable` and `data-busy` belong to C#, the JS reads them** – fresh on each gesture, not as
     an `attach` option. Locking is done via `pointer-events`, **never** via `disabled` on the port or
     palette entry: Blazor otherwise re-renders the attribute mid-drag and the pointer capture is lost.
   - **Geometry of a running gesture into C#-rendered placeholders** (`.graph-rubber`, `.graph-ghost`) –
     the module only sets `d` or `x`/`y`/`width`/`height`. No `createElement` in a Blazor container. Both
     need `pointer-events: none`, because the target hit test runs via `document.elementFromPoint` (after
     `setPointerCapture` the `event.target` is the capture element).
   - **`swallowNextClick` belongs on the element whose `click` follows** – for the palette drag that is
     the palette entry, not the canvas. Otherwise each drag additionally triggers the click action.
   - **New interactive elements in the node need their own branch in `pointerdown`** – **before** the
     `.graph-node` check, otherwise the move-drag swallows their gesture; and, as everywhere, without
     `preventDefault`, so that Blazor's `@onclick` (the pointer-free path) carries.
   - **Decision rules belong in a service, not in the `@code` block.** `tests/Flirty.Tests/Designer`
     renders no components (no bUnit) – what lives in the Razor is not testable. Prior art:
     `GraphEditing`, `LoopAnalyzer`, `QuestionFormModel.SuggestKey`.
   - **`[Parameter]` types must be `public`.** The form models stay `internal` and private panel state;
     what goes outward are the records from `Models/GraphEdits.cs`. Responsibility:
     **panel = form, page = commands.**
   - **An inspector panel works without `EditForm`:** raw fields with `@oninput`, required check in the
     save handler, `@key` on the element id. Two measured reasons: `onchange` delivers the value only when
     the field is left and loses it, because the panel is rebuilt after every gesture; and the submit of
     an `EditForm` did not arrive in a panel inside changing `@if` branches.
   - **In the E2E a DOM value proves nothing.** If the first interaction on a freshly rendered field
     fizzles, the typed value is still in the DOM until the next render overwrites it – a
     `ToHaveValueAsync` in that window reports success, and the old value is saved. What is checked is a
     **server-produced** effect, and the repeated unit comprises filling *and* saving. Conversely, a
     gesture that locks its own trigger must not be repeated on its own.

## Build order

**EPIC 7 – completed:** #37 connection profiles ✅ → #38 dialog CRUD UI ✅ → #39 question editor ✅ →
#40 branching editor ✅ → #41 loop editor ✅ → #42 trigger editor ✅ → #43 test runner ✅ →
#46 designer E2E ✅.

**EPIC 11 – visual graph designer (#99, completed):** #100 spike canvas technique ✅ (ADR 0006) →
#101 graph view reading ✅ → #102 layout persistence (table `DialogLayout`) ✅ (ADR 0007) →
#103 editing on the canvas ✅ (ADR 0008) → #104 test run in the graph ✅ →
#105 Playwright E2E of the canvas ✅.

## Conventions

- Blazor components under `Components/` (pages in `Components/Pages/`), keep the server-interactive
  render mode.
- **Component names must not shadow the view types from `Flirty.Runtime.Admin`** – that is why the detail
  pages are called `DialogEditor`/`QuestionEditor`/`TransitionEditor`/`LoopEditor`/`TriggerEditor` and not
  `DialogDetail`/`QuestionDetail`/… (otherwise the generated component type shadows the same-named
  record). Applies equally to an upcoming page for `AnswerOptionDetail`.
- **Live validation needs a raw `<textarea>` with `@oninput`**: on an `InputTextArea`,
  `@bind-Value:event="oninput"` cannot be combined with `@bind-Value:after` (RZ10010), and without
  `oninput` the editor only checks when the field is left.
- Shared UI classes belong in `wwwroot/app.css` (global), not copied into each `*.razor.css`.
- **If a page needs more than the reading width, the content decides – not the route** (#118).
  `main.flirty-content` caps at 1100 px; the cap falls via
  `main.flirty-content:has(.graph-layout) { max-width: none; }` in `app.css`. No second layout and no
  `@layout` on the page: the test runner renders `.graph-layout` only in the graph branch of its toggle,
  its history list should stay narrow. Upward (child → ancestor) there is no cascade in Blazor,
  `:has()` is the tool here – the rule must live **globally** (CSS isolation does not reach into child
  components) and needs `main` in front so its specificity beats the scoped rule.
- Solve confirmations **inline** in the component state, **no** JS `confirm`/`alert` – that would
  otherwise block the Playwright E2E (#46).
- UI texts and docs **in English**. The designer is `IsPackable=false` (no NuGet package) → CS1591 is
  **not** an error here, XML docs are optional.
- E2E tests of the designer belong in `tests/Flirty.E2E` (Playwright, #46). Two pitfalls that
  `docs/DESIGNER.md` § Tests spells out: the in-process hosted designer needs
  `ApplicationName = "Flirty.Designer"` **and** `EnvironmentName = "Development"` (otherwise
  `_framework/blazor.web.js` is missing and nothing is interactive), and after **every** page change the
  first interaction fizzles silently until the circuit has taken over the page – so it is repeated via
  `InteractWhenReadyAsync` and must be idempotent.
- **On the canvas `InteractWhenReadyAsync` does not carry** – dragging and zooming are not idempotent (a
  repeated drag would move twice). There one waits for `svg[data-canvas-ready='true']`; the module sets
  the attribute on binding, and because `OnAfterRenderAsync` does not run at all during prerendering, it
  is at the same time the proof that the circuit has taken over the page. It is the **first** `data-`
  attribute in the designer and deliberately an exception to the otherwise usual selector practice (role,
  heading, field `id`, CSS class).
- **A drag in the E2E needs `ScrollIntoViewIfNeededAsync` and `page.Mouse`.** `DragToAsync` uses the
  HTML5 drag-and-drop API and does not fire at all on an SVG canvas with pointer events; and mouse
  coordinates are window-relative, while the canvas host is 70 vh tall below header, hint and toolbar –
  without scrolling the gesture aims into the void at a node of the lower layers, without any error
  message. Drag over several `Mouse.MoveAsync` steps, so that the 4-px threshold is crossed as in a real
  gesture. Aim in **fractions** of the surface (`DragToCanvasFractionAsync`), not in pixels: the SVG
  scales its `viewBox` into the host, so how big a node is on screen depends on the window – a fixed pixel
  value would hit a node instead of the free surface at a different layout, and the drag into the void
  would silently become a connection.
- **Behind every canvas gesture stands a server-produced effect, no wait time** (#105). `send()` discards
  a second gesture **silently** while the first runs: a movement triggered too early leaves no error, only
  a missing effect. If a canvas test goes red, the first question is therefore "which gesture was
  silently discarded?" – not "which assertion failed?".
- **A non-idempotent action needs a visible precondition instead of a repetition** (#105). When
  connecting via `#inspectorConnect` that is the "Connect" button: it becomes operable exactly when the
  server knows the target. Only the selection is repeated, the click happens once. Otherwise the
  re-render of the node selection overtakes the list selection and discards it (the `@key` on the panel
  replaces the instance) – the same family as "a DOM value proves nothing", just on a `<select>`.
- **Warning texts are a contract.** `TransitionWarningAnalyzer` and `LoopAnalyzer` return `GraphWarning`
  (target + text). The text is the same the list view has always shown – the publish confirmation counts
  it and the E2E suite searches within it. Whoever rephrases changes the surface and must do so
  deliberately (`TransitionWarningAnalyzerTests` nails the wordings). There is **no** second warning
  logic beside these two: graph and list view draw from the same source, located by cause (group property
  → question, property of a transition → its edge).
- **The publish confirmation reads the *whole* graph** (#118), not a single analyzer: the source is
  `DialogGraphModel.AllWarnings` via `GraphWarningList.Describe`, and because reachability arises only
  from the layering starting at the entry question, the `DialogEditor` holds a `DialogGraphBuilder` model
  in a field for this (`_graph`, once per load – **never** in the markup, otherwise the layering would run
  on every click). Whoever adds a new warning kind therefore does **not** need to touch the confirmation;
  whoever narrows the source back to an analyzer rebuilds the defect of #118. And whoever produces a
  warning without a `QuestionId` (`ForDialog`/`ForLoop`) relies on the prefix living in the service and
  not in the `@code` block.
- **The taken path is derived, not stored** (#104). The engine keeps no `TransitionId` on the answer;
  `GraphRunAnalyzer` reads the path from the answer sequence. Parallel transitions between the same two
  questions therefore stay **fundamentally** ambiguous – that is reported, not guessed. Giving the domain
  a column for it would be a schema change and runtime write load for a pure display concern; whoever
  really needs it justifies it in an ADR.

## Definition of Done

Feature works in the server-interactive designer via the admin commands (through `FlirtyAdminGateway`) ·
expressions are validated on save · service tests in `tests/Flirty.Tests/Designer/` ·
extend `docs/DESIGNER.md` for the respective feature · if the change touches a flow of the designer E2E,
update `tests/Flirty.E2E/DesignerE2ETests` too.

## Verification

```pwsh
dotnet build Flirty.sln
dotnet run --project src/Flirty.Designer     # start the designer locally
dotnet test tests/Flirty.Tests
dotnet test tests/Flirty.E2E                # browser coverage (needs Chromium, see docs/DESIGNER.md)
```
