# Mediator setup (core)

> Status: Issue #14. This guide describes how the mediator (martinothamar) is wired up in the core
> project `Flirty` and how to add commands/queries, handlers and pipeline behaviors.
> **Why** a mediator at all – and what stood as the alternatives (MediatR, custom
> service interfaces) – is in [ADR 0002](./adr/0002-mediator-as-in-process-bus.md).

## Overview

Flirty uses **[Mediator (martinothamar)](https://github.com/martinothamar/Mediator)** – a
source-generator-based implementation of the mediator pattern (no reflection overhead, MIT).

- **`Mediator.Abstractions`** provides the contract types (`ICommand<TResponse>`, `IQuery<TResponse>`,
  `INotification`, `ICommandHandler<,>`, `INotificationHandler<>`, `IPipelineBehavior<,>`, `ISender`,
  `IMediator`, `IPublisher`, `Unit`).
- **`Mediator.SourceGenerator`** generates the `IMediator` implementation at compile time and the
  DI registration `AddMediator(...)` (namespace `Microsoft.Extensions.DependencyInjection`).

Both packages are pinned centrally in `Directory.Packages.props` (v3.0.2) and referenced in the core;
the source generator is included as an analyzer (`PrivateAssets=all`) and is **not** published as a
package dependency.

## Registration

The public extension `AddFlirty()` (namespace `Microsoft.Extensions.DependencyInjection`,
`FlirtyServiceCollectionExtensions`) wires everything up:

```csharp
public static IServiceCollection AddFlirty(this IServiceCollection services)
{
    services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);
    services.AddSingleton(typeof(IPipelineBehavior<,>), typeof(LoggingPipelineBehavior<,>));
    services.AddSingleton(typeof(IPipelineBehavior<,>), typeof(ValidationPipelineBehavior<,>));
    return services;
}
```

> **Note:** `AddFlirty()` is currently a **#14 stub**. Issue #34 extends the method into the
> full `AddFlirty(o => …)` registration (DB provider, auto-migration, webhooks,
> a swappable expression evaluator).

Usage (e.g. in a console app – the core is ASP.NET-free):

```csharp
var services = new ServiceCollection();
services.AddLogging();     // the base behaviors use ILogger<>
services.AddFlirty();
var provider = services.BuildServiceProvider();

using var scope = provider.CreateScope();  // handlers/mediator are Scoped
var sender = scope.ServiceProvider.GetRequiredService<ISender>();
```

## Two central rules of martinothamar/Mediator

1. **The source generator only discovers handlers within the same compilation** and the
   `AddMediator` call **must** live in the same project that references the generator.
   → That is why the generator, the `AddMediator` call **and** all real commands/queries/handlers
   live in the **core** (`Flirty`). The core is the published engine; consumers only call
   `AddFlirty()` and do not need the source generator themselves.
2. **Pipeline behaviors are not registered automatically.** Open-generic behaviors
   must be registered manually via `AddSingleton(typeof(IPipelineBehavior<,>), typeof(MyBehavior<,>))`.
   The order of registration determines the nesting of the pipeline.

Both rules are not a footnote but an **architecture invariant** – rule 1 is the reason why
`Flirty.AspNetCore` *must* remain a pure mapping layer over `ISender`
([ADR 0002](./adr/0002-mediator-as-in-process-bus.md), [ADR 0003](./adr/0003-aspnet-free-core.md)).

## Base pipeline behaviors

Both live in the namespace `Flirty.Pipeline` and are registered by `AddFlirty()`:

| Behavior | Purpose |
|---|---|
| `LoggingPipelineBehavior<TMessage,TResponse>` | Logs the start, completion (incl. duration) and errors of each message via `ILogger<>`. |
| `ValidationPipelineBehavior<TMessage,TResponse>` | Validates the message **declaratively** via `System.ComponentModel.DataAnnotations` (`[Required]`, …) and throws a `ValidationException` on violations. |

In addition, `AddFlirty()` has since #30 registered a **domain-level** answer-validation behavior –
deliberately **closed per answer-submitting command** (not open-generic) and **internal**, because it
needs the scoped `IDialogStore`:

| Behavior | Purpose |
|---|---|
| `AnswerValidationPipelineBehavior<TMessage,TResponse>` (internal, closed for `SubmitAnswerCommand`/`EditAnswerCommand`) | Resolves the question of the pinned dialog version and validates the answer value (type + `ValidationRules`) via `IAnswerValidator` **before** the handler; throws `AnswerValidationException` on violation. Details in [VALIDATION.md](./VALIDATION.md). |

## Adding a command/handler

Command and handler belong in the **core** (so that the generator sees them):

```csharp
public sealed record CreateFooCommand(string Name) : ICommand<FooResult>;

internal sealed class CreateFooCommandHandler : ICommandHandler<CreateFooCommand, FooResult>
{
    public ValueTask<FooResult> Handle(CreateFooCommand command, CancellationToken cancellationToken)
        => ValueTask.FromResult(new FooResult(/* … */));
}
```

Sending: `await sender.Send(new CreateFooCommand("bar"));`

## Notifications (in-process triggers)

Notifications are the back channel into the host app: the engine publishes `INotification` contracts,
which host apps handle via their own `INotificationHandler<T>`.

```csharp
public sealed record FooHappenedNotification(Guid Id) : INotification;   // the contract belongs in the core

// Publishing (in a command handler):
await _publisher.Publish(new FooHappenedNotification(id), cancellationToken);

// Handling (in the host app, registered via DI):
services.AddScoped<INotificationHandler<FooHappenedNotification>, MyHandler>();
```

For the engine triggers there is the convenience helper
`services.AddFlirtyHandler<DialogCompletedNotification, MyHandler>()` (since #32, default `Scoped`) – see
[TRIGGERS.md](./TRIGGERS.md).

Two peculiarities of martinothamar/Mediator that follow from rule 1:

- **Notification contracts must live in the core.** Only then does the source generator know the type and
  does `IPublisher.Publish` deliver it to registered handlers (including those from host assemblies). A
  notification type defined in the sample reaches no handler via `IPublisher`.
- **MSG0005 (message without a handler).** The generator requires a handler per message in the
  core compilation. Trigger notifications, however, are deliberately handled only by host apps; that is why
  MSG0005 is suppressed per notification type on purpose (`#pragma warning disable MSG0005`) instead of
  project-wide, so that a genuinely missing command/query handler still stands out.

The concrete engine triggers (`DialogStarted`/`AnswerSubmitted`/`QuestionAnswered`/`DialogCompleted`) and
when they are published are described in [TRIGGERS.md](./TRIGGERS.md).

## Adding a pipeline behavior

1. Implement `IPipelineBehavior<TMessage, TResponse>` (constraint `where TMessage : notnull, IMessage`),
   call `next(message, cancellationToken)` in `Handle(...)` (or deliberately abort/throw).
2. Register it open-generic in `AddFlirty()`:
   `services.AddSingleton(typeof(IPipelineBehavior<,>), typeof(MyBehavior<,>));`

## Verification / smoke test

The core contains an **internal** smoke seam (`Flirty.Diagnostics.PingCommand`/`Pong`/
`PingCommandHandler`) that is visible to tests exclusively via `[assembly: InternalsVisibleTo("Flirty.Tests")]`
(no part of the public API). It has **deliberately stayed** even though there are real commands
by now: it exercises the pipeline in isolation – without persistence, dialog graph or
expression engine – so that a wiring error stands out here and not first as a seeming
runtime bug in a dialog test. The tests
in `tests/Flirty.Tests/MediatorPipelineBehaviorTests.cs` demonstrate the acceptance criterion of #14:

- a dummy command runs through the `LoggingPipelineBehavior` (log entries are captured),
- an invalid command is rejected by the `ValidationPipelineBehavior` with a `ValidationException`.

```pwsh
dotnet test tests/Flirty.Tests
```
