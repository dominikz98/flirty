using Flirty.Persistence;
using Mediator;

namespace Flirty.Runtime.Admin;

/// <summary>
/// Updates the transition <see cref="TransitionId"/> in the dialog <see cref="DialogId"/> (in place).
/// </summary>
/// <param name="DialogId">The id of the dialog the transition belongs to.</param>
/// <param name="TransitionId">The primary key of the transition to change.</param>
/// <param name="FromQuestionId">Reference to the source question.</param>
/// <param name="TargetQuestionId">Reference to the target question.</param>
/// <param name="Expression">Optional condition expression; <see langword="null"/>/empty = unconditional.</param>
/// <param name="Priority">Priority for the evaluation order (smaller value = earlier).</param>
/// <param name="IsDefault">Indicates whether this transition is the default.</param>
public sealed record UpdateTransitionCommand(
    Guid DialogId,
    Guid TransitionId,
    Guid FromQuestionId,
    Guid TargetQuestionId,
    string? Expression,
    int Priority,
    bool IsDefault) : ICommand<TransitionDetail>;

/// <summary>Handler for <see cref="UpdateTransitionCommand"/>.</summary>
internal sealed class UpdateTransitionCommandHandler : ICommandHandler<UpdateTransitionCommand, TransitionDetail>
{
    private readonly IDialogAdminStore _store;

    /// <summary>Creates the handler over the given <see cref="IDialogAdminStore"/>.</summary>
    /// <param name="store">The writing repository for the configuration graph.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is <see langword="null"/>.</exception>
    public UpdateTransitionCommandHandler(IDialogAdminStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    /// <exception cref="ConfigurationNotFoundException">
    /// No transition with the given id exists in the given dialog.
    /// </exception>
    /// <exception cref="DialogPublishedException">The dialog is published; its graph is locked.</exception>
    public async ValueTask<TransitionDetail> Handle(
        UpdateTransitionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // A published version is immutable (running sessions depend on it).
        await DialogEditGuard.EnsureEditableAsync(_store, command.DialogId, cancellationToken);

        var transition = await _store.GetTransitionAsync(command.TransitionId, cancellationToken);
        if (transition is null || transition.DialogId != command.DialogId)
        {
            throw ConfigurationNotFoundException.ForTransition(command.TransitionId);
        }

        transition.FromQuestionId = command.FromQuestionId;
        transition.TargetQuestionId = command.TargetQuestionId;
        transition.Expression = command.Expression;
        transition.Priority = command.Priority;
        transition.IsDefault = command.IsDefault;

        await _store.SaveChangesAsync(cancellationToken);

        return AdminProjection.ToDetail(transition);
    }
}
