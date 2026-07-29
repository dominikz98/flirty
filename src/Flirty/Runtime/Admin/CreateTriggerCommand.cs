using System.ComponentModel.DataAnnotations;
using Flirty.Domain;
using Flirty.Persistence;
using Mediator;

namespace Flirty.Runtime.Admin;

/// <summary>
/// Creates a new trigger definition (<see cref="TriggerDefinition"/>) in the dialog <see cref="DialogId"/>.
/// The trigger describes a back channel into the host application: <see cref="Scope"/> sets the
/// point in time, <see cref="Kind"/> the channel and <see cref="Config"/> its configuration (schema:
/// <see cref="TriggerConfig"/>).
/// </summary>
/// <remarks>
/// <see cref="QuestionId"/> is – in line with the FK-free domain model – a raw question reference;
/// its validity is the responsibility of the caller. Only whether it matches the
/// <see cref="Scope"/> is checked (see <see cref="Validate"/>).
/// </remarks>
/// <param name="DialogId">The id of the dialog the trigger belongs to.</param>
/// <param name="Scope">The point in the dialog flow at which the trigger fires.</param>
/// <param name="QuestionId">
/// The question that is listened to for <see cref="TriggerScope.AfterQuestion"/>; for all other
/// points in time <see langword="null"/>.
/// </param>
/// <param name="Kind">The channel over which the host application is notified.</param>
/// <param name="Config">The channel-specific configuration as JSON (e.g. <c>{"url":"https://…"}</c>).</param>
/// <param name="Expression">Optional condition expression; <see langword="null"/>/empty = unconditional.</param>
public sealed record CreateTriggerCommand(
    Guid DialogId,
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

/// <summary>Handler for <see cref="CreateTriggerCommand"/>.</summary>
internal sealed class CreateTriggerCommandHandler : ICommandHandler<CreateTriggerCommand, TriggerDetail>
{
    private readonly IDialogAdminStore _store;

    /// <summary>Creates the handler over the given <see cref="IDialogAdminStore"/>.</summary>
    /// <param name="store">The writing repository for the configuration graph.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is <see langword="null"/>.</exception>
    public CreateTriggerCommandHandler(IDialogAdminStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    /// <exception cref="ConfigurationNotFoundException">No dialog with the given id exists.</exception>
    /// <exception cref="DialogPublishedException">The dialog is published; its graph is locked.</exception>
    public async ValueTask<TriggerDetail> Handle(CreateTriggerCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var dialog = await _store.GetDialogAsync(command.DialogId, cancellationToken)
            ?? throw ConfigurationNotFoundException.ForDialog(command.DialogId);

        // A published version is immutable (running sessions depend on it).
        DialogEditGuard.EnsureEditable(dialog);

        var trigger = new TriggerDefinition
        {
            Id = Guid.NewGuid(),
            DialogId = command.DialogId,
            Scope = command.Scope,
            QuestionId = command.QuestionId,
            Kind = command.Kind,
            Config = command.Config,
            Expression = command.Expression,
        };

        _store.Add(trigger);
        await _store.SaveChangesAsync(cancellationToken);

        return AdminProjection.ToDetail(trigger);
    }
}
