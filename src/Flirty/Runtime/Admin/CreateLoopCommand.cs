using System.ComponentModel.DataAnnotations;
using Flirty.Domain;
using Flirty.Persistence;
using Mediator;

namespace Flirty.Runtime.Admin;

/// <summary>
/// Legt einen neuen Schleifen-Marker (<see cref="LoopDefinition"/>) im Dialog <see cref="DialogId"/> an.
/// Der Marker beschreibt einen bereits über das Branching gebildeten Zyklus; der
/// <see cref="CollectionKey"/> muss innerhalb des Dialogs eindeutig sein.
/// <see cref="EntryQuestionId"/>/<see cref="BreakingQuestionId"/> sind – dem FK-losen Domänenmodell
/// entsprechend – rohe Frage-Verweise; ihre Gültigkeit liegt in der Verantwortung des Aufrufers.
/// </summary>
/// <param name="DialogId">Die Id des Dialogs, zu dem die Schleife gehört.</param>
/// <param name="CollectionKey">Schlüssel, unter dem die je Iteration gesammelten Antworten im Ausdruckskontext liegen.</param>
/// <param name="EntryQuestionId">Verweis auf die Einstiegsfrage der Schleife (Ziel des Loop-Back-Übergangs).</param>
/// <param name="BreakingQuestionId">Verweis auf die Breaking Question (deren Exit-Übergang den Zyklus verlässt).</param>
public sealed record CreateLoopCommand(
    Guid DialogId,
    [property: Required] string CollectionKey,
    Guid EntryQuestionId,
    Guid BreakingQuestionId) : ICommand<LoopDetail>;

/// <summary>Handler für <see cref="CreateLoopCommand"/>.</summary>
internal sealed class CreateLoopCommandHandler : ICommandHandler<CreateLoopCommand, LoopDetail>
{
    private readonly IDialogAdminStore _store;

    /// <summary>Erstellt den Handler über den angegebenen <see cref="IDialogAdminStore"/>.</summary>
    /// <param name="store">Das schreibende Repository für den Konfigurationsgraphen.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> ist <see langword="null"/>.</exception>
    public CreateLoopCommandHandler(IDialogAdminStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    /// <exception cref="ConfigurationNotFoundException">Kein Dialog mit der angegebenen Id existiert.</exception>
    /// <exception cref="InvalidOperationException">
    /// Im Dialog existiert bereits eine Schleife mit diesem Collection-Schlüssel.
    /// </exception>
    public async ValueTask<LoopDetail> Handle(CreateLoopCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        _ = await _store.GetDialogAsync(command.DialogId, cancellationToken)
            ?? throw ConfigurationNotFoundException.ForDialog(command.DialogId);

        if (await _store.LoopCollectionKeyExistsAsync(
                command.DialogId, command.CollectionKey, cancellationToken: cancellationToken))
        {
            throw new InvalidOperationException(
                $"Im Dialog '{command.DialogId}' existiert bereits eine Schleife mit dem "
                + $"Collection-Schlüssel '{command.CollectionKey}'.");
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
