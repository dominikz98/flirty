using Flirty.Persistence;
using Mediator;

namespace Flirty.Runtime.Admin;

/// <summary>
/// Deletes the transition <see cref="TransitionId"/> in the dialog <see cref="DialogId"/>.
/// </summary>
/// <param name="DialogId">The id of the dialog the transition belongs to.</param>
/// <param name="TransitionId">The primary key of the transition to delete.</param>
public sealed record DeleteTransitionCommand(Guid DialogId, Guid TransitionId) : ICommand<Unit>;

/// <summary>Handler for <see cref="DeleteTransitionCommand"/>.</summary>
internal sealed class DeleteTransitionCommandHandler : ICommandHandler<DeleteTransitionCommand, Unit>
{
    private readonly IDialogAdminStore _store;

    /// <summary>Creates the handler over the given <see cref="IDialogAdminStore"/>.</summary>
    /// <param name="store">The writing repository for the configuration graph.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is <see langword="null"/>.</exception>
    public DeleteTransitionCommandHandler(IDialogAdminStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    /// <exception cref="ConfigurationNotFoundException">
    /// No transition with the given id exists in the given dialog.
    /// </exception>
    /// <exception cref="DialogPublishedException">The dialog is published; its graph is locked.</exception>
    public async ValueTask<Unit> Handle(DeleteTransitionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // A published version is immutable (running sessions depend on it).
        await DialogEditGuard.EnsureEditableAsync(_store, command.DialogId, cancellationToken);

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
