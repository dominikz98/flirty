# Mediator-Setup (Core)

> Stand: Issue #14. Dieser Guide beschreibt, wie der Mediator (martinothamar) im Core-Projekt
> `Flirty` verdrahtet ist und wie man Commands/Queries, Handler und Pipeline-Behaviors ergänzt.

## Überblick

Flirty nutzt **[Mediator (martinothamar)](https://github.com/martinothamar/Mediator)** – eine
Source-Generator-basierte Implementierung des Mediator-Patterns (kein Reflection-Overhead, MIT).

- **`Mediator.Abstractions`** liefert die Vertragstypen (`ICommand<TResponse>`, `IQuery<TResponse>`,
  `INotification`, `ICommandHandler<,>`, `INotificationHandler<>`, `IPipelineBehavior<,>`, `ISender`,
  `IMediator`, `IPublisher`, `Unit`).
- **`Mediator.SourceGenerator`** generiert zur Compile-Zeit die `IMediator`-Implementierung und die
  DI-Registrierung `AddMediator(...)` (Namespace `Microsoft.Extensions.DependencyInjection`).

Beide Pakete sind zentral in `Directory.Packages.props` gepinnt (v3.0.2) und im Core referenziert;
der Source-Generator ist als Analyzer (`PrivateAssets=all`) eingebunden und wird **nicht** als
Paket-Abhängigkeit veröffentlicht.

## Registrierung

Die öffentliche Extension `AddFlirty()` (Namespace `Microsoft.Extensions.DependencyInjection`,
`FlirtyServiceCollectionExtensions`) verdrahtet alles:

```csharp
public static IServiceCollection AddFlirty(this IServiceCollection services)
{
    services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);
    services.AddSingleton(typeof(IPipelineBehavior<,>), typeof(LoggingPipelineBehavior<,>));
    services.AddSingleton(typeof(IPipelineBehavior<,>), typeof(ValidationPipelineBehavior<,>));
    return services;
}
```

> **Hinweis:** `AddFlirty()` ist aktuell ein **#14-Stub**. Issue #34 erweitert die Methode zur
> vollständigen `AddFlirty(o => …)`-Registrierung (DB-Provider, Auto-Migration, Webhooks,
> austauschbarer Expression-Evaluator).

Nutzung (z. B. in einer Console-App – der Core ist ASP.NET-frei):

```csharp
var services = new ServiceCollection();
services.AddLogging();     // die Basis-Behaviors nutzen ILogger<>
services.AddFlirty();
var provider = services.BuildServiceProvider();

using var scope = provider.CreateScope();  // Handler/Mediator sind Scoped
var sender = scope.ServiceProvider.GetRequiredService<ISender>();
```

## Zwei zentrale Regeln von martinothamar/Mediator

1. **Der Source-Generator entdeckt Handler nur innerhalb derselben Compilation** und der
   `AddMediator`-Aufruf **muss** im selben Projekt stehen, das den Generator referenziert.
   → Deshalb liegen Generator, `AddMediator`-Aufruf **und** alle echten Commands/Queries/Handler
   im **Core** (`Flirty`). Der Core ist die veröffentlichte Engine; Konsumenten rufen nur
   `AddFlirty()` auf und brauchen den Source-Generator nicht selbst.
2. **Pipeline-Behaviors werden nicht automatisch registriert.** Offen-generische Behaviors
   müssen manuell via `AddSingleton(typeof(IPipelineBehavior<,>), typeof(MyBehavior<,>))`
   registriert werden. Die Reihenfolge der Registrierung bestimmt die Verschachtelung der Pipeline.

## Basis-Pipeline-Behaviors

Beide liegen im Namespace `Flirty.Pipeline` und werden von `AddFlirty()` registriert:

| Behavior | Zweck |
|---|---|
| `LoggingPipelineBehavior<TMessage,TResponse>` | Protokolliert Beginn, Abschluss (inkl. Dauer) und Fehler jeder Nachricht via `ILogger<>`. |
| `ValidationPipelineBehavior<TMessage,TResponse>` | Validiert die Nachricht **deklarativ** per `System.ComponentModel.DataAnnotations` (`[Required]`, …) und wirft bei Verstößen eine `ValidationException`. |

Zusätzlich registriert `AddFlirty()` seit #30 ein **fachliches** Antwort-Validierungs-Behavior –
bewusst **geschlossen je antworteinreichendem Command** (nicht offen-generisch) und **intern**, weil es
den scoped `IDialogStore` benötigt:

| Behavior | Zweck |
|---|---|
| `AnswerValidationPipelineBehavior<TMessage,TResponse>` (intern, geschlossen für `SubmitAnswerCommand`/`EditAnswerCommand`) | Löst die Frage der gepinnten Dialogversion auf und validiert den Antwortwert (Typ + `ValidationRules`) per `IAnswerValidator` **vor** dem Handler; wirft bei Verstoß `AnswerValidationException`. Details in [VALIDATION.md](./VALIDATION.md). |

## Ein Command/Handler hinzufügen

Command und Handler gehören in den **Core** (damit der Generator sie sieht):

```csharp
public sealed record CreateFooCommand(string Name) : ICommand<FooResult>;

internal sealed class CreateFooCommandHandler : ICommandHandler<CreateFooCommand, FooResult>
{
    public ValueTask<FooResult> Handle(CreateFooCommand command, CancellationToken cancellationToken)
        => ValueTask.FromResult(new FooResult(/* … */));
}
```

Senden: `await sender.Send(new CreateFooCommand("bar"));`

## Notifications (In-Process-Trigger)

Notifications sind der Rückkanal in die Host-App: Die Engine publiziert `INotification`-Contracts, die
Host-Apps über eigene `INotificationHandler<T>` behandeln.

```csharp
public sealed record FooHappenedNotification(Guid Id) : INotification;   // Contract gehört in den Core

// Publizieren (in einem Command-Handler):
await _publisher.Publish(new FooHappenedNotification(id), cancellationToken);

// Behandeln (in der Host-App, per DI registriert):
services.AddScoped<INotificationHandler<FooHappenedNotification>, MyHandler>();
```

Zwei Besonderheiten von martinothamar/Mediator, die aus Regel 1 folgen:

- **Notification-Contracts müssen im Core liegen.** Nur so kennt der Source-Generator den Typ und
  liefert `IPublisher.Publish` ihn an registrierte Handler (auch aus Host-Assemblies) aus. Ein im Sample
  definierter Notification-Typ erreicht über `IPublisher` keinen Handler.
- **MSG0005 (Nachricht ohne Handler).** Der Generator verlangt je Nachricht einen Handler in der
  Core-Compilation. Trigger-Notifications werden aber bewusst erst von Host-Apps behandelt; daher ist
  MSG0005 je Notification-Typ gezielt unterdrückt (`#pragma warning disable MSG0005`) statt projektweit,
  damit ein echt fehlender Command-/Query-Handler weiterhin auffällt.

Die konkreten Engine-Trigger (`DialogStarted`/`AnswerSubmitted`/`QuestionAnswered`/`DialogCompleted`) und
wann sie publiziert werden, beschreibt [TRIGGERS.md](./TRIGGERS.md).

## Ein Pipeline-Behavior hinzufügen

1. `IPipelineBehavior<TMessage, TResponse>` implementieren (Constraint `where TMessage : notnull, IMessage`),
   in `Handle(...)` `next(message, cancellationToken)` aufrufen (oder bewusst abbrechen/werfen).
2. In `AddFlirty()` offen-generisch registrieren:
   `services.AddSingleton(typeof(IPipelineBehavior<,>), typeof(MyBehavior<,>));`

## Verifikation / Smoke-Test

Der Core enthält einen **internen** Smoke-Seam (`Flirty.Diagnostics.PingCommand`/`Pong`/
`PingCommandHandler`), der ausschließlich über `[assembly: InternalsVisibleTo("Flirty.Tests")]`
für Tests sichtbar ist (kein Teil der öffentlichen API; entfernbar, sobald echte Commands
existieren – #17/#25). Die Tests in `tests/Flirty.Tests/MediatorPipelineBehaviorTests.cs` belegen
das Akzeptanzkriterium von #14:

- ein Dummy-Command läuft durch das `LoggingPipelineBehavior` (Log-Einträge werden erfasst),
- ein ungültiger Command wird vom `ValidationPipelineBehavior` mit `ValidationException` abgewiesen.

```pwsh
dotnet test tests/Flirty.Tests
```
