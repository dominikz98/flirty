using System.ComponentModel.DataAnnotations;
using Mediator;

namespace Flirty.Diagnostics;

// Internal smoke-test seam for the mediator pipeline (issue #14, acceptance criterion:
// "a dummy command runs through the pipeline behavior"). Deliberately 'internal' – not part of the
// public package API. Can be removed once real commands (#17/#25) exist.
// Made visible to tests via <InternalsVisibleTo Include="Flirty.Tests" /> in Flirty.csproj.

/// <summary>Response of the internal <see cref="PingCommand"/> smoke test.</summary>
internal sealed record Pong(string Message);

/// <summary>Internal smoke-test command for verifying the mediator pipeline.</summary>
internal sealed record PingCommand([property: Required] string Message) : ICommand<Pong>;

/// <summary>Handler for the internal <see cref="PingCommand"/>.</summary>
internal sealed class PingCommandHandler : ICommandHandler<PingCommand, Pong>
{
    public ValueTask<Pong> Handle(PingCommand command, CancellationToken cancellationToken)
        => ValueTask.FromResult(new Pong(command.Message));
}
