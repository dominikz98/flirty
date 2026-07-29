using System.ComponentModel.DataAnnotations;
using Flirty.Domain;
using Flirty.Persistence;
using Mediator;

namespace Flirty.Runtime.Admin;

/// <summary>
/// Creates a new loop marker (<see cref="LoopDefinition"/>) in the dialog <see cref="DialogId"/>.
/// The marker describes a cycle already formed via the branching; the
/// <see cref="CollectionKey"/> must be unique within the dialog.
/// <see cref="EntryQuestionId"/>/<see cref="BreakingQuestionId"/> are – in line with the FK-free domain
/// model – raw question references; their validity is the responsibility of the caller.
/// </summary>
/// <param name="DialogId">The id of the dialog the loop belongs to.</param>
/// <param name="CollectionKey">Key under which the answers gathered per iteration lie in the expression context.</param>
/// <param name="EntryQuestionId">Reference to the entry question of the loop (target of the loop-back transition).</param>
/// <param name="BreakingQuestionId">Reference to the breaking question (whose exit transition leaves the cycle).</param>
public sealed record CreateLoopCommand(
    Guid DialogId,
    [property: Required] string CollectionKey,
    Guid EntryQuestionId,
    Guid BreakingQuestionId) : ICommand<LoopDetail>;

/// <summary>Handler for <see cref="CreateLoopCommand"/>.</summary>
internal sealed class CreateLoopCommandHandler : ICommandHandler<CreateLoopCommand, LoopDetail>
{
    private readonly IDialogAdminStore _store;

    /// <summary>Creates the handler over the given <see cref="IDialogAdminStore"/>.</summary>
    /// <param name="store">The writing repository for the configuration graph.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is <see langword="null"/>.</exception>
    public CreateLoopCommandHandler(IDialogAdminStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    /// <exception cref="ConfigurationNotFoundException">No dialog with the given id exists.</exception>
    /// <exception cref="InvalidOperationException">
    /// A loop with this collection key already exists in the dialog.
    /// </exception>
    /// <exception cref="DialogPublishedException">The dialog is published; its graph is locked.</exception>
    public async ValueTask<LoopDetail> Handle(CreateLoopCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var dialog = await _store.GetDialogAsync(command.DialogId, cancellationToken)
            ?? throw ConfigurationNotFoundException.ForDialog(command.DialogId);

        // A published version is immutable (running sessions depend on it).
        DialogEditGuard.EnsureEditable(dialog);

        if (await _store.LoopCollectionKeyExistsAsync(
                command.DialogId, command.CollectionKey, cancellationToken: cancellationToken))
        {
            throw new InvalidOperationException(
                $"A loop with the collection key '{command.CollectionKey}' already exists in the "
                + $"dialog '{command.DialogId}'.");
        }

        var loop = new LoopDefinition
        {
            Id = Guid.NewGuid(),
            DialogId = command.DialogId,
            CollectionKey = command.CollectionKey,
            EntryQuestionId = command.EntryQuestionId,
            BreakingQuestionId = command.BreakingQuestionId,
        };

        _store.Add(loop);
        await _store.SaveChangesAsync(cancellationToken);

        return AdminProjection.ToDetail(loop);
    }
}
