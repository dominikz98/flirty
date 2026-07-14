using System.ComponentModel.DataAnnotations;
using Mediator;

namespace Flirty.Diagnostics;

// Interner Smoke-Test-Seam für die Mediator-Pipeline (Issue #14, Akzeptanzkriterium:
// "ein Dummy-Command läuft durch das Pipeline-Behavior"). Bewusst 'internal' – kein Teil der
// öffentlichen Package-API. Kann entfernt werden, sobald echte Commands (#17/#25) existieren.
// Für Tests sichtbar über <InternalsVisibleTo Include="Flirty.Tests" /> in Flirty.csproj.

/// <summary>Antwort des internen <see cref="PingCommand"/>-Smoke-Tests.</summary>
internal sealed record Pong(string Message);

/// <summary>Interner Smoke-Test-Command zur Verifikation der Mediator-Pipeline.</summary>
internal sealed record PingCommand([property: Required] string Message) : ICommand<Pong>;

/// <summary>Handler für den internen <see cref="PingCommand"/>.</summary>
internal sealed class PingCommandHandler : ICommandHandler<PingCommand, Pong>
{
    public ValueTask<Pong> Handle(PingCommand command, CancellationToken cancellationToken)
        => ValueTask.FromResult(new Pong(command.Message));
}
