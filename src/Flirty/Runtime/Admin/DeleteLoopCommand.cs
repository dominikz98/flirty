using Flirty.Persistence;
using Mediator;

namespace Flirty.Runtime.Admin;

/// <summary>
/// Löscht den Schleifen-Marker <see cref="LoopId"/> im Dialog <see cref="DialogId"/>. Der Zyklus selbst
/// bleibt bestehen – er entsteht aus den Übergängen; ohne Marker werden die Antworten des Bereichs zur
/// Laufzeit aber überschrieben statt je Iteration gesammelt.
/// </summary>
/// <param name="DialogId">Die Id des Dialogs, zu dem die Schleife gehört.</param>
/// <param name="LoopId">Der Primärschlüssel der zu löschenden Schleifen-Definition.</param>
public sealed record DeleteLoopCommand(Guid DialogId, Guid LoopId) : ICommand<Unit>;

/// <summary>Handler für <see cref="DeleteLoopCommand"/>.</summary>
internal sealed class DeleteLoopCommandHandler : ICommandHandler<DeleteLoopCommand, Unit>
{
    private readonly IDialogAdminStore _store;

    /// <summary>Erstellt den Handler über den angegebenen <see cref="IDialogAdminStore"/>.</summary>
    /// <param name="store">Das schreibende Repository für den Konfigurationsgraphen.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> ist <see langword="null"/>.</exception>
    public DeleteLoopCommandHandler(IDialogAdminStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    /// <exception cref="ConfigurationNotFoundException">
    /// Keine Schleife mit der angegebenen Id im angegebenen Dialog existiert.
    /// </exception>
    public async ValueTask<Unit> Handle(DeleteLoopCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var loop = await _store.GetLoopAsync(command.LoopId, cancellationToken);
        if (loop is null || loop.DialogId != command.DialogId)
        {
            throw ConfigurationNotFoundException.ForLoop(command.LoopId);
        }

        _store.Remove(loop);
        await _store.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
