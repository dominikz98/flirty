using Flirty.Persistence;
using Mediator;

namespace Flirty.Runtime.Admin;

/// <summary>
/// Deletes the question <see cref="QuestionId"/> in the dialog <see cref="DialogId"/> along with its options
/// (database cascade). Since <see cref="Flirty.Domain.Transition"/>,
/// <see cref="Flirty.Domain.LoopDefinition"/>, <see cref="Flirty.Domain.TriggerDefinition"/> and
/// <see cref="Flirty.Domain.DialogLayout"/> reference questions FK-free, referencing
/// transitions (source or target question), loop markers (entry or breaking question), triggers
/// (scope <c>AfterQuestion</c>) and the stored canvas position are removed along with it; if the
/// entry question of the dialog points to the deleted question, it is set to <see langword="null"/>.
/// </summary>
/// <param name="DialogId">The id of the dialog the question belongs to.</param>
/// <param name="QuestionId">The primary key of the question to delete.</param>
public sealed record DeleteQuestionCommand(Guid DialogId, Guid QuestionId) : ICommand<Unit>;

/// <summary>Handler for <see cref="DeleteQuestionCommand"/>.</summary>
internal sealed class DeleteQuestionCommandHandler : ICommandHandler<DeleteQuestionCommand, Unit>
{
    private readonly IDialogAdminStore _store;

    /// <summary>Creates the handler over the given <see cref="IDialogAdminStore"/>.</summary>
    /// <param name="store">The writing repository for the configuration graph.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is <see langword="null"/>.</exception>
    public DeleteQuestionCommandHandler(IDialogAdminStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    /// <exception cref="ConfigurationNotFoundException">
    /// No question with the given id exists in the given dialog.
    /// </exception>
    /// <exception cref="DialogPublishedException">The dialog is published; its graph is locked.</exception>
    public async ValueTask<Unit> Handle(DeleteQuestionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // A published version is immutable (running sessions depend on it).
        await DialogEditGuard.EnsureEditableAsync(_store, command.DialogId, cancellationToken);

        var question = await _store.GetQuestionAsync(command.QuestionId, cancellationToken);
        if (question is null || question.DialogId != command.DialogId)
        {
            throw ConfigurationNotFoundException.ForQuestion(command.QuestionId);
        }

        // Clean up orphaned (FK-free) transitions that reference this question.
        var referencingTransitions =
            await _store.GetTransitionsReferencingQuestionAsync(command.QuestionId, cancellationToken);
        if (referencingTransitions.Count > 0)
        {
            _store.RemoveRange(referencingTransitions);
        }

        // Likewise orphaned loop markers: if one were left standing on the deleted question, the
        // LoopResolver would compute at runtime against a range that no longer exists in the graph.
        var referencingLoops = await _store.GetLoopsReferencingQuestionAsync(command.QuestionId, cancellationToken);
        if (referencingLoops.Count > 0)
        {
            _store.RemoveRange(referencingLoops);
        }

        // And likewise triggers on this question (scope AfterQuestion): they would never fire again, but
        // would remain in the designer as seemingly active configuration.
        var referencingTriggers = await _store.GetTriggersReferencingQuestionAsync(command.QuestionId, cancellationToken);
        if (referencingTriggers.Count > 0)
        {
            _store.RemoveRange(referencingTriggers);
        }

        // And the stored canvas position: DialogLayout.ElementId is FK-free too, the row would otherwise
        // remain as the position of a node that no longer exists – and would be dragged along when
        // deriving the next dialog version.
        var referencingLayout =
            await _store.GetLayoutsReferencingElementAsync(command.QuestionId, cancellationToken);
        if (referencingLayout.Count > 0)
        {
            _store.RemoveRange(referencingLayout);
        }

        // Reset the entry question if it points to the deleted question.
        var dialog = await _store.GetDialogAsync(command.DialogId, cancellationToken);
        if (dialog is not null && dialog.StartQuestionId == command.QuestionId)
        {
            dialog.StartQuestionId = null;
            dialog.UpdatedAt = DateTimeOffset.UtcNow;
        }

        _store.Remove(question);
        await _store.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
