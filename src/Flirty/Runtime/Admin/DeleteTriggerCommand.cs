using Flirty.Persistence;
using Mediator;

namespace Flirty.Runtime.Admin;

/// <summary>
/// Deletes the trigger definition <see cref="TriggerId"/> in the dialog <see cref="DialogId"/>. The dialog
/// continues to run unchanged – without the definition only the back channel is dropped (for
/// <see cref="Flirty.Domain.TriggerKind.Webhook"/> therefore the delivery).
/// </summary>
/// <param name="DialogId">The id of the dialog the trigger belongs to.</param>
/// <param name="TriggerId">The primary key of the trigger definition to delete.</param>
public sealed record DeleteTriggerCommand(Guid DialogId, Guid TriggerId) : ICommand<Unit>;

/// <summary>Handler for <see cref="DeleteTriggerCommand"/>.</summary>
internal sealed class DeleteTriggerCommandHandler : ICommandHandler<DeleteTriggerCommand, Unit>
{
    private readonly IDialogAdminStore _store;

    /// <summary>Creates the handler over the given <see cref="IDialogAdminStore"/>.</summary>
    /// <param name="store">The writing repository for the configuration graph.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is <see langword="null"/>.</exception>
    public DeleteTriggerCommandHandler(IDialogAdminStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    /// <exception cref="ConfigurationNotFoundException">
    /// No trigger with the given id exists in the given dialog.
    /// </exception>
    /// <exception cref="DialogPublishedException">The dialog is published; its graph is locked.</exception>
    public async ValueTask<Unit> Handle(DeleteTriggerCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // A published version is immutable (running sessions depend on it).
        await DialogEditGuard.EnsureEditableAsync(_store, command.DialogId, cancellationToken);

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
