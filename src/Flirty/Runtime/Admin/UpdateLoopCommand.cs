using System.ComponentModel.DataAnnotations;
using Flirty.Persistence;
using Mediator;

namespace Flirty.Runtime.Admin;

/// <summary>
/// Updates the loop marker <see cref="LoopId"/> in the dialog <see cref="DialogId"/> (in place).
/// The <see cref="CollectionKey"/> must remain unique within the dialog.
/// </summary>
/// <param name="DialogId">The id of the dialog the loop belongs to.</param>
/// <param name="LoopId">The primary key of the loop definition to change.</param>
/// <param name="CollectionKey">Key under which the answers gathered per iteration lie in the expression context.</param>
/// <param name="EntryQuestionId">Reference to the entry question of the loop.</param>
/// <param name="BreakingQuestionId">Reference to the breaking question.</param>
public sealed record UpdateLoopCommand(
    Guid DialogId,
    Guid LoopId,
    [property: Required] string CollectionKey,
    Guid EntryQuestionId,
    Guid BreakingQuestionId) : ICommand<LoopDetail>;

/// <summary>Handler for <see cref="UpdateLoopCommand"/>.</summary>
internal sealed class UpdateLoopCommandHandler : ICommandHandler<UpdateLoopCommand, LoopDetail>
{
    private readonly IDialogAdminStore _store;

    /// <summary>Creates the handler over the given <see cref="IDialogAdminStore"/>.</summary>
    /// <param name="store">The writing repository for the configuration graph.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is <see langword="null"/>.</exception>
    public UpdateLoopCommandHandler(IDialogAdminStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    /// <exception cref="ConfigurationNotFoundException">
    /// No loop with the given id exists in the given dialog.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Another loop with this collection key already exists in the dialog.
    /// </exception>
    /// <exception cref="DialogPublishedException">The dialog is published; its graph is locked.</exception>
    public async ValueTask<LoopDetail> Handle(UpdateLoopCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // A published version is immutable (running sessions depend on it).
        await DialogEditGuard.EnsureEditableAsync(_store, command.DialogId, cancellationToken);

        var loop = await _store.GetLoopAsync(command.LoopId, cancellationToken);
        if (loop is null || loop.DialogId != command.DialogId)
        {
            throw ConfigurationNotFoundException.ForLoop(command.LoopId);
        }

        if (await _store.LoopCollectionKeyExistsAsync(
                command.DialogId, command.CollectionKey, command.LoopId, cancellationToken))
        {
            throw new InvalidOperationException(
                $"A loop with the collection key '{command.CollectionKey}' already exists in the "
                + $"dialog '{command.DialogId}'.");
        }

        loop.CollectionKey = command.CollectionKey;
        loop.EntryQuestionId = command.EntryQuestionId;
        loop.BreakingQuestionId = command.BreakingQuestionId;

        await _store.SaveChangesAsync(cancellationToken);

        return AdminProjection.ToDetail(loop);
    }
}
