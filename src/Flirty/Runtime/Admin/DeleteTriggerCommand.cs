using Flirty.Persistence;
using Mediator;

namespace Flirty.Runtime.Admin;

/// <summary>
/// Löscht die Trigger-Definition <see cref="TriggerId"/> im Dialog <see cref="DialogId"/>. Der Dialog
/// läuft unverändert weiter – ohne Definition entfällt lediglich der Rückkanal (bei
/// <see cref="Flirty.Domain.TriggerKind.Webhook"/> also die Zustellung).
/// </summary>
/// <param name="DialogId">Die Id des Dialogs, zu dem der Trigger gehört.</param>
/// <param name="TriggerId">Der Primärschlüssel der zu löschenden Trigger-Definition.</param>
public sealed record DeleteTriggerCommand(Guid DialogId, Guid TriggerId) : ICommand<Unit>;

/// <summary>Handler für <see cref="DeleteTriggerCommand"/>.</summary>
internal sealed class DeleteTriggerCommandHandler : ICommandHandler<DeleteTriggerCommand, Unit>
{
    private readonly IDialogAdminStore _store;

    /// <summary>Erstellt den Handler über den angegebenen <see cref="IDialogAdminStore"/>.</summary>
    /// <param name="store">Das schreibende Repository für den Konfigurationsgraphen.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> ist <see langword="null"/>.</exception>
    public DeleteTriggerCommandHandler(IDialogAdminStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    /// <exception cref="ConfigurationNotFoundException">
    /// Kein Trigger mit der angegebenen Id im angegebenen Dialog existiert.
    /// </exception>
    public async ValueTask<Unit> Handle(DeleteTriggerCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var trigger = await _store.GetTriggerAsync(command.TriggerId, cancellationToken);
        if (trigger is null || trigger.DialogId != command.DialogId)
        {
            throw ConfigurationNotFoundException.ForTrigger(command.TriggerId);
        }

        _store.Remove(trigger);
        await _store.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
