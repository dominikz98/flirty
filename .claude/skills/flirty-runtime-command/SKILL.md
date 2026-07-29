---
name: flirty-runtime-command
description: Add a new engine operation (Mediator command/query) to the Flirty runtime – incl. handler, DI registration, test and optional ASP.NET endpoint. Use for "new command", "new query", "new runtime operation", "extend IFlirtyEngine", "new endpoint for an engine action".
---

# Add a new runtime command/query

Canonical extension path of the engine. All engine operations are **Mediator commands/queries** and
live in the **core** (`src/Flirty`), because the Mediator source generator (martinothamar) only
discovers handlers in the same compilation. Reference: `docs/RUNTIME.md`, `docs/MEDIATOR.md`.

## Prior art (read before writing)

- `src/Flirty/Runtime/SubmitAnswerCommand.cs` – command with a result record, persistence + branching.
- `src/Flirty/Runtime/ResumeDialogQuery.cs` – purely reading query (no `SaveChangesAsync`).
- `src/Flirty/Runtime/TransitionResolver.cs` – shared branching logic (do not duplicate).
- `src/Flirty/Runtime/IFlirtyEngine.cs` + `FlirtyEngine.cs` – facade over `ISender`.
- `src/Flirty/DependencyInjection/FlirtyServiceCollectionExtensions.cs` – DI wiring.

## Steps

1. **Command/query + result** in `src/Flirty/Runtime/`:
   ```csharp
   public sealed record DoThingCommand(
       [property: Required] Guid SessionId,
       [property: Required] string Value) : ICommand<DoThingResult>;   // Query: : IQuery<TResult>

   public sealed record DoThingResult(Guid SessionId, bool IsCompleted);
   ```
   - `[Required]` takes effect via the `ValidationPipelineBehavior` against `null`/empty strings, **not**
     against `Guid.Empty` (value type) – handle empty ids in the handler instead.
   - Keep return and question views lean (see `QuestionView`/`SessionAnswerView` in `RUNTIME.md`).

2. **Handler** (`internal sealed`, in the same folder):
   ```csharp
   internal sealed class DoThingCommandHandler(IDialogStore store, IExpressionEvaluator evaluator)
       : ICommandHandler<DoThingCommand, DoThingResult>
   {
       public async ValueTask<DoThingResult> Handle(DoThingCommand cmd, CancellationToken ct) { … }
   }
   ```
   - Persistence **exclusively** via `IDialogStore` (never `FlirtyDbContext` directly in the handler).
   - Evaluate branching via the shared `TransitionResolver`, do not reimplement it.
   - Reuse known error types: `SessionNotFoundException`, `DialogNotFoundException`,
     `ConfigurationNotFoundException`, otherwise `InvalidOperationException` on misconfiguration.
   - Writing handlers: `SaveChangesAsync()` at the end; publish notifications **after** saving
     (see skill `flirty-trigger-notification`).

3. **Facade** (optional, if the operation should be reachable in a typed way): add a method in
   `IFlirtyEngine.cs` and implement it in `FlirtyEngine.cs` as a thin `ISender.Send(...)` call.

4. **DI:** usually **nothing** to do – command/query handlers are registered automatically by the
   source generator. Only a new **pipeline behavior** or a **closed** behavior registration (like
   `AnswerValidationPipelineBehavior` for submit/edit) must be added manually in
   `FlirtyServiceCollectionExtensions.cs`.

5. **Test** in `tests/Flirty.Tests/Runtime/`: against SQLite in-memory through the full pipeline via
   `IFlirtyEngine`. English, snake_case-ish test names. Cover success **and** failure cases.

6. **Endpoint** (optional, only if HTTP is needed): see the section below.

> **Does the command change the configuration graph of a dialog** (questions, answer options,
> transitions, loop markers, triggers, entry question)? Then it belongs as the **first** precondition in
> the handler:
> ```csharp
> await DialogEditGuard.EnsureEditableAsync(_store, command.DialogId, cancellationToken);
> ```
> – or `DialogEditGuard.EnsureEditable(dialog)` if the dialog is already loaded. A published version is
> immutable, because running sessions load their graph from the same row (ADR
> `docs/adr/0005-immutable-published-dialog-version.md`). The guard sits **before**
> resolving the child elements, so the understandable conflict message wins and not a not-found from a
> follow-up check. In the test: the case "published → `DialogPublishedException`" belongs there (pattern:
> `tests/Flirty.Tests/Runtime/DialogVersioningTests.cs`).

## Optional ASP.NET endpoint (`Flirty.AspNetCore`)

- Request/response **DTO** in `src/Flirty.AspNetCore/Dtos/` (admin: `Dtos/Admin/`).
- Add the route in `FlirtyEndpointRouteBuilderExtensions.cs` (admin:
  `FlirtyAdminEndpointRouteBuilderExtensions.cs`): map DTO → command, `ISender.Send(...)`, map result →
  response.
- Mapping in `src/Flirty.AspNetCore/Mapping/` (no auto-mapper – hand mapping as in the existing code).
- Engine exceptions are mapped by the `FlirtyExceptionEndpointFilter` to `ProblemDetails` (404/400/409) –
  add new exception types there if a different status code is wanted.
- Endpoint test in `tests/Flirty.Tests/AspNetCore/` via `FlirtyTestHost`.

## Definition of Done

English XML docs on all new public API (CS1591 is an error in the packable projects) · tests green
· `docs/RUNTIME.md` (and possibly `docs/GETTING-STARTED-WebApi.md`) updated.

## Verification

```pwsh
dotnet build Flirty.sln
dotnet test tests/Flirty.Tests
```
