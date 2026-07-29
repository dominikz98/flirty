# ADR 0008 – Editing gestures on the canvas: existing commands, reload, a lock per gesture

- **Status:** Accepted
- **Context issue:** #103 – Editing on the canvas (dragging building blocks, connecting, loops, triggers)
- **Affected:** `src/Flirty.Designer/` (**no** core code, **no** schema change)

## Context

With stage 3 of EPIC 11 the graph canvas turns from a viewer into an editor: building blocks are dropped
from a palette, nodes are connected at the source port, transitions are ordered and deleted, loops are
created from a cycle, triggers are added. Until then the designer had **one** write path – the forms – and
the graph view was read-only.

Three constraints shape the decision:

- **The form path stays fully intact** (a promise of EPIC 11). That means there are two surfaces for the
  same data, and the question is not *whether* they agree, but *by what means*.
- **The canvas writes from a `[JSInvokable]`.** The form path's lock is exclusively the rendered
  `disabled` attribute – a call from the JS module sees none of it.
- **ADR 0007 says "the commit does not reload"** and justifies that for the move gesture. Whether this
  sentence also holds for graph changes is exactly the question to be decided here.

## Decision

**1. Every gesture calls the existing admin commands.** The canvas gets no CRUD of its own and no
command of its own in the core. A palette drop is `CreateQuestionCommand` + `SetDialogLayoutCommand`, a
connection is `CreateTransitionCommand`, a resort is several `UpdateTransitionCommand` – all in **one**
`FlirtyAdminGateway.ExecuteAsync` call, i.e. one DI scope with one error path. The computation rules that
previously sat privately in the `@code` block of `DialogEditor.razor` (next `Order`, next `Priority` per
source question, position index → `Priority`, back-jump detection) now live in `Services/GraphEditing.cs`
respectively `Services/LoopAnalyzer.cs` and are used by **both** views.

**2. After a graph mutation there is a reload** (`GetDialogQuery`), not a local write-through. That is a
deliberate **restriction** of ADR 0007: its sentence still holds, but only for the layout path, whose
command returns the complete new layout. The graph commands return only their own slice;
`DeleteQuestionCommand` additionally cleans up transitions, loop markers, triggers and layout rows. But
the decisive point is the **warnings**: `TransitionWarningAnalyzer` and `LoopAnalyzer` compute over the
*whole* `DialogDetail`. A new transition can lift "No default transition" on another question, a deleted
one can produce unreachability at several nodes. The acceptance criterion "the warnings update
immediately" is only true with the real server state.

**3. Gestures are not idempotent, so there is a two-stage lock.** A double drop created two questions, a
double connection gesture two transitions:

- **Client-side** every message runs through a `send()` helper in the JS module that locks until the
  .NET method returns. **The promise of `invokeMethodAsync` is the receipt** – Blazor Server fulfills it
  when the call is complete. A second back channel would be a place one can forget.
- **Server-side** every writing operation begins with an early exit on `_busy` (`RunGestureAsync`). The
  client lock is an operability promise and bypassable; the server gate is the invariant. Taking it alone
  would silently swallow the second *legitimate* gesture of a fast user.

**4. The read mode is rendered, not disabled.** For a published dialog, ports do not arise at all, and the
palette is locked. The JS module learns the state via `data-editable` on the `<svg>` – an attribute that
**C# owns and the module only reads**, fresh on each gesture. That is the flip side of the rule from
ADR 0006 ("what the JS sets, C# never renders"), not its breach. An `attach` option would be frozen;
`MoveNodeAsync` deliberately does not check `data-editable`, because moving stays allowed (ADR 0007).

**5. The geometry of a running gesture lives in C#-rendered placeholders.** The rubber band
(`.graph-rubber`) and the drop preview (`.graph-ghost`) stand as empty elements in the markup; the module
only sets and clears their geometry (`d` respectively `x`/`y`/`width`/`height`). DOM created via
`createElement` in a Blazor-managed container would throw the renderer off over the child indices on the
next diff.

## Discarded alternatives

