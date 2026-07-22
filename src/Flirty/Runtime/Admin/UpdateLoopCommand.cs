using System.ComponentModel.DataAnnotations;
using Flirty.Persistence;
using Mediator;

namespace Flirty.Runtime.Admin;

/// <summary>
/// Aktualisiert den Schleifen-Marker <see cref="LoopId"/> im Dialog <see cref="DialogId"/> (In-Place).
/// Der <see cref="CollectionKey"/> muss innerhalb des Dialogs eindeutig bleiben.
/// </summary>
/// <param name="DialogId">Die Id des Dialogs, zu dem die Schleife gehört.</param>
/// <param name="LoopId">Der Primärschlüssel der zu ändernden Schleifen-Definition.</param>
/// <param name="CollectionKey">Schlüssel, unter dem die je Iteration gesammelten Antworten im Ausdruckskontext liegen.</param>
/// <param name="EntryQuestionId">Verweis auf die Einstiegsfrage der Schleife.</param>
/// <param name="BreakingQuestionId">Verweis auf die Breaking Question.</param>
public sealed record UpdateLoopCommand(
    Guid DialogId,
    Guid LoopId,
    [property: Required] string CollectionKey,
    Guid EntryQuestionId,
    Guid BreakingQuestionId) : ICommand<LoopDetail>;

/// <summary>Handler für <see cref="UpdateLoopCommand"/>.</summary>
internal sealed class UpdateLoopCommandHandler : ICommandHandler<UpdateLoopCommand, LoopDetail>
{
    private readonly IDialogAdminStore _store;

    /// <summary>Erstellt den Handler über den angegebenen <see cref="IDialogAdminStore"/>.</summary>
    /// <param name="store">Das schreibende Repository für den Konfigurationsgraphen.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> ist <see langword="null"/>.</exception>
    public UpdateLoopCommandHandler(IDialogAdminStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    /// <exception cref="ConfigurationNotFoundException">
    /// Keine Schleife mit der angegebenen Id im angegebenen Dialog existiert.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Im Dialog existiert bereits eine andere Schleife mit diesem Collection-Schlüssel.
    /// </exception>
    public async ValueTask<LoopDetail> Handle(UpdateLoopCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var loop = await _store.GetLoopAsync(command.LoopId, cancellationToken);
        if (loop is null || loop.DialogId != command.DialogId)
        {
            throw ConfigurationNotFoundException.ForLoop(command.LoopId);
        }

        if (await _store.LoopCollectionKeyExistsAsync(
                command.DialogId, command.CollectionKey, command.LoopId, cancellationToken))
        {
            throw new InvalidOperationException(
                $"Im Dialog '{command.DialogId}' existiert bereits eine Schleife mit dem "
                + $"Collection-Schlüssel '{command.CollectionKey}'.");
        }

        loop.CollectionKey = command.CollectionKey;
        loop.EntryQuestionId = command.EntryQuestionId;
        loop.BreakingQuestionId = command.BreakingQuestionId;

        await _store.SaveChangesAsync(cancellationToken);

        return AdminProjection.ToDetail(loop);
    }
}
