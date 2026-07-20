using Flirty.Persistence;
using Mediator;

namespace Flirty.Runtime.Admin;

/// <summary>
/// Aktualisiert den Übergang <see cref="TransitionId"/> im Dialog <see cref="DialogId"/> (In-Place).
/// </summary>
/// <param name="DialogId">Die Id des Dialogs, zu dem der Übergang gehört.</param>
/// <param name="TransitionId">Der Primärschlüssel des zu ändernden Übergangs.</param>
/// <param name="FromQuestionId">Verweis auf die Ausgangsfrage.</param>
/// <param name="TargetQuestionId">Verweis auf die Zielfrage.</param>
/// <param name="Expression">Optionaler Bedingungsausdruck; <see langword="null"/>/leer = bedingungslos.</param>
/// <param name="Priority">Priorität für die Auswertungsreihenfolge (kleinerer Wert = früher).</param>
/// <param name="IsDefault">Gibt an, ob dieser Übergang der Default ist.</param>
public sealed record UpdateTransitionCommand(
    Guid DialogId,
    Guid TransitionId,
    Guid FromQuestionId,
    Guid TargetQuestionId,
    string? Expression,
    int Priority,
    bool IsDefault) : ICommand<TransitionDetail>;

/// <summary>Handler für <see cref="UpdateTransitionCommand"/>.</summary>
internal sealed class UpdateTransitionCommandHandler : ICommandHandler<UpdateTransitionCommand, TransitionDetail>
{
    private readonly IDialogAdminStore _store;

    /// <summary>Erstellt den Handler über den angegebenen <see cref="IDialogAdminStore"/>.</summary>
    /// <param name="store">Das schreibende Repository für den Konfigurationsgraphen.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> ist <see langword="null"/>.</exception>
    public UpdateTransitionCommandHandler(IDialogAdminStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    /// <exception cref="ConfigurationNotFoundException">
    /// Kein Übergang mit der angegebenen Id im angegebenen Dialog existiert.
    /// </exception>
    public async ValueTask<TransitionDetail> Handle(
        UpdateTransitionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

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
