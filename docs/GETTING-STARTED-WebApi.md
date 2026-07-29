# Getting Started – WebAPI (Flirty.AspNetCore)

> Status: Issues #35 (runtime endpoints) & #36 (admin CRUD), extended by the loop endpoints (#41)
> and the trigger endpoints (#42). This guide shows how to expose the Flirty engine as an HTTP API – via the
> optional package [`Flirty.AspNetCore`](../src/Flirty.AspNetCore) and the extension method
> `MapFlirtyEndpoints`. The endpoints are a **thin layer over the Mediator commands**: they send
> the runtime commands directly via `ISender` and map the results onto serializable DTOs. The core
> (`src/Flirty`) stays deliberately ASP.NET-free in doing so (see [ARCHITECTURE.md](./ARCHITECTURE.md)).

## Project setup

`Flirty.AspNetCore` brings the endpoints along and references the ASP.NET Core shared framework via
a `FrameworkReference` (not as a NuGet package). A host app needs only the two references:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <ItemGroup>
    <!-- EF Core, the SQLite/SqlServer/PostgreSQL providers, and Mediator come transitively via the core. -->
    <ProjectReference Include="..\Flirty\Flirty.csproj" />
    <ProjectReference Include="..\Flirty.AspNetCore\Flirty.AspNetCore.csproj" />
  </ItemGroup>
</Project>
```

> As a NuGet consumer, use `dotnet add package Flirty` **and** `dotnet add package Flirty.AspNetCore` instead.

## 1. Registration (`AddFlirty` + `MapFlirtyEndpoints`)

`AddFlirty(o => …)` wires up the complete stack (Mediator, runtime, persistence, expression engine,
validation). `MapFlirtyEndpoints(prefix)` registers the HTTP endpoints as a minimal-API route group.
Both building blocks are visible without an extra `using` (the extensions live in
`Microsoft.Extensions.DependencyInjection` and `Microsoft.AspNetCore.Builder` respectively):

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddFlirty(o =>
{
    o.UseSqlServer(builder.Configuration.GetConnectionString("Flirty")!);
    o.ApplyMigrations();                 // optional: auto-migration on startup
});

var app = builder.Build();

app.MapFlirtyEndpoints("/flirty");       // the default prefix is likewise "/flirty"

app.Run();
```

`MapFlirtyEndpoints` returns the created `RouteGroupBuilder` – so the group can be configured further,
e.g. `app.MapFlirtyEndpoints("/flirty").RequireAuthorization();`.

## 2. Endpoints

All endpoints map 1:1 onto the runtime commands/query (see [RUNTIME.md](./RUNTIME.md) and
[MEDIATOR.md](./MEDIATOR.md)). `sessionId`/`questionId` sit in the route, the remaining fields in the
body. Answer values are **raw JSON text** (format per question type, e.g. `"dev"` for a choice).

| Method & route | Command/query | Success |
|---|---|---|
| `POST /flirty/sessions` | `StartDialogCommand` | `201 Created` + `Location`, `StartSessionResponse` |
| `GET /flirty/sessions/{id}` | `ResumeDialogQuery` | `200 OK`, `SessionStateResponse` |
| `POST /flirty/sessions/{id}/answers` | `SubmitAnswerCommand` | `200 OK`, `SubmitAnswerResponse` |
| `PUT /flirty/sessions/{id}/answers/{questionId}` | `EditAnswerCommand` | `200 OK`, `EditAnswerResponse` |

> **Deliberately without an endpoint:** `StartDialogVersionCommand` (#43) starts a **specific dialog
> version regardless of the publication status** and is reachable only via the facade
> `IFlirtyEngine.StartDialogVersionAsync` (used by the
> [designer's test runner](./DESIGNER.md#test-runner-43)). Over HTTP the publish status stays the
> production barrier – a draft should not go live via a request. Details:
> [RUNTIME.md](./RUNTIME.md#startdialogversioncommand-43).

### Start a dialog (or resume it)

```http
POST /flirty/sessions
Content-Type: application/json

{ "dialogKey": "onboarding", "externalUserKey": "user-42" }
```

```jsonc
// 201 Created, Location: /flirty/sessions/8f3e…
{
  "sessionId": "8f3e…",
  "isResumed": false,
  "currentQuestion": {
    "id": "1a2b…", "key": "role", "text": "Which role?", "type": 0,
    "options": [ { "id": "…", "key": "dev", "label": "Developer", "value": "dev" } ]
  }
}
```

If a running session of the current dialog version already exists for the user, it is resumed
(`isResumed: true`) instead of creating a new one.

### Submit an answer

```http
POST /flirty/sessions/8f3e…/answers
Content-Type: application/json

{ "questionId": "1a2b…", "value": "\"dev\"" }
```

```jsonc
// 200 OK
{ "sessionId": "8f3e…", "isCompleted": false, "nextQuestion": { "key": "devDetail", … } }
```

If the dialog is completed after the answer, the response delivers `"isCompleted": true` and
`"nextQuestion": null`.

### Read state (resume after reload)

```http
GET /flirty/sessions/8f3e…
```

```jsonc
// 200 OK
{
  "sessionId": "8f3e…", "status": 0,          // 0 = InProgress, 1 = Completed, 2 = Abandoned
  "currentQuestion": { "key": "devDetail", … },
  "answers": [ { "questionKey": "role", "value": "\"dev\"", "sequence": 0, … } ]
}
```

### Edit an earlier answer

```http
PUT /flirty/sessions/8f3e…/answers/1a2b…
Content-Type: application/json

{ "value": "\"pm\"" }
```

```jsonc
// 200 OK – downstream answers are discarded, the path is recomputed
{ "sessionId": "8f3e…", "isCompleted": false, "nextQuestion": { "key": "pmDetail", … }, "invalidatedAnswers": 1 }
```

The optional body value `iterationIndex` edits, within a loop, the answer of a specific
iteration in a targeted way (see [LOOPS.md](./LOOPS.md)).

## 3. Error mapping

Exceptions thrown by the engine are mapped uniformly onto `ProblemDetails` via an endpoint filter –
the host app needs **no** exception middleware of its own for this:

| Situation | Exception | Status |
|---|---|---|
| No published dialog for the key | `DialogNotFoundException` | `404 Not Found` |
| Unknown session id | `SessionNotFoundException` | `404 Not Found` |
| Answer violates the question's type/rules | `AnswerValidationException` | `400 Bad Request` (`ValidationProblem`) |
| Required field missing (`[Required]`) | `ValidationException` | `400 Bad Request` |
| Session not open / wrong question / misconfiguration | `InvalidOperationException` | `409 Conflict` |

## 4. Admin CRUD (optional)

Alongside the runtime endpoints, the package provides optional **admin CRUD endpoints** to maintain the
complete configuration graph (dialogs, questions, options, transitions, loop markers and triggers)
as well as the designer's canvas positions over HTTP – the same surface that the
[designer](./DESIGNER.md) drives too. They are registered via a
**dedicated, opt-in** extension method – so the admin surface can be secured selectively,
without restricting the public runtime endpoints:

```csharp
app.MapFlirtyEndpoints("/flirty");                       // runtime (sessions)
app.MapFlirtyAdminEndpoints("/flirty/admin")             // configuration (admin)
   .RequireAuthorization();                              // strongly recommended
```

All endpoints are – like the runtime side – a thin layer over Mediator commands (`ISender`)
and share the same error filter. Child resources are addressed hierarchically under the dialog
and are read via `GET {prefix}/dialogs/{id}` (the complete graph).

| Method & route | Purpose | Success |
|---|---|---|
| `POST /flirty/admin/dialogs` | Create a dialog (version 1, unpublished) | `201 Created` + `Location`, `DialogResponse` |
| `GET /flirty/admin/dialogs` | List dialogs (metadata) | `200 OK`, `DialogResponse[]` |
| `GET /flirty/admin/dialogs/{id}` | Read a dialog with its graph | `200 OK`, `DialogDetailResponse` |
| `PUT /flirty/admin/dialogs/{id}` | Change metadata/`StartQuestionId` | `200 OK`, `DialogResponse` |
| `DELETE /flirty/admin/dialogs/{id}` | Delete a dialog + graph (only without running sessions) | `204 No Content` |
| `POST /flirty/admin/dialogs/{id}/publish` \| `/unpublish` | Control publication | `200 OK`, `DialogResponse` |
| `POST /flirty/admin/dialogs/{id}/versions` | Derive a new version as a draft (clone) | `201 Created` + `Location`, `DialogDetailResponse` |
| `POST /flirty/admin/dialogs/{id}/abandon-sessions` | End the running sessions of this version | `200 OK`, `AbandonSessionsResponse` |
| `POST /flirty/admin/dialogs/{dialogId}/questions` | Create a question | `201 Created`, `QuestionResponse` |
| `PUT` \| `DELETE .../questions/{questionId}` | Change/delete a question | `200 OK` \| `204 No Content` |
| `POST .../questions/{questionId}/options` | Create an option | `201 Created`, `AnswerOptionResponse` |
| `PUT` \| `DELETE .../options/{optionId}` | Change/delete an option | `200 OK` \| `204 No Content` |
| `POST /flirty/admin/dialogs/{dialogId}/transitions` | Create a transition | `201 Created`, `TransitionResponse` |
| `PUT` \| `DELETE .../transitions/{transitionId}` | Change/delete a transition | `200 OK` \| `204 No Content` |
| `POST /flirty/admin/dialogs/{dialogId}/loops` | Create a loop marker | `201 Created`, `LoopResponse` |
| `PUT` \| `DELETE .../loops/{loopId}` | Change/delete a loop marker | `200 OK` \| `204 No Content` |
| `POST /flirty/admin/dialogs/{dialogId}/triggers` | Create a trigger | `201 Created`, `TriggerResponse` |
| `PUT` \| `DELETE .../triggers/{triggerId}` | Change/delete a trigger | `200 OK` \| `204 No Content` |
| `PUT /flirty/admin/dialogs/{dialogId}/layout` | Set canvas positions (**merge**) | `200 OK`, `DialogLayoutResponse[]` |
| `DELETE /flirty/admin/dialogs/{dialogId}/layout` | Discard all canvas positions | `204 No Content` |

### Set canvas positions

The positions (`DialogLayout`, since #102) are **display data of the designer** – the runtime never reads
them. They exist so that the arrangement on the graph canvas is the author's and not that of the
auto-layout; without a row, the designer arranges automatically.

```http
PUT /flirty/admin/dialogs/8f3e…/layout
Content-Type: application/json

{
  "entries": [
    { "elementKind": 0, "elementId": "3c1a…", "x": 520, "y": 240 }
  ]
}
```

`elementKind` is a numeric enum value (`0` = `Question`; further element kinds are foreseen but
not implemented). `x`/`y` are canvas pixels and must not be negative.

Two properties that deviate from the rest of the admin CRUD:

- **`PUT` is a merge, not a replacement.** Named elements are created or updated, **unnamed ones stay
  in place**. A drag gesture moves an element and should not have to send the whole layout along for
  that. Full discarding happens via `DELETE .../layout`. The response contains, in each case,
  the **complete** layout of the dialog, so that a caller can replace its state instead of
  merging it.
- **Both routes take effect even on a published version** and return no `409`. Where every
  graph change is locked, arranging stays allowed – coordinates do not belong to the graph
  ([ADR 0007](./adr/0007-layout-as-its-own-table.md)). `elementId` is, like the other question references,
  a raw reference without an existence check; deleting a question cleans up its position along with it, and
  deriving a version remaps the positions onto the new question ids.

### Create a trigger

A trigger (`TriggerDefinition`, since #42) connects a **point in time** in the dialog flow with a
**channel**. `config` is the channel-specific configuration as JSON following the schema
`Flirty.Domain.TriggerConfig`:

```http
POST /flirty/admin/dialogs/8f3e…/triggers
Content-Type: application/json

{
  "scope": 3,
  "questionId": null,
  "kind": 1,
  "config": "{\"url\":\"https://host.example/flirty/completed\",\"name\":\"order-created\"}",
  "expression": null
}
```

`scope` and `kind` are – like `type`/`status` above – **numeric** enum values:
`scope` `0` = `OnDialogStarted`, `1` = `AfterAnswer`, `2` = `AfterQuestion`, `3` = `OnDialogCompleted`;
`kind` `0` = `InProcess`, `1` = `Webhook`. `config` is a **JSON string** (the schema sits as text
inside it, not as an object) – just as it also lies in the column `TriggerDefinition.Config`.

The field semantics (scope mapping, `Config` schema, conditions, behavior of `Kind = InProcess`)
are in [TRIGGERS.md § Trigger definitions on the dialog](./TRIGGERS.md#trigger-definitions-on-the-dialog-42) –
deliberately not duplicated here.

### Flow: build and publish a dialog

The runtime starts only **published** dialogs. A dialog built via the API thus becomes startable
like this:

1. `POST /flirty/admin/dialogs` – create the dialog.
2. `POST .../questions` (+ `.../options` for choice types) – add questions/options.
3. `PUT /flirty/admin/dialogs/{id}` – set `startQuestionId` to the entry question.
4. `POST .../transitions` – add branches (optional if there is only a single, terminal question).
5. `POST .../loops` – mark cycles as a loop so that their answers are collected per iteration instead of
   overwritten (only needed if a transition points back to an earlier question).
6. `POST .../triggers` – add optional back channels (webhook targets or documented
   in-process intents).
7. `POST /flirty/admin/dialogs/{id}/publish` – publish; afterwards startable via `POST /flirty/sessions`.

### Error mapping (admin)

In addition to the mapping above, the following apply to the admin CRUD:

| Situation | Exception | Status |
|---|---|---|
| Unknown dialog/question/option/transition/loop/trigger id (or a child foreign to the parent) | `ConfigurationNotFoundException` | `404 Not Found` |
| Duplicate key (`Key` per dialog / `(DialogId,Key)` / `(QuestionId,Key)` / `CollectionKey` per dialog) | `InvalidOperationException` | `409 Conflict` |
| Publishing without a set entry question | `InvalidOperationException` | `409 Conflict` |
| Graph change on a **published** version (question/option/transition/loop/trigger/entry question) | `DialogPublishedException` | `409 Conflict` |
| Deleting a version with running sessions | `InvalidOperationException` | `409 Conflict` |
| Renaming a key while multiple versions exist | `InvalidOperationException` | `409 Conflict` |
| Missing required field (`[Required]`) | `ValidationException` | `400 Bad Request` |
| Trigger: `AfterQuestion` without `questionId` – or `questionId` at another point in time | `ValidationException` | `400 Bad Request` |
| Trigger: broken `config` JSON or `Kind = Webhook` without an absolute `http`/`https` URL | `ValidationException` | `400 Bad Request` |
| Layout: empty batch, the same element multiple times in the batch, or a negative coordinate | `ValidationException` | `400 Bad Request` |

The three trigger and layout rows are **cross-field rules** (`Runtime/Admin/TriggerValidation.cs` and
`SetDialogLayoutCommand.Validate` respectively, invoked via `IValidatableObject`): they check the **request**,
not the database state, and therefore run in the validation behavior already before the handler.

The layout routes deliberately do **not** appear in the `DialogPublishedException` row – they are the
one exception to the publish lock.

> **Notes / deliberate limits:** `POST /dialogs` always creates `Version = 1`; follow-up versions arise
> exclusively via `POST /dialogs/{id}/versions`. The **draft** is edited in place, a
> **published** version is locked (see the error mapping above and
> [ADR 0005](./adr/0005-immutable-published-dialog-version.md)) – name and description
> are exempt from this. The question references of `Transition`
> (`FromQuestionId`/`TargetQuestionId`), `LoopDefinition` (`EntryQuestionId`/`BreakingQuestionId`) and
> `TriggerDefinition` (`QuestionId`) as well as `DialogLayout` (`ElementId`) are – in line with the FK-free
> domain model – raw references without an existence check; deleting a question, however, cleans up
> referencing transitions, loop markers, triggers **and** layout rows and resets a `StartQuestionId`
> pointing at it.

### Evolve a productive version

```http
POST /flirty/admin/dialogs/{id}/versions      -> 201, copy as a draft (Version n+1)
… changes to the draft via the usual CRUD routes …
POST /flirty/admin/dialogs/{copyId}/publish   -> 200, retires version n automatically
```

Running sessions stay on their version and run to completion there; new sessions start on the
newly published one. If a version is to be **deleted**, its sessions must be ended –
`POST /dialogs/{id}/abandon-sessions` sets them to `Abandoned` (the answers are preserved).

## Verification

The endpoints are secured end-to-end via an in-process `TestServer` (real HTTP calls, SQLite in-memory,
Docker-free) – the runtime endpoints in
`tests/Flirty.Tests/AspNetCore/MapFlirtyEndpointsTests.cs`, the admin CRUD in
`tests/Flirty.Tests/AspNetCore/MapFlirtyAdminEndpointsTests.cs` (including an end-to-end test that
subsequently starts a published dialog, built purely via the API, over `POST /flirty/sessions`).
Both share the test host `tests/Flirty.Tests/AspNetCore/FlirtyTestHost.cs`.

```pwsh
dotnet test Flirty.sln -c Release
```
