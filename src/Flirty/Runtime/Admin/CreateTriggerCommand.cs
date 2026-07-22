using System.ComponentModel.DataAnnotations;
using Flirty.Domain;
using Flirty.Persistence;
using Mediator;

namespace Flirty.Runtime.Admin;

/// <summary>
/// Legt eine neue Trigger-Definition (<see cref="TriggerDefinition"/>) im Dialog <see cref="DialogId"/>
/// an. Der Trigger beschreibt einen Rückkanal in die Host-Anwendung: <see cref="Scope"/> legt den
/// Zeitpunkt fest, <see cref="Kind"/> den Kanal und <see cref="Config"/> dessen Konfiguration (Schema:
/// <see cref="TriggerConfig"/>).
/// </summary>
/// <remarks>
/// <see cref="QuestionId"/> ist – dem FK-losen Domänenmodell entsprechend – ein roher Frage-Verweis;
/// seine Gültigkeit liegt in der Verantwortung des Aufrufers. Geprüft wird nur, ob er zum
/// <see cref="Scope"/> passt (siehe <see cref="Validate"/>).
/// </remarks>
/// <param name="DialogId">Die Id des Dialogs, zu dem der Trigger gehört.</param>
/// <param name="Scope">Der Zeitpunkt im Dialogablauf, zu dem der Trigger auslöst.</param>
/// <param name="QuestionId">
/// Die Frage, auf die bei <see cref="TriggerScope.AfterQuestion"/> gehört wird; bei allen anderen
/// Zeitpunkten <see langword="null"/>.
/// </param>
/// <param name="Kind">Der Kanal, über den die Host-Anwendung benachrichtigt wird.</param>
/// <param name="Config">Die kanal-spezifische Konfiguration als JSON (z. B. <c>{"url":"https://…"}</c>).</param>
/// <param name="Expression">Optionaler Bedingungsausdruck; <see langword="null"/>/leer = bedingungslos.</param>
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

/// <summary>Handler für <see cref="CreateTriggerCommand"/>.</summary>
internal sealed class CreateTriggerCommandHandler : ICommandHandler<CreateTriggerCommand, TriggerDetail>
{
    private readonly IDialogAdminStore _store;

    /// <summary>Erstellt den Handler über den angegebenen <see cref="IDialogAdminStore"/>.</summary>
    /// <param name="store">Das schreibende Repository für den Konfigurationsgraphen.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> ist <see langword="null"/>.</exception>
    public CreateTriggerCommandHandler(IDialogAdminStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    /// <exception cref="ConfigurationNotFoundException">Kein Dialog mit der angegebenen Id existiert.</exception>
    public async ValueTask<TriggerDetail> Handle(CreateTriggerCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        _ = await _store.GetDialogAsync(command.DialogId, cancellationToken)
            ?? throw ConfigurationNotFoundException.ForDialog(command.DialogId);

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
