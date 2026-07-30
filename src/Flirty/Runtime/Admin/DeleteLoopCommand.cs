using Flirty.Persistence;
using Mediator;

namespace Flirty.Runtime.Admin;

/// <summary>
/// Deletes the loop marker <see cref="LoopId"/> in the dialog <see cref="DialogId"/>. The cycle itself
/// remains – it arises from the transitions; without a marker, however, the answers of the range are
/// overwritten at runtime instead of gathered per iteration.
/// </summary>
/// <param name="DialogId">The id of the dialog the loop belongs to.</param>
/// <param name="LoopId">The primary key of the loop definition to delete.</param>
public sealed record DeleteLoopCommand(Guid DialogId, Guid LoopId) : ICommand<Unit>;

/// <summary>Handler for <see cref="DeleteLoopCommand"/>.</summary>
internal sealed class DeleteLoopCommandHandler : ICommandHandler<DeleteLoopCommand, Unit>
{
    private readonly IDialogAdminStore _store;

    /// <summary>Creates the handler over the given <see cref="IDialogAdminStore"/>.</summary>
    /// <param name="store">The writing repository for the configuration graph.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is <see langword="null"/>.</exception>
    public DeleteLoopCommandHandler(IDialogAdminStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    /// <exception cref="ConfigurationNotFoundException">
    /// No loop with the given id exists in the given dialog.
    /// </exception>
    /// <exception cref="DialogPublishedException">The dialog is published; its graph is locked.</exception>
    public async ValueTask<Unit> Handle(DeleteLoopCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // A published version is immutable (running sessions depend on it).
        await DialogEditGuard.EnsureEditableAsync(_store, command.DialogId, cancellationToken);

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
