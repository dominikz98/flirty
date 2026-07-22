# ADR 0002 – Mediator (martinothamar) als In-Process-Bus

- **Status:** Akzeptiert
- **Kontext-Issue:** #14 – Mediator-Setup im Core
- **Betroffen:** `src/Flirty` (`DependencyInjection/`, `Runtime/`, `Pipeline/`), `src/Flirty.AspNetCore`

## Kontext

Flirty muss drei Dinge über **einen** Mechanismus abbilden:

1. **Engine-Operationen** (Start, Submit, Edit, Resume, Admin-CRUD) brauchen einen einheitlichen
   In-Process-Einstieg – sowohl für Host-Apps als auch für die dünne HTTP-Schicht.
2. **Trigger** brauchen einen Rückkanal in die Host-App: „nach dieser Antwort passiert bei mir X".
3. **Cross-Cutting** (Logging, deklarative Nachrichten-Validierung, fachliche Antwort-Validierung)
   muss sich **vor** jedem Handler einhängen lassen, ohne jeden Handler anzufassen.

Das Ganze läuft in einer Bibliothek, die als NuGet-Paket weitergegeben wird: Startzeit, Trimming
und Lizenz des gewählten Bausteins fallen also beim Konsumenten an, nicht bei uns.

## Entscheidung

**[Mediator (martinothamar)](https://github.com/martinothamar/Mediator)** als In-Process-Bus – eine
**Source-Generator**-basierte Implementierung des Mediator-Patterns (MIT, zentral gepinnt in
`Directory.Packages.props`). Die Zuordnung ist fest:

| Anliegen | Mediator-Baustein |
|---|---|
| Engine-Operation | `ICommand<T>` / `IQuery<T>` + Handler |
| Trigger (Rückkanal) | `INotification` + `INotificationHandler<T>` der Host-App |
| Cross-Cutting | `IPipelineBehavior<TMessage, TResponse>` |

Verdrahtet wird in `AddFlirty()` (`src/Flirty/DependencyInjection/FlirtyServiceCollectionExtensions.cs`):
`AddMediator(o => o.ServiceLifetime = ServiceLifetime.Scoped)`, danach die offen-generischen
Basis-Behaviors `LoggingPipelineBehavior<,>` und `ValidationPipelineBehavior<,>`.

## Verworfene Alternativen

- **MediatR.** Etablierter, aber **Reflection-basiert** (Assembly-Scan beim Start, Handler-Auflösung
  zur Laufzeit) – Startkosten und AOT-/Trimming-Reibung, die eine Bibliothek an jeden Konsumenten
  weitergibt. Dazu ist die Lizenzierung seit 2024 nicht mehr durchgängig frei; für ein Paket, das
  selbst unter MIT veröffentlicht wird, ist eine durchgängig freie Abhängigkeit die risikoärmere Wahl.
- **Eigene Service-Interfaces ohne Bus** (`IDialogService.SubmitAsync(…)`). Kein natürlicher
  Einhängepunkt für Cross-Cutting – Logging und Validierung landen entweder in jedem Handler oder in
  handgeschriebenen Dekoratoren je Interface. Der Trigger-Rückkanal müsste zusätzlich selbst gebaut
  werden (Events, eigene Handler-Registrierung, eigene Publish-Semantik).
- **Bus nur für Trigger, Services für die Operationen.** Zwei Mechanismen mit zwei Registrierungs-
  und Lebensdauer-Modellen für dieselbe Engine – mehr Konzepte für den Konsumenten, ohne Gewinn.

## Konsequenzen

**Positiv**

- Kein Reflection-Overhead beim Start; die Verdrahtung ist zur Compile-Zeit sichtbar.
- Cross-Cutting ist ein Einzeiler in `AddFlirty()`: die Antwort-Validierung (#30) hängt als Behavior
  **vor** Submit/Edit, statt in beiden Handlern dupliziert zu werden.
- Der Trigger-Rückkanal ist derselbe Mechanismus wie die Engine selbst: Die Host-App registriert
  `INotificationHandler<T>` – in einer Console-App identisch zu einer Web-App.

**Negativ / Bindend** – die zwei harten Regeln des Generators werden zur **Architektur-Invariante**:

1. **Handler werden nur innerhalb derselben Compilation entdeckt**, und der `AddMediator`-Aufruf muss
   im Projekt liegen, das den Generator referenziert. Deshalb liegen **alle** Commands/Queries/Handler
   **und** die Notification-Contracts im Core `Flirty`; `Flirty.AspNetCore` bleibt eine reine
   Mapping-Schicht über `ISender` und **kann** gar keine Handler beitragen (stützt
   [ADR 0003](./0003-aspnet-freier-core.md)). Ein im Sample definierter Notification-Typ erreicht über
   `IPublisher` keinen Handler.
2. **Offen-generische Behaviors werden nicht automatisch registriert**, sondern manuell per
   `AddSingleton(typeof(IPipelineBehavior<,>), typeof(MyBehavior<,>))`; die **Reihenfolge der
   Registrierung** bestimmt die Verschachtelung der Pipeline.

Zusätzlich verlangt der Generator je Nachricht einen Handler in der Core-Compilation (`MSG0005`).
Trigger-Notifications werden aber bewusst erst von Host-Apps behandelt – die Warnung ist deshalb
**je Notification-Typ** per `#pragma` unterdrückt (`src/Flirty/Runtime/Notifications/*.cs`) statt
projektweit, damit ein echt fehlender Command-/Query-Handler weiterhin den Build bricht
(`TreatWarningsAsErrors`).

Details: [MEDIATOR.md](../MEDIATOR.md), Trigger-Seite in [TRIGGERS.md](../TRIGGERS.md).
