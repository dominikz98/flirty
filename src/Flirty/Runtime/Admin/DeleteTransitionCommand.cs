using Flirty.Persistence;
using Mediator;

namespace Flirty.Runtime.Admin;

/// <summary>
/// Löscht den Übergang <see cref="TransitionId"/> im Dialog <see cref="DialogId"/>.
/// </summary>
/// <param name="DialogId">Die Id des Dialogs, zu dem der Übergang gehört.</param>
/// <param name="TransitionId">Der Primärschlüssel des zu löschenden Übergangs.</param>
public sealed record DeleteTransitionCommand(Guid DialogId, Guid TransitionId) : ICommand<Unit>;

/// <summary>Handler für <see cref="DeleteTransitionCommand"/>.</summary>
internal sealed class DeleteTransitionCommandHandler : ICommandHandler<DeleteTransitionCommand, Unit>
{
    private readonly IDialogAdminStore _store;

    /// <summary>Erstellt den Handler über den angegebenen <see cref="IDialogAdminStore"/>.</summary>
    /// <param name="store">Das schreibende Repository für den Konfigurationsgraphen.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> ist <see langword="null"/>.</exception>
    public DeleteTransitionCommandHandler(IDialogAdminStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    /// <exception cref="ConfigurationNotFoundException">
    /// Kein Übergang mit der angegebenen Id im angegebenen Dialog existiert.
    /// </exception>
    public async ValueTask<Unit> Handle(DeleteTransitionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var transition = await _store.GetTransitionAsync(command.TransitionId, cancellationToken);
        if (transition is null || transition.DialogId != command.DialogId)
        {
            throw ConfigurationNotFoundException.ForTransition(command.TransitionId);
        }

        _store.Remove(transition);
        await _store.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
