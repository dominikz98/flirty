# ADR 0002 – Mediator (martinothamar) as the in-process bus

- **Status:** Accepted
- **Context issue:** #14 – Mediator setup in the core
- **Affected:** `src/Flirty` (`DependencyInjection/`, `Runtime/`, `Pipeline/`), `src/Flirty.AspNetCore`

## Context

Flirty has to map three things through **one** mechanism:

1. **Engine operations** (start, submit, edit, resume, admin CRUD) need a uniform
   in-process entry point – both for host apps and for the thin HTTP layer.
2. **Triggers** need a back channel into the host app: "after this answer, X happens on my side".
3. **Cross-cutting** (logging, declarative message validation, domain answer validation)
   must be pluggable **before** every handler, without touching every handler.

The whole thing runs in a library that is passed on as a NuGet package: startup time, trimming
and the license of the chosen building block therefore fall on the consumer, not on us.

## Decision

**[Mediator (martinothamar)](https://github.com/martinothamar/Mediator)** as the in-process bus – a
**source-generator**-based implementation of the mediator pattern (MIT, pinned centrally in
`Directory.Packages.props`). The mapping is fixed:

| Concern | Mediator building block |
|---|---|
| Engine operation | `ICommand<T>` / `IQuery<T>` + handler |
| Trigger (back channel) | `INotification` + the host app's `INotificationHandler<T>` |
| Cross-cutting | `IPipelineBehavior<TMessage, TResponse>` |

It is wired up in `AddFlirty()` (`src/Flirty/DependencyInjection/FlirtyServiceCollectionExtensions.cs`):
`AddMediator(o => o.ServiceLifetime = ServiceLifetime.Scoped)`, followed by the open-generic
base behaviors `LoggingPipelineBehavior<,>` and `ValidationPipelineBehavior<,>`.

## Discarded alternatives

- **MediatR.** Established, but **reflection-based** (assembly scan at startup, handler resolution
  at runtime) – startup cost and AOT/trimming friction that a library passes on to every consumer.
  On top of that, its licensing has not been consistently free since 2024; for a package that is
  itself published under MIT, a consistently free dependency is the lower-risk choice.
- **Custom service interfaces without a bus** (`IDialogService.SubmitAsync(…)`). No natural
  plug-in point for cross-cutting – logging and validation end up either in every handler or in
  hand-written decorators per interface. The trigger back channel would additionally have to be
  built by hand (events, custom handler registration, custom publish semantics).
- **A bus for triggers only, services for the operations.** Two mechanisms with two registration
  and lifetime models for the same engine – more concepts for the consumer, with no gain.

## Consequences

**Positive**

- No reflection overhead at startup; the wiring is visible at compile time.
- Cross-cutting is a one-liner in `AddFlirty()`: the answer validation (#30) hangs as a behavior
  **before** submit/edit, instead of being duplicated in both handlers.
- The trigger back channel is the same mechanism as the engine itself: the host app registers
  `INotificationHandler<T>` – identical in a console app and a web app.

**Negative / binding** – the generator's two hard rules become an **architecture invariant**:

1. **Handlers are discovered only within the same compilation**, and the `AddMediator` call must
   live in the project that references the generator. That is why **all** commands/queries/handlers
   **and** the notification contracts live in the `Flirty` core; `Flirty.AspNetCore` remains a pure
   mapping layer over `ISender` and **cannot** contribute any handlers at all (backs
   [ADR 0003](./0003-aspnet-free-core.md)). A notification type defined in the sample reaches no
   handler via `IPublisher`.
2. **Open-generic behaviors are not registered automatically**, but manually via
   `AddSingleton(typeof(IPipelineBehavior<,>), typeof(MyBehavior<,>))`; the **order of
   registration** determines the nesting of the pipeline.

In addition, the generator requires a handler per message in the core compilation (`MSG0005`).
Trigger notifications, however, are deliberately handled only by host apps – the warning is therefore
suppressed **per notification type** via `#pragma` (`src/Flirty/Runtime/Notifications/*.cs`) instead of
project-wide, so that a genuinely missing command/query handler still breaks the build
(`TreatWarningsAsErrors`).

Details: [MEDIATOR.md](../MEDIATOR.md), the trigger side in [TRIGGERS.md](../TRIGGERS.md).
