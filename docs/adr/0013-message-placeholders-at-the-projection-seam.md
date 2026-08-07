# ADR 0013 – Message placeholders filled at the single projection seam

- **Status:** Accepted
- **Context issue:** #140 – feat: message placeholders replaced with live data at delivery time
- **Affected:** `Flirty` (`Placeholders/*`, `DependencyInjection/FlirtyOptions.cs`, the five runtime
  handlers), `Flirty.Designer`, `Flirty.Mcp`, `Flirty.Samples.Web`

## Context

A delivered question text was copied verbatim into the `QuestionView` a client renders. A host had no way
to say "greet the user by name", "show today's opening balance" or "fill in the order number this session
is about": the value is not known at authoring time, and a **published dialog version is immutable**
(ADR 0005), so it cannot be patched later either.

There is exactly **one** place in the repository where a delivered text is produced:
`QuestionProjection.ResolveQuestion` (`new QuestionView(` is a single hit repo-wide). All five runtime
operations (`StartDialogCommand`, `StartDialogVersionCommand`, `SubmitAnswerCommand`, `EditAnswerCommand`,
`ResumeDialogQuery`) route through it; the HTTP layer and the MCP session tools are pure pass-throughs of
`QuestionView.Text`. But that seam is `static`, synchronous, and receives only `(Dialog, Guid?)` – and a
live-data source is I/O by nature and needs the running session.

## Decision

- **Replace at the one seam, turned async and session-aware.** An `internal` scoped `PlaceholderRenderer`
  wraps the unchanged `ResolveQuestion` and fills `{{key}}` markers in the question text and every option
  label. The five handlers call `renderer.RenderAsync(dialog, session, questionId, ct)` where they called
  the projection before – one change covers every delivery path at once.
- **Gated by absence.** With no placeholder declared the renderer returns the projection untouched – no
  scan, no context build, no filler resolution. A dialog without placeholders is byte-for-byte what it was,
  and the renderer stays `Scoped` whether or not anything is declared, so nothing's lifetime changes.
- **The filler hangs in DI, the EPIC-14 pattern.** `IPlaceholderFiller.FillAsync(PlaceholderContext, ct)`
  is registered via `o.AddPlaceholder<TFiller>(key, displayName, sample?)` and resolved from the **request
  scope**, so it shares the handler's `FlirtyDbContext`. `PlaceholderContext` carries the running-session
  facts (`SessionId`, `ExternalUserKey`, `DialogId`/`DialogKey`, `QuestionKey`) plus the already-built
  `ExpressionContext`.
- **Marker syntax `{{key}}`**, `key` restricted to `[a-z0-9-]` (mirroring `AddQuestionType`).
- **Best-effort.** An unknown key, a filler that throws, or one that returns `null` all degrade the single
  marker to its raw `{{key}}` text and log a warning; nothing breaks start/submit/resume/edit. Values are
  never persisted, and recursion is one level (a filled value is not re-scanned).
- **The designer previews from the `sample`.** It becomes a host via an optional `placeholders.json`
  declared through the same (filler-less) `AddPlaceholder`, and the test runner fills markers from the
  declared sample, stating on screen that it is a sample rather than live data.

## Discarded alternatives

- **A pipeline behavior as the seam.** A behavior hangs off the command/response, but the text only exists
  *after* the handler projects it – the replacement belongs in the projection, not the behavior.
- **A plain `Func<>` filler.** It cannot take scoped dependencies, which is the entire point of resolving
  live data on demand from a `FlirtyDbContext` or an `HttpClient`.
- **`${key}` / `{key}` / `[[key]]` marker syntaxes.** The branching layer runs raw C# through
  DynamicExpresso, where `{ }`, `[ ]` and `$` all carry meaning; an overlapping syntax would be a trap the
  moment a token is reused in a condition. `{{…}}` (Mustache/Handlebars convention) collides with nothing
  in the codebase.
- **Persisting the resolved value / templating stored answers.** Placeholders are a delivery-time concern;
  the stored `SessionAnswer` rows and the admin/config views keep the raw marker.
- **JSON-Schema-style, nested, or multi-level recursive placeholders.** Out of scope; one level,
  best-effort.
- **The designer executing the real filler.** Impossible and unwanted – a filler is host-process code. The
  designer shows samples, exactly as ADR 0012 decided for question types.
- **Replacing in `Dialog.Name`/`Description`.** Out of scope: designer/overview metadata, not a chat
  message.

## Consequences

- **Positive:** every delivery path (facade, HTTP, MCP session tools) is covered by one seam; a host that
  never uses placeholders pays nothing (no lifetime change, no async continuation); the MCP surface gains a
  single registry-sourced listing tool (`flirty_placeholder_list`), the twin of `flirty_question_type_list`.
- **Negative:** the two user-facing texts (`Question.Text`, `AnswerOption.Label`) are now scanned per
  delivery when at least one placeholder is declared – bounded by a compiled regex and skipped entirely
  when no marker is present.
- **Open:** the designer test run previews the declared sample, not the host's live value, because it runs
  no filler – stated on the `/placeholders` page and beside the runner. Closing it would require running
  host code in the designer, which ADR 0012 already ruled out for question types.

Details: [PLACEHOLDERS.md](../PLACEHOLDERS.md).
