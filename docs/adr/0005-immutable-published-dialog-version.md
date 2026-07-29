# ADR 0005 – Published dialog versions are immutable

- **Status:** Accepted
- **Context issue:** #95 – findings from the manual acceptance pass (versioning, designer UI)
- **Affected:** `src/Flirty/Runtime/Admin/`, `src/Flirty/Persistence/IDialogAdminStore.cs`,
  `src/Flirty.AspNetCore/FlirtyAdminEndpointRouteBuilderExtensions.cs`,
  `src/Flirty.Designer/Components/Pages/`

## Context

Since #17 the domain model carries the building blocks of versioning: `Dialog.Version`, the
unique index `(Key, Version)` and `DialogSession.DialogVersion`. On this rested a promise that stood in
`ARCHITECTURE.md`, `DOMAIN-MODEL.md`, `RUNTIME.md`, `DESIGNER.md` and `CLAUDE.md`: *sessions
pin their dialog version, editing published dialogs does not break running sessions.*

An acceptance pass against the running application (#95) showed that the promise did not hold:

- `Version` was set to `1` only on creation, no command counted it up. A
  second dialog with the same `Key` was rejected (`CreateDialogCommand` checked only the key).
  **So there was no way to a second version at all** – the field was dead ballast.
- The runtime loads a session's graph via `IDialogStore.GetDialogAsync(dialogId)`, i.e. from
  the **same row** that the admin CRUD changes in-place. The pinning could therefore never take effect.
- In practice: if the currently open question of a published dialog is deleted,
  `GET /flirty/sessions/{id}` **and** the submit answer with `409` – the session is neither resumable nor
  readable.

The decision was therefore whether to fulfill or withdraw the promise.

## Decision

The promise is **fulfilled**. Three rules apply for this:

1. **A published version is immutable.** Every change to its configuration graph –
   questions, answer options, transitions, loop markers, triggers and the entry question – is rejected with
   `DialogPublishedException` (→ HTTP `409`). This is enforced by
   `DialogEditGuard.EnsureEditable*`, called as the first precondition of the 15 graph commands and in the
   `UpdateDialogCommand` when the entry question changes. **Name and description stay changeable** –
   they are purely descriptive and affect no flow.
2. **Evolution goes through a new version.** `CreateDialogVersionCommand`
   (`POST {prefix}/dialogs/{id}/versions`) clones the graph with new Guids as a **draft** with the
   next version number and rewrites all question references to the copies. Publishing is
   separate; `PublishDialogCommand` retires the previously productive version of the same key,
   so that at most one version per key is published.
3. **A version with running sessions is not deleted.** `DeleteDialogCommand` refuses as long as
   sessions with `InProgress` exist (the message names the count);
   `AbandonDialogSessionsCommand` ends them beforehand on request (status `Abandoned`, answers and
   history are preserved). Deletion is the one case the pinning cannot cover – without a
   graph there is no flow left.

The proof exists as a test: `DialogVersioningTests` plays in
`Laufende_Session_ueberlebt_eine_neu_veroeffentlichte_Version` a session to completion on version 1
while version 2 is derived, changed and published – and proves that a new
user lands on version 2.

## Discarded alternatives

**Withdraw the promise and only document it.** The cheapest variant: docs and designer would say in future
that changes to published dialogs can break running sessions. Ruled out, because the
promise belongs to the core of the product – a dialog runs over days, and the README lists
"dialog versioning" as a feature. Moreover `Dialog.Version`, `(Key, Version)` and
`DialogSession.DialogVersion` would stay permanently functionless: ballast that pretends a capability.

**Only lock, without a clone function.** Reject graph changes to published dialogs and always route the way
through *retire → change → publish*. Prevents accidental breaking, but
does not solve the problem: during editing no one can start the dialog, and after the
retirement the changes lie again on the same row the running sessions hang on –
so they still break, only later. Too little for a tool that maintains productive dialogs.

**Copy-on-write on first access.** An `UpdateQuestionCommand` on a published version
would have implicitly created a new version and made the change there. Convenient, but the response
of a `PUT .../questions/{id}` would suddenly have affected a **different** Id than the route named, and the
caller would have unnoticed created a second version. Such a surprising API semantics weighs heavier
than the saved click; the derivation therefore stays an explicit step.

**Make sessions immutable instead of dialogs** – e.g. by freezing the graph into the session
(snapshot as JSON). Solves the same problem and also does not break on deletion. Ruled out, because it
doubles the data model (configuration lies in tables *and* in every session), answers could no longer
point at questions by foreign key and the designer would no longer have a view of running sessions. The
effort is out of all proportion to the clone of a dialog row.

**Remove sessions along with deletion** (cascade), instead of refusing deletion. Prevents orphans,
but destroys the answer data – usually the actual yield of a dialog. The engine deliberately knows
no deletion of sessions; that stays so.

## Consequences

**Positive**

- The promise from four guides and `CLAUDE.md` now actually holds and is secured by tests.
- The designer mirrors the rule: locked editors, a banner with the way out and the button
  "Create new version"; the delete lock shows the count of running sessions along with an abandon action.
- `Dialog.Version` and `(Key, Version)` have a function; a key's version series is visible in
  the dialog list.

**Negative**

- More steps for the user: a correction to a productive dialog requires deriving,
  changing, publishing. For a typo in a question text this is more effort than before.
- Every version is a complete copy of the graph – with many versions the database grows.
  There is no cleanup of old versions (deletion is manual and locked by running sessions).
- The 15 graph commands each carry an additional database access for the check.

**Open**

- **Renaming a versioned family.** The `Key` identifies the family; changing it on only one of
  several versions would tear the series apart and is rejected. A "rename all versions"
  does not yet exist.
- **Comparing two versions** (diff) and **rolling back** to an earlier version are not
  implemented – both could be retrofitted without a model change (clone of the older version + publish).
- **Cleaning up** old drafts/versions is manual work via the dialog list.

Details: [RUNTIME.md § Version pinning](../RUNTIME.md#version-pinning),
[DESIGNER.md § Versioning](../DESIGNER.md#versioning-95).
