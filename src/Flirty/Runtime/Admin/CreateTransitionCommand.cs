using Flirty.Domain;
using Flirty.Persistence;
using Mediator;

namespace Flirty.Runtime.Admin;

/// <summary>
/// Legt einen neuen bedingten Übergang (Branching) im Dialog <see cref="DialogId"/> an.
/// <see cref="FromQuestionId"/>/<see cref="TargetQuestionId"/> sind – dem FK-losen Domänenmodell
/// entsprechend – rohe Frage-Verweise; ihre Gültigkeit liegt in der Verantwortung des Aufrufers.
/// </summary>
/// <param name="DialogId">Die Id des Dialogs, zu dem der Übergang gehört.</param>
/// <param name="FromQuestionId">Verweis auf die Ausgangsfrage.</param>
/// <param name="TargetQuestionId">Verweis auf die Zielfrage.</param>
/// <param name="Expression">Optionaler Bedingungsausdruck; <see langword="null"/>/leer = bedingungslos.</param>
/// <param name="Priority">Priorität für die Auswertungsreihenfolge (kleinerer Wert = früher).</param>
/// <param name="IsDefault">Gibt an, ob dieser Übergang der Default ist.</param>
public sealed record CreateTransitionCommand(
    Guid DialogId,
    Guid FromQuestionId,
    Guid TargetQuestionId,
    string? Expression,
    int Priority,
    bool IsDefault) : ICommand<TransitionDetail>;

/// <summary>Handler für <see cref="CreateTransitionCommand"/>.</summary>
internal sealed class CreateTransitionCommandHandler : ICommandHandler<CreateTransitionCommand, TransitionDetail>
{
    private readonly IDialogAdminStore _store;

    /// <summary>Erstellt den Handler über den angegebenen <see cref="IDialogAdminStore"/>.</summary>
    /// <param name="store">Das schreibende Repository für den Konfigurationsgraphen.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> ist <see langword="null"/>.</exception>
    public CreateTransitionCommandHandler(IDialogAdminStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    /// <exception cref="ConfigurationNotFoundException">Kein Dialog mit der angegebenen Id existiert.</exception>
    public async ValueTask<TransitionDetail> Handle(
        CreateTransitionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        _ = await _store.GetDialogAsync(command.DialogId, cancellationToken)
            ?? throw ConfigurationNotFoundException.ForDialog(command.DialogId);

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