| Alternative | Why not |
|---|---|
| **Local write-through of the `DialogDetail`** instead of reloading | The client would have to rebuild `DeleteQuestionCommand`'s co-cleanup – literally the "second truth" the issue forbids. Worse still: a hand-patched `DialogDetail` that misses a cascade produces *wrong* warnings. A second round-trip after the write is the cheaper price, and it falls per gesture, not per pointer step. |
| **An aggregate command in the core** (`CreateQuestionWithLayoutCommand`) that writes question, position and transition in one transaction | Would be transactionally cleaner, but would make a **designer-specific operation into an engine API**. The engine knows no canvas; `DialogLayout` is deliberately not part of the graph (ADR 0007). The price of the rejection is known and borne: if the second message fails, the question exists without a position and the auto-layout places it – degraded, but consistent. Deliberately **no** compensation by deleting: removing a just-created question because of a layout hiccup would be the more expensive mistake. |
| **HTML5 drag-and-drop for the palette** | The palette is HTML outside the SVG, where DnD would be the native idiom. But: it would be a **second event model** with a second place for the lock, the drop event does not deliver the position in user coordinates (`getScreenCTM().inverse()` would have to be rebuilt), and the ghost belongs to the browser instead of the drawing surface. One model per gesture, one lock. |
| **Undo/redo stack** | Would need inverse commands for every operation – and for `DeleteQuestionCommand` with its cascade there is none. Deliberately left out (as already recorded in #99); in exchange, deletion is confirmed in two stages. |
| **A native context menu on the node** (`contextmenu` event), as the issue text suggests | Would need JS positioning, a focus trap and escape handling, would not be keyboard-operable and only hard to drive from Playwright – and the designer forbids blocking browser dialogs anyway (rationale on `.confirm` in `app.css`). Triggers therefore arise via a section in the inspector. The purpose of the criterion – "triggers addable on the node" – is met. |
| **Embedding the four `@page` editors into the inspector** | They have their own `PageTitle`, their own heading and their own back link; embedding them would mean rebuilding them. Instead the inspector gets **its own panels** that call the same commands. The boundary runs along the data shape: scalar fields in the panel, their own substructure (answer options, validation rules, raw JSON) in the full editor. |
| **`disabled` on port and palette entry during a gesture** | Blazor then re-renders the attribute mid-drag, and the pointer capture is lost. Locking is done via `data-busy` on the `<svg>` and `pointer-events` – that touches no attribute on the node. |

## Consequences

**Good:**

- There is **one** write path into the engine. What happens on the canvas stands immediately in the list
  view – not because both are synchronized, but because they do the same thing.
- The computation rules are **testable** for the first time: the designer has no bUnit, but `GraphEditing`
  and `LoopAnalyzer.UnmarkedBackJumps` are pure functions over `DialogDetail`. Before, they sat in a
  `@code` block and were covered by no test.
- The expression editor exists as a component (`ExpressionField`) instead of in three copies. Side finding
  of the merge: `.expr-status`/`.expr-caret` sat scoped in the `TransitionEditor` – in the `TriggerEditor`
  the live status was **unstyled** since #42.

**Costs:**

- **One additional `GetDialogQuery` per mutating gesture.** The busy window thereby comprises writing
  *and* reading and is visibly longer on a slow circuit. That is the intended honesty toward a race; made
  visible via `data-busy` and `cursor: progress`.
- **Two locks must fit together.** Whoever adds a new gesture must serve both ends: the `send()` helper in
  the module and `RunGestureAsync` in the page. A direct `invokeMethodAsync` call alongside `send()`
  undercuts the lock.
- **A known, bounded window remains:** the promise resolves before the render batch is applied. A click in
  this sub-frame window works on old DOM – caught by the server gate, documented here instead of
  engineered away.
- **The inspector's panels are a third form location.** They are deliberately kept narrow (only scalar
  fields), but the boundary must be redrawn on every extension.

**Not decided** (stays open): undo/redo, multi-selection, edge routing with waypoints, triggers on
permanently displayed scope markers (they still arise only *if* they carry triggers – a promise from #101,
nailed down by a test on `MinY == 0`).
