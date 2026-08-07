# Message placeholders

A message text (and an answer-option label) can carry a **placeholder** – a `{{key}}` marker that the
engine replaces with **live data at delivery time**. The host resolves the value in code, on demand, from
wherever it likes (a `DbContext`, an `HttpClient`, the session's `ExternalUserKey`, an earlier answer).

Nothing about a dialog *without* placeholders changes: the feature is **gated by absence**, so a host that
never declares one pays no cost at all. See ADR
[0013](./adr/0013-message-placeholders-at-the-projection-seam.md) for why the design is shaped this way.

## Marker syntax

```
Hello {{user-name}}, your order {{order-id}} is on its way.
```

- A marker is `{{`, a `key`, `}}`. The `key` is restricted to lowercase ASCII letters, digits and `-`
  (the same charset as `AddQuestionType`/`AddPlaceholder`). A token with any other character – uppercase,
  underscore, spaces inside the braces – is **not** a marker and is left verbatim.
- `{{…}}` was chosen over `${…}`, `{…}` and `[[…]]` for one reason: the branching layer runs raw C#
  through DynamicExpresso, where `{ }`, `[ ]` and `$` all carry meaning. `{{…}}` collides with nothing.
- The two fields that are filled are **`Question.Text`** and **`AnswerOption.Label`** – both flow through
  the same projection, so covering the one seam covers both. `Dialog.Name`/`Description` are out of scope
  (metadata, not a chat message).

## Registering a filler

A filler is host code that returns a value into the render path. Register it the same way you register a
custom question-type validator (EPIC 14) or a webhook:

```csharp
services.AddFlirty(o => o
    .UseSqlite(connectionString)
    .AddPlaceholder<UserNameFiller>("user-name", "User name")
    .AddPlaceholder<TodayFiller>("today", "Today's date", sample: "2026-08-07"));
```

- `AddPlaceholder<TFiller>(key, displayName, sample?)` declares a placeholder **with** a filler. The filler
  type is registered as `Scoped` and resolved from the request scope, so it may take scoped dependencies –
  including the same `FlirtyDbContext` the handler uses.
- `AddPlaceholder(key, displayName, sample?)` declares one **for display only**, without a filler. A marker
  for a filler-less placeholder degrades to its raw text at delivery time (there is nothing to produce a
  value). This overload exists for the designer, which has no filler code; a host normally uses the generic
  one.
- Keys are unique and validated at configuration time: an empty key, a key outside `[a-z0-9-]`, or a
  duplicate throws `ArgumentException`. Unlike a question-type sample, a placeholder `sample` is **plain
  text** (substituted into a message), so any string is accepted.

## The filler contract

```csharp
public interface IPlaceholderFiller
{
    ValueTask<string?> FillAsync(PlaceholderContext context, CancellationToken cancellationToken);
}
```

`PlaceholderContext` carries the placeholder `Key` plus the running-session facts a host needs to decide
how and from where to resolve the value: `SessionId`, `ExternalUserKey`, `DialogId`, `DialogKey`,
`QuestionKey`, and the **already-built `ExpressionContext`** (the answers so far by question key, the loop
collections, the iteration index and the point in time – the same context a branching condition sees).

```csharp
public sealed class UserNameFiller(FlirtyDbContext db) : IPlaceholderFiller
{
    public async ValueTask<string?> FillAsync(PlaceholderContext context, CancellationToken ct)
        => (await db.Users.FindAsync([context.ExternalUserKey], ct))?.DisplayName;
}
```

**Why an interface and not a `Func<>`:** a delegate cannot take scoped dependencies, which is the whole
point of resolving live data on demand.

## Best-effort – a placeholder never breaks a delivery

A published dialog version cannot be repaired (ADR [0005](./adr/0005-immutable-published-dialog-version.md)),
so a broken filler must not turn every delivery into a failure. Every degradation is logged and leaves the
**raw `{{key}}`** in place so the failure is visible to the author:

| Situation | Result |
|---|---|
| Marker present, key not declared | raw marker retained, one warning |
| Placeholder declared without a filler | raw marker retained, one warning |
| Filler throws | raw marker retained, one warning (the exception is logged) |
| Filler returns `null` | raw marker retained, one warning |

One misbehaving placeholder does not poison the rest of the text – the other markers still fill. Only an
`OperationCanceledException` propagates: that is a genuine cancellation of the delivery, not a placeholder
failure. Recursion is **one level** – a filled value is never re-scanned for markers. Within a single
delivery a key is resolved at most once (its value is cached), so the same marker in the text and in an
option label is consistent and costs one filler call.

## Where it happens

The replacement is an `internal` scoped `PlaceholderRenderer` that wraps the shared
`QuestionProjection.ResolveQuestion`. The five runtime operations (`StartDialogCommand`,
`StartDialogVersionCommand`, `SubmitAnswerCommand`, `EditAnswerCommand`, `ResumeDialogQuery`) call
`renderer.RenderAsync(dialog, session, questionId, ct)` where they resolved the question before, so every
delivery path – the facade `IFlirtyEngine`, the HTTP endpoints and the MCP session tools – is covered at
once (see [RUNTIME.md](./RUNTIME.md)).

**Gated by absence:** if no placeholder is declared the renderer returns the projected view untouched – no
scan, no context build, no filler resolution – and it stays `Scoped` whether or not anything is declared,
so nothing's lifetime changes. **Never persisted:** values are resolved fresh on every delivery; the stored
`SessionAnswer` rows and the admin/config views (e.g. `flirty_dialog_get`) keep the raw marker.

## In the designer

The designer runs no host filler – a filler is host-process code – so it learns placeholders from an
optional **`placeholders.json`** in its content root, read at startup and declared through the real
(filler-less) `o.AddPlaceholder(...)`. A bad entry is skipped and reported rather than crashing the app.

```json
{
  "placeholders": [
    { "key": "user-name", "displayName": "User name", "sample": "Alice" },
    { "key": "today", "displayName": "Today's date", "sample": "2026-08-07" }
  ]
}
```

- The read-only page **`/placeholders`** lists the declarations (marker, display name, sample) and the file
  path, and states the limit: the designer previews the sample, not live data.
- The three text editors (question editor, the new-question form in the dialog editor, and the graph
  inspector) offer an **insert affordance** – clickable chips that append a declared `{{key}}` marker.
- The **test runner** previews markers from the declared **sample** and says so on screen. The engine's
  own delivery leaves them raw (the designer declares no filler), and the runner fills from the sample for
  display; the submitted answer value is untouched, so a run submits exactly what production would. See
  [DESIGNER.md](./DESIGNER.md).

## Over MCP

`flirty_placeholder_list` (Admin surface) lists the declared placeholders – the registry-sourced twin of
`flirty_question_type_list`, with no `MapXxxEndpoints` counterpart. The result is a projection of key,
display name and sample; the CLR filler type is server-side only and never reaches the wire. The session
tools deliver filled text unchanged (they pass `QuestionView.Text` through), and `flirty_dialog_get` keeps
returning the raw marker (authoring view). See [MCP.md](./MCP.md).
