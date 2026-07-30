using System.ComponentModel.DataAnnotations;
using Flirty.Domain;
using Flirty.Persistence;
using Mediator;

namespace Flirty.Runtime.Admin;

/// <summary>
/// Updates the trigger definition <see cref="TriggerId"/> in the dialog <see cref="DialogId"/>
/// (in place). The same cross-field rules as on creation apply (see
/// <see cref="CreateTriggerCommand"/>).
/// </summary>
/// <param name="DialogId">The id of the dialog the trigger belongs to.</param>
/// <param name="TriggerId">The primary key of the trigger definition to change.</param>
/// <param name="Scope">The point in the dialog flow at which the trigger fires.</param>
/// <param name="QuestionId">
/// The question that is listened to for <see cref="TriggerScope.AfterQuestion"/>; otherwise <see langword="null"/>.
/// </param>
/// <param name="Kind">The channel over which the host application is notified.</param>
/// <param name="Config">The channel-specific configuration as JSON (schema: <see cref="TriggerConfig"/>).</param>
/// <param name="Expression">Optional condition expression; <see langword="null"/>/empty = unconditional.</param>
public sealed record UpdateTriggerCommand(
    Guid DialogId,
    Guid TriggerId,
    TriggerScope Scope,
    Guid? QuestionId,
    TriggerKind Kind,
    [property: Required] string Config,
    string? Expression) : ICommand<TriggerDetail>, IValidatableObject
{
    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        => TriggerValidation.Validate(Scope, QuestionId, Kind, Config);
}

/// <summary>Handler for <see cref="UpdateTriggerCommand"/>.</summary>
internal sealed class UpdateTriggerCommandHandler : ICommandHandler<UpdateTriggerCommand, TriggerDetail>
{
    private readonly IDialogAdminStore _store;

    /// <summary>Creates the handler over the given <see cref="IDialogAdminStore"/>.</summary>
    /// <param name="store">The writing repository for the configuration graph.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is <see langword="null"/>.</exception>
    public UpdateTriggerCommandHandler(IDialogAdminStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    /// <exception cref="ConfigurationNotFoundException">
    /// No trigger with the given id exists in the given dialog.
    /// </exception>
    /// <exception cref="DialogPublishedException">The dialog is published; its graph is locked.</exception>
    public async ValueTask<TriggerDetail> Handle(UpdateTriggerCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // A published version is immutable (running sessions depend on it).
        await DialogEditGuard.EnsureEditableAsync(_store, command.DialogId, cancellationToken);

        var trigger = await _store.GetTriggerAsync(command.TriggerId, cancellationToken);
        if (trigger is null || trigger.DialogId != command.DialogId)
        {
            throw ConfigurationNotFoundException.ForTrigger(command.TriggerId);
        }

        trigger.Scope = command.Scope;
        trigger.QuestionId = command.QuestionId;
        trigger.Kind = command.Kind;
        trigger.Config = command.Config;
        trigger.Expression = command.Expression;

        await _store.SaveChangesAsync(cancellationToken);

        return AdminProjection.ToDetail(trigger);
    }
}
