# Getting Started – Web Sample (Minimal API + Chat UI)

> Status: Issue #45. This guide shows the runnable **web sample** under
> [`src/Flirty.Samples.Web`](../src/Flirty.Samples.Web): a minimal-API host that hosts the Flirty endpoints
> **and** **consumes** them through a static chat UI (HTML + vanilla JS). Demonstrated are
> **resume**, **edit**, **branching**, a **loop over a list** and **triggers** – with a custom
> in-process **handler** and an inbound **webhook receiver**. Foundations: the endpoints in
> [GETTING-STARTED-WebApi.md](./GETTING-STARTED-WebApi.md), the triggers in [TRIGGERS.md](./TRIGGERS.md),
> the loops in [LOOPS.md](./LOOPS.md).

## Running

```pwsh
dotnet run --project src/Flirty.Samples.Web
```

Then open [`http://localhost:5080`](http://localhost:5080). On start the app provisions a demo dialog
(see below), and the chat UI automatically starts a session. Play the dialog through (role selection →
detail question → several skills via the loop → completion); on the right, panels show the collected
skills, the fired in-process triggers and the received webhooks. **Reload** restores the session
(resume), the **✏️** on an answer edits it (edit).

## Project setup

The host is a `Microsoft.NET.Sdk.Web` project and references only the core, the endpoint package and –
for `o.ApplyMigrations()` with SQLite – the SQLite migrations assembly. ASP.NET Core comes through the SDK,
**no** additional NuGet packages:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <ItemGroup>
    <ProjectReference Include="..\Flirty\Flirty.csproj" />
    <ProjectReference Include="..\Flirty.AspNetCore\Flirty.AspNetCore.csproj" />
    <ProjectReference Include="..\Flirty.Migrations.Sqlite\Flirty.Migrations.Sqlite.csproj" />
  </ItemGroup>
</Project>
```

## 1. Registration & endpoints

The composition lives in [`WebSampleApp`](../src/Flirty.Samples.Web/WebSampleApp.cs) (shared by
`Program.cs` and the integration tests). `AddFlirty(…)` wires up the stack; the custom in-process handler
is registered via `AddFlirtyHandler<…>()`, the loopback target of the outbound webhook via `o.AddWebhook(…)`:

```csharp
builder.Services.AddFlirty(o =>
{
    o.UseSqlite(connectionString);
    o.ApplyMigrations();
    o.AddWebhook(TriggerScope.OnDialogCompleted, baseUrl + "/demo/webhooks/flirty"); // loopback demo
});
builder.Services.AddFlirtyHandler<DialogCompletedNotification, DemoDialogCompletedHandler>();
```

```csharp
app.UseDefaultFiles();
app.UseStaticFiles();                         // static chat UI from wwwroot
app.MapFlirtyEndpoints("/flirty");            // runtime – consumed by the chat UI
app.MapFlirtyAdminEndpoints("/flirty/admin"); // configuration – builds the demo dialog
```

> **Security:** In the sample the admin endpoints are deliberately mapped **without** `RequireAuthorization()`,
> so that provisioning and the UI run without an auth setup. In production, the admin surface must be secured
> (see [GETTING-STARTED-WebApi.md](./GETTING-STARTED-WebApi.md) §4).

Configurable settings (defaults in `appsettings.json`): `ConnectionStrings:Flirty`, `Flirty:BaseUrl`,
`Flirty:ApplyMigrations`, `Flirty:EnableOutboundWebhook`, `Flirty:AutoProvision`.

## 2. Demo dialog: built via the admin CRUD API

The demo dialog `web-onboarding` is built idempotently on start via the **admin CRUD API**
([`DemoDialogProvisioner`](../src/Flirty.Samples.Web/DemoDialogProvisioner.cs), driven by the
[`DemoProvisioningHostedService`](../src/Flirty.Samples.Web/DemoProvisioningHostedService.cs)). Flow:
`POST /dialogs` → `POST …/questions` (+ `…/options`) → `PUT /dialogs/{id}` (StartQuestionId) →
`POST …/transitions` → `POST …/dialogs/{id}/publish`.

Dialog flow (branching **and** a loop over a list):

```text
role (SingleChoice: dev|pm)                     ← branching (entry question)
   ├─ role=="dev"  → language (FreeText)
   └─ default      → product  (FreeText)
language|product → skill (FreeText)             ← loop entry (CollectionKey "skills")
skill            → more  (SingleChoice: yes|no) ← breaking question
   ├─ more=="yes" → skill  (loop-back, Priority 0)
   └─ default     → summary (exit, Priority 1)
summary (Boolean, terminal)                     → completion → trigger
```

> **Deliberate exception (loop marker):** The admin CRUD API covers **no** loop CRUD (only dialog/question/
> option/transition, see [GETTING-STARTED-WebApi.md](./GETTING-STARTED-WebApi.md) §4). The cycle arises
> from the loop-back `Transition` (`more == "yes"` → `skill`); the actual
> [`LoopDefinition`](../src/Flirty/Domain/LoopDefinition.cs) (`CollectionKey="skills"`, entry `skill`,
> breaking `more`) is attached by the provisioner **once directly via the `FlirtyDbContext`** – only then
> does the runtime collect the `skill` answers per iteration instead of overwriting them (see
> [LOOPS.md](./LOOPS.md)).

## 3. Chat UI (`wwwroot`)

The UI ([`wwwroot/app.js`](../src/Flirty.Samples.Web/wwwroot/app.js)) talks exclusively to the
HTTP endpoints and holds no server state:

- **Start/Resume:** `externalUserKey` and `sessionId` live in `localStorage`. On load the history is
  reconstructed via `GET /flirty/sessions/{id}` (resume after reload); without a stored session a new one is
  started via `POST /flirty/sessions`.
- **Answers:** `POST /flirty/sessions/{id}/answers` with the `value` as **raw JSON text** per question type
  (SingleChoice/FreeText → JSON string, Boolean → `true`/`false`).
- **Edit:** `PUT /flirty/sessions/{id}/answers/{questionId}` (for loop answers with `iterationIndex`);
  the number of discarded downstream answers is shown.
- **Loop/Branching:** arise from the rendered `currentQuestion` flow; the collected `skills` are shown
  by a side panel.

> **Input control: one place for answer and edit.** `renderAnswerControls` builds the control
> **type-dependently** – option buttons for `SingleChoice`, Yes/No for `Boolean`, otherwise a field with a
> matching `input.type` – and is used by the open question **and** the edit form. This is not an end in
> itself: the UI shows answers in their *display form* (the option **label**, "Yes"/"No"), but what is
> stored is the **value** (`option.value`, `true`/`false`). A separate edit form with a generic text field
> mixed exactly these two levels and wrote the label back – for a choice the
> [`AnswerValidator`](../src/Flirty/Validation/AnswerValidator.cs) rejected it as an invalid option with `400`,
> for `Boolean` the answer silently flipped to "No". That is why the edit path gets the same controls and
> the field is pre-filled with the **raw** value (`decodeRaw`, not `decodeForDisplay`).

> **One request at a time.** For the duration of a submit or edit, `setBusy(true)` sets the
> **✏️** buttons of all answer bubbles to `disabled` – the input line is cleared on send anyway.
> Without this lock, a quickly clicked edit could overtake the still-flying answer: the
> server did not yet know the last answer, discarded one answer too few on the edit and rejected the
> trailing submit with `409` ("is not the currently open question"). The data stayed
> consistent – but the display was not plausible. This was noticed via an E2E test reacting to it (#97).

## 4. Triggers: handler + webhook receiver

- **In-process handler:** [`DemoDialogCompletedHandler`](../src/Flirty.Samples.Web/DemoDialogCompletedHandler.cs)
  (`INotificationHandler<DialogCompletedNotification>`) logs every completion into a sink that
  `GET /demo/triggers` shows.
- **Inbound webhook receiver:** `POST /demo/webhooks/flirty` receives the engine's outbound HTTP `POST`,
  reads the header `X-Flirty-Event` and the JSON body and stores both for `GET /demo/webhooks`.
  Because the sample delivers to itself via `o.AddWebhook(OnDialogCompleted, …/demo/webhooks/flirty)`,
  the complete **outbound→inbound round-trip** is visible live in the trigger panel.

## Verification

```pwsh
dotnet test tests/Flirty.Tests -c Release   # in-process TestServer: branching/loop/resume/edit/handler/inbound
```

The integration test [`WebSampleTests`](../tests/Flirty.Tests/Samples/WebSampleTests.cs) hosts the real
sample composition over an in-process `TestServer` (SQLite in-memory) and plays it through end-to-end.
The full outbound→inbound webhook round-trip needs real Kestrel and is secured in the browser:

```pwsh
pwsh tests/Flirty.E2E/bin/Release/net10.0/playwright.ps1 install chromium  # once
dotnet test tests/Flirty.E2E -c Release
```

[`WebSampleE2ETests`](../tests/Flirty.E2E/WebSampleE2ETests.cs) starts the app on real Kestrel and drives
the chat UI in the browser. Seven tests cover the acceptance criterion from **#47**:

| Test | Covers |
|---|---|
| `Run_through_branching_loop_and_the_trigger_round_trip` | dev branch, two loop iterations, completion, in-process handler and outbound→inbound webhook |
| `Branching_default_branch_leads_over_product_into_the_loop` | the `IsDefault` transition (`pm` → `product`) as a counter-check to the dev branch |
| `Reload_restores_the_session_in_the_middle_of_the_loop` | reload **inside** the loop → iteration state and open question come from the server |
| `Editing_an_answer_discards_the_downstream_answers` | free-text edit incl. number of discarded downstream answers and recomputation of the path |
| `Editing_the_branching_question_switches_the_branch` | edit of a choice → branch switch; at the same time a regression test for the type-dependent input control (see §3) |
| `Editing_a_loop_iteration_hits_exactly_that_iteration` | `iterationIndex` path and re-opening an already-completed session |
| `Editing_a_yes_no_answer_keeps_the_chosen_value` | the second half of the same regression test: a `Boolean` answer must not silently flip on edit |

All tests share the app including the database, but each gets a fresh browser context (empty
`localStorage` → its own `externalUserKey` → its own session). If the Playwright browsers are missing, the
E2E tests skip instead of failing.
