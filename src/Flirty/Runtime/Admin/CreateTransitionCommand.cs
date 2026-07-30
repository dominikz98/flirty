using Flirty.Domain;
using Flirty.Persistence;
using Mediator;

namespace Flirty.Runtime.Admin;

/// <summary>
/// Creates a new conditional transition (branching) in the dialog <see cref="DialogId"/>.
/// <see cref="FromQuestionId"/>/<see cref="TargetQuestionId"/> are – in line with the FK-free domain
/// model – raw question references; their validity is the responsibility of the caller.
/// </summary>
/// <param name="DialogId">The id of the dialog the transition belongs to.</param>
/// <param name="FromQuestionId">Reference to the source question.</param>
/// <param name="TargetQuestionId">Reference to the target question.</param>
/// <param name="Expression">Optional condition expression; <see langword="null"/>/empty = unconditional.</param>
/// <param name="Priority">Priority for the evaluation order (smaller value = earlier).</param>
/// <param name="IsDefault">Indicates whether this transition is the default.</param>
public sealed record CreateTransitionCommand(
    Guid DialogId,
    Guid FromQuestionId,
    Guid TargetQuestionId,
    string? Expression,
    int Priority,
    bool IsDefault) : ICommand<TransitionDetail>;

/// <summary>Handler for <see cref="CreateTransitionCommand"/>.</summary>
internal sealed class CreateTransitionCommandHandler : ICommandHandler<CreateTransitionCommand, TransitionDetail>
{
    private readonly IDialogAdminStore _store;

    /// <summary>Creates the handler over the given <see cref="IDialogAdminStore"/>.</summary>
    /// <param name="store">The writing repository for the configuration graph.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is <see langword="null"/>.</exception>
    public CreateTransitionCommandHandler(IDialogAdminStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    /// <exception cref="ConfigurationNotFoundException">No dialog with the given id exists.</exception>
    /// <exception cref="DialogPublishedException">The dialog is published; its graph is locked.</exception>
    public async ValueTask<TransitionDetail> Handle(
        CreateTransitionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var dialog = await _store.GetDialogAsync(command.DialogId, cancellationToken)
            ?? throw ConfigurationNotFoundException.ForDialog(command.DialogId);

        // A published version is immutable (running sessions depend on it).
        DialogEditGuard.EnsureEditable(dialog);

        var transition = new Transition
        {
            Id = Guid.NewGuid(),
            DialogId = command.DialogId,
            FromQuestionId = command.FromQuestionId,
            TargetQuestionId = command.TargetQuestionId,
            Expression = command.Expression,
            Priority = command.Priority,
            IsDefault = command.IsDefault,
        };

        _store.Add(transition);
        await _store.SaveChangesAsync(cancellationToken);

        return AdminProjection.ToDetail(transition);
    }
}
