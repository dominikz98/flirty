using System.ComponentModel.DataAnnotations;
using Flirty.Domain;
using Flirty.Persistence;
using Mediator;

namespace Flirty.Runtime.Admin;

/// <summary>
/// Aktualisiert die Trigger-Definition <see cref="TriggerId"/> im Dialog <see cref="DialogId"/>
/// (In-Place). Es gelten dieselben Querfeld-Regeln wie beim Anlegen (siehe
/// <see cref="CreateTriggerCommand"/>).
/// </summary>
/// <param name="DialogId">Die Id des Dialogs, zu dem der Trigger gehört.</param>
/// <param name="TriggerId">Der Primärschlüssel der zu ändernden Trigger-Definition.</param>
/// <param name="Scope">Der Zeitpunkt im Dialogablauf, zu dem der Trigger auslöst.</param>
/// <param name="QuestionId">
/// Die Frage, auf die bei <see cref="TriggerScope.AfterQuestion"/> gehört wird; sonst <see langword="null"/>.
/// </param>
/// <param name="Kind">Der Kanal, über den die Host-Anwendung benachrichtigt wird.</param>
/// <param name="Config">Die kanal-spezifische Konfiguration als JSON (Schema: <see cref="TriggerConfig"/>).</param>
/// <param name="Expression">Optionaler Bedingungsausdruck; <see langword="null"/>/leer = bedingungslos.</param>
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

/// <summary>Handler für <see cref="UpdateTriggerCommand"/>.</summary>
internal sealed class UpdateTriggerCommandHandler : ICommandHandler<UpdateTriggerCommand, TriggerDetail>
{
    private readonly IDialogAdminStore _store;

    /// <summary>Erstellt den Handler über den angegebenen <see cref="IDialogAdminStore"/>.</summary>
    /// <param name="store">Das schreibende Repository für den Konfigurationsgraphen.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> ist <see langword="null"/>.</exception>
    public UpdateTriggerCommandHandler(IDialogAdminStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    /// <exception cref="ConfigurationNotFoundException">
    /// Kein Trigger mit der angegebenen Id im angegebenen Dialog existiert.
    /// </exception>
    public async ValueTask<TriggerDetail> Handle(UpdateTriggerCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

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
