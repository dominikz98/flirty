---
name: flirty-placeholder
description: Add or inspect a message placeholder in Flirty – a {{key}} marker in a question text or answer-option label replaced with live host data at delivery time. Use for "placeholder", "{{key}}", "AddPlaceholder", "IPlaceholderFiller", "greet by name", "fill in a value at delivery", "flirty_placeholder_list", "placeholders.json".
---

# Add a message placeholder

A placeholder is a `{{key}}` marker in a delivered message (a `Question.Text` or an `AnswerOption.Label`)
that the engine replaces with a **live value at delivery time**. The host produces the value in code, on
demand, from wherever it likes. Reference: `docs/PLACEHOLDERS.md`, ADR
`docs/adr/0013-message-placeholders-at-the-projection-seam.md`.

This is a **host extension point**, not an engine change – the same shape as a custom question type
(`flirty-question-type`, ADR 0011). There is nothing to add to the core to declare one.

## Prior art (read before writing)

- `src/Flirty/Placeholders/IPlaceholderFiller.cs` – the filler contract (`FillAsync`).
- `src/Flirty/Placeholders/PlaceholderContext.cs` – the running-session facts a filler gets.
- `src/Flirty/Placeholders/PlaceholderRenderer.cs` – the `internal` scoped renderer at the one seam.
- `src/Flirty/Placeholders/FlirtyPlaceholder.cs` + `FlirtyPlaceholderRegistry.cs` – the declaration + registry.
- `src/Flirty/DependencyInjection/FlirtyOptions.cs` – `AddPlaceholder<TFiller>` / `AddPlaceholder`.
- Worked examples: `src/Flirty.Samples.Web/UserNamePlaceholderFiller.cs`, `TodayPlaceholderFiller.cs`.

## Steps – a host wants a placeholder

1. Declare it in `AddFlirty`:
   ```csharp
   services.AddFlirty(o => o
       .UseSqlite(connectionString)
       .AddPlaceholder<UserNameFiller>("user-name", "User name")
       .AddPlaceholder<TodayFiller>("today", "Today's date", sample: "2026-08-07"));
   ```
   Keys are `[a-z0-9-]`, compared **ordinally**; an empty or duplicate one throws at **declaration** time.
   The `sample` is plain text (a preview value), **not** JSON – unlike a question-type sample.
2. Implement `IPlaceholderFiller` (return `ValueTask<string?>`). It is resolved from the **request scope**,
   so it may take scoped dependencies – the same `FlirtyDbContext` the handler uses, an
   `IHttpClientFactory`, `IOptions`. Read what you need from `PlaceholderContext` (`ExternalUserKey`, the
   `ExpressionContext` with the answers so far, etc.).
3. Put the marker in a message: a `Question.Text` or an `AnswerOption.Label` containing `{{user-name}}` –
   over HTTP, MCP or in the designer. Author-time only; the value is filled on delivery.
4. Nothing else. Every delivery path (facade, HTTP, MCP session tools) fills it, because the replacement
   sits at the single projection seam all five runtime operations share.
5. **Optional, for the Blazor designer**: mirror the declaration as data in `placeholders.json` in the
   designer's ContentRoot, so the marker shows a display name, is offered as an insert chip in the text
   editors, and previews its `sample` in the test runner:
   ```json
   { "placeholders": [ { "key": "user-name", "displayName": "User name", "sample": "Alice" } ] }
   ```
   Read once at startup. It buys **cosmetics and a sample preview, never your filler** – that is code in
   your process, so the test runner shows the sample and says so. Details: `docs/DESIGNER.md`
   § *Message placeholders*.

## To inspect what a host declared

`flirty_placeholder_list` (MCP, `Admin` surface) lists the declared placeholders (key, display name,
sample) – the registry-sourced twin of `flirty_question_type_list`, with no route counterpart. Add it, if
ever a second such tool is needed, by mirroring `Tools/FlirtyPlaceholderTools.cs`: a new const in
`FlirtyToolNames.cs`, a result projection in `FlirtyToolResults.cs` (**no** CLR filler type on the wire), a
`.WithTools<…>()` line in the `Admin` block of `AddFlirtyMcp`, and the golden tool count bumped in
`tests/Flirty.Tests/Mcp/FlirtyToolSurfaceTests.cs` and `MapFlirtyMcpTests.cs`. See `flirty-mcp`.

## Important

Four rules that hold, each a decision rather than an implementation detail:

- **Gated by absence.** With no placeholder declared, `RenderAsync` returns the projected view untouched –
  no scan, no context build, no filler resolution – and the `PlaceholderRenderer` stays `Scoped` either
  way, so nothing's lifetime changes. A dialog without placeholders is byte-for-byte what it was.
- **Best-effort, never fatal.** An unknown key, a filler that throws, or one that returns `null` all
  degrade the single marker to its raw `{{key}}` text and log a warning; nothing breaks
  start/submit/resume/edit. A published dialog cannot be repaired (ADR 0005), so a throw would be
  unrepairable. Only `OperationCanceledException` propagates.
- **Never persisted, one level.** Values are resolved fresh on every delivery; stored answers and the
  admin/config views keep the raw marker. A filled value is not re-scanned for markers.
- **Marker charset is `[a-z0-9-]`.** A token with any other character is not a marker and is left verbatim.
  `{{…}}` was chosen because `${}`/`{}`/`[[]]` all collide with the DynamicExpresso branching syntax.

## Tests

`tests/Flirty.Tests/Placeholders/PlaceholderRendererTests.cs` – the renderer in isolation (fill, degrade,
gated-by-absence, per-key cache, one-level recursion). `PlaceholderRuntimeTests.cs` – end-to-end via
`IFlirtyEngine` (filled over the facade, the filler shares the handler's `FlirtyDbContext`, never
persisted). `DependencyInjection/FlirtyServiceCollectionExtensionsTests.cs` – the DI promises. Designer:
`Designer/PlaceholderDescriptorFileTests.cs`, `DesignerPlaceholdersTests.cs`, `PlaceholderPreviewTests.cs`,
`DesignerAppPlaceholdersTests.cs`. MCP: `Mcp/FlirtyPlaceholderToolsTests.cs` (+ the golden surface tests).
Sample: `Samples/WebSampleTests.cs` and the E2E case in `tests/Flirty.E2E/WebSampleE2ETests.cs`.

## Definition of Done

English XML docs · `docs/PLACEHOLDERS.md` and the cross-linked guides updated · tests green. If the
placeholder should be visible in the designer, the `placeholders.json` entry from step 5. No schema change
is involved (the two text columns are unbounded).

## Verification

```pwsh
dotnet build Flirty.sln
dotnet test tests/Flirty.Tests
```
