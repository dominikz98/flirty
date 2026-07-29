# ADR 0007 – Canvas layout as its own table, with a guard-free layout command

- **Status:** Accepted
- **Context issue:** #102 – Layout persistence (table `DialogLayout`) + moving nodes
- **Affected:** `src/Flirty/Domain/`, `src/Flirty/Persistence/`, `src/Flirty/Runtime/Admin/`,
  `src/Flirty.Migrations.*`, `src/Flirty.AspNetCore/`, `src/Flirty.Designer/`

## Context

The graph view from stage 1 of EPIC 11 (#101) arranges the dialog via a deterministic
auto-layout (`GraphLayout`, "Sugiyama-light"). For a *view* that carries. As soon as the canvas becomes an
*editor*, it no longer carries: the arrangement is the algorithm's, not the author's, and there
is no way to correct it. `Question` has no coordinates.

Two boundary conditions shape the decision:

- **Versioning is the only way to evolve a published dialog** (ADR 0005).
  `CreateDialogVersionCommand` assigns each question a new `Guid` on cloning and does not expose the
  internal `questionIdMap` to the outside. Whatever hangs positions on question guids must therefore go
  through this rebuild – from the inside.
- **`DialogEditGuard` locks 16 places** – the 15 graph commands and the change of the entry question in
  `UpdateDialogCommand` (ADR 0005). If writing coordinates ran through one of those,
  a published dialog could not even be arranged clearly: every move would respond with
  `409`. But the productive dialog is exactly the one that is looked at most often.

## Decision

**Positions live in their own table `DialogLayout`**, not on a graph entity:

| Column | Type | Note |
|---|---|---|
| `Id` | `Guid` | PK |
| `DialogId` | `Guid` | FK on `Dialog`, **Cascade** |
| `ElementKind` | `LayoutElementKind` (`int`) | initially only `Question` |
| `ElementId` | `Guid` | FK-free, like the question references in `LoopDefinition` |
| `X`, `Y` | `int` | Canvas coordinates, never negative |

Unique over (`DialogId`, `ElementKind`, `ElementId`). **Without a row the auto-layout takes effect** – that is
at the same time the return: `ResetDialogLayoutCommand` deletes the rows, "reset layout" needs nothing
more.

Writing happens via `SetDialogLayoutCommand` – a **batch upsert**: named elements are
created or updated, unnamed ones stay. Over HTTP that is `PUT .../dialogs/{id}/layout`
(the merge semantics are in the XML doc and in the WebAPI guide, because `PUT` would otherwise read as a full replacement);
`DELETE .../dialogs/{id}/layout` resets.

**Both layout commands deliberately do not run under `DialogEditGuard`.** Coordinates do not touch the
session semantics: sessions pin `DialogId`/`DialogVersion` and follow guids, not pixels. With
its own table this is not a circumvention of the publish lock from ADR 0005, but its **edge** –
the command writes into something that is not part of the graph. ADR 0005 holds unchanged; its 16
call sites remain untouched.

Two branches are **manual work** here and each nailed down by a test:
`CreateDialogVersionCommand` clones the rows and rewrites `ElementId` via the `questionIdMap` (a
row with no mappable element is discarded, not dragged along); `DeleteQuestionCommand` cleans up
referencing rows, because `ElementId` is FK-free.

In the designer the positions take effect at **one** place: right at the end of `GraphLayout.Render`, where the
node boxes arise. Layering, edge shape, barycenter and channel assignment stay with the auto-layout.
A move therefore changes only the position of a node – never the drawing shape of an edge and never
the arrangement of the rest.

## Discarded alternatives

**Don't store at all, re-arrange every time.** No schema change, no command, no clone branch. For
the reading view that was the decision of #101 and correct. For an editor it is not:
every arrangement the author produces would be gone on the next call – and without moving the
canvas stays a prettier list. The follow-up stages (#103 editing, #104 test run in the graph) presuppose a
stable arrangement.

**A designer-local JSON file** next to `connection-profiles.json`. Likewise without a schema change and built
in an hour. Two reasons against, the second is the hard one: the layout would hang on the *machine* instead
of on the dialog – two authors of the same dialog would see different pictures, and a change of the
connection profile or the workstation would discard it. And: **it does not survive versioning.**
Because the clone gives each question a new `Guid` and `CreateDialogVersionCommand` does not hand out the
mapping, every new version – by ADR 0005 the only way to change a productive dialog – would start
with a discarded layout. Exactly at the moment the author wants to continue working.

**`LayoutX`/`LayoutY` (`int?`) on `Question`.** The cheapest viable variant: two nullable
columns, no separate aggregate, no command set – and the cloning as well as the cleanup take care of
themselves, because the values hang on the question. Ruled out for three reasons. First, it mixes a
pure **display concern** into a graph entity that otherwise describes only flow; the
runtime would read columns it never needs. Second, it caps extensibility: positions for
something other than a question – edge waypoints, note nodes, a viewport – would then not exist without
another reach into a graph entity. Third, and this tipped the scales: a write path that skips the
publish lock would be a **convention** with this variant – a field of a locked entity
that one writes past the guard, and every future `UpdateQuestion` path would have to know the exception.
With its own table the same freedom is **structural**: there is simply nothing
locked to circumvent.

**Put the layout under `DialogEditGuard` too** and in return forgo moving on published dialogs.
Thought through consistently – but the productive dialog is the one that is opened most often,
and a canvas that freezes precisely there is half the function. The way out "first
derive a new version, then arrange" would produce a dialog version for a screen arrangement.

**`PUT` as a full replacement** (the client always sends all positions). More REST-clean and without the
explanatory burden of the merge semantics. Ruled out, because a drag gesture moves one element: it would
then have to send the entire layout along, and two authors on the same dialog would overwrite each other's
positions that they did not even touch.

## Consequences

**Positive**

- Nodes are movable, and the position survives reload, restart **and** the derivation of a new
  dialog version. Without a row the deterministic auto-layout takes effect again.
- Moving works on the published dialog, without the publish lock softening – proven
  by one test each at handler and HTTP level, with the counter-check that a real graph change on
  the same dialog still returns `409`.
- The graph entities stay free of display data; a second `LayoutElementKind` (waypoints, notes,
  viewport) costs one enum line and no schema change on `Question`.
- Exactly **one** server message per drag gesture: the JS module writes during the drag only the
  node's `transform` and calls `MoveNodeAsync` only on release (ADR 0006).

**Negative**

- Cloning and cleanup are manual work – two branches one forgets on the next element type. They
  hang on one test each (`DialogLayoutTests`), but a test protects only what it knows.
- One aggregate, one command pair, two endpoints and one migration per provider more than with
  `LayoutX`/`LayoutY`.
- `ElementId` is FK-free: the database does not prevent an orphaned row. The cleanup branch is the
  belt, the skipping of unknown elements in `GraphLayout` the braces.
- During a drag the edges are briefly wrong. They are dimmed instead of recomputed in the browser –
  their geometry arises in C# and is tested there, a second source for it would be more expensive than the
  inaccuracy for one drag's duration.

**Open**

- **No undo.** A drag writes immediately; back it goes via a second drag or "reset
  layout" (which discards *all* positions, not the last one).
- **No multi-selection.** The command can do a batch, the surface today always produces one
  entry.
- **No viewport, no waypoints.** The table carries both, nothing is pre-built – a viewport
  would need a row without an element, waypoints additionally a `Sequence` column.

Details: [DOMAIN-MODEL.md](../DOMAIN-MODEL.md), [PERSISTENCE.md](../PERSISTENCE.md),
[DESIGNER.md § Graph-Ansicht](../DESIGNER.md#graph-view-101).
