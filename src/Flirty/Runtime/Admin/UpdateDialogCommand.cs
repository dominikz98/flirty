using System.ComponentModel.DataAnnotations;
using Flirty.Persistence;
using Mediator;

namespace Flirty.Runtime.Admin;

/// <summary>
/// Aktualisiert die Metadaten des Dialogs <see cref="Id"/> (In-Place). Setzt optional die
/// Einstiegsfrage (<see cref="StartQuestionId"/>); ist sie nicht <see langword="null"/>, muss sie
/// auf eine Frage <b>dieses</b> Dialogs verweisen.
/// </summary>
/// <param name="Id">Der Primärschlüssel des zu ändernden Dialogs.</param>
/// <param name="Key">Der fachliche, stabile Schlüssel des Dialogs (muss eindeutig bleiben).</param>
/// <param name="Name">Der Anzeigename des Dialogs.</param>
/// <param name="Description">Die optionale Beschreibung des Dialogs.</param>
/// <param name="StartQuestionId">Optionaler Verweis auf die Einstiegsfrage dieses Dialogs.</param>
public sealed record UpdateDialogCommand(
    Guid Id,
    [property: Required] string Key,
    [property: Required] string Name,
    string? Description,
    Guid? StartQuestionId) : ICommand<DialogSummary>;

/// <summary>Handler für <see cref="UpdateDialogCommand"/>.</summary>
internal sealed class UpdateDialogCommandHandler : ICommandHandler<UpdateDialogCommand, DialogSummary>
{
    private readonly IDialogAdminStore _store;

    /// <summary>Erstellt den Handler über den angegebenen <see cref="IDialogAdminStore"/>.</summary>
    /// <param name="store">Das schreibende Repository für den Konfigurationsgraphen.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> ist <see langword="null"/>.</exception>
    public UpdateDialogCommandHandler(IDialogAdminStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    /// <exception cref="ConfigurationNotFoundException">Kein Dialog mit der angegebenen Id existiert.</exception>
    /// <exception cref="InvalidOperationException">
    /// Der Schlüssel kollidiert mit einem anderen Dialog, oder die angegebene Einstiegsfrage gehört
    /// nicht zu diesem Dialog.
    /// </exception>
    public async ValueTask<DialogSummary> Handle(UpdateDialogCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var dialog = await _store.GetDialogAsync(command.Id, cancellationToken)
            ?? throw ConfigurationNotFoundException.ForDialog(command.Id);

        if (await _store.DialogKeyExistsAsync(command.Key, command.Id, cancellationToken))
        {
            throw new InvalidOperationException(
                $"Es existiert bereits ein anderer Dialog mit dem Schlüssel '{command.Key}'.");
        }

        if (command.StartQuestionId is Guid startQuestionId)
        {
            var startQuestion = await _store.GetQuestionAsync(startQuestionId, cancellationToken);
            if (startQuestion is null || startQuestion.DialogId != dialog.Id)
            {
                throw new InvalidOperationException(
                    $"Die Einstiegsfrage '{startQuestionId}' gehört nicht zum Dialog '{dialog.Id}'.");
            }
        }

        dialog.Key = command.Key;
        dialog.Name = command.Name;
        dialog.Description = command.Description;
        dialog.StartQuestionId = command.StartQuestionId;
        dialog.UpdatedAt = DateTimeOffset.UtcNow;

        await _store.SaveChangesAsync(cancellationToken);

        return AdminProjection.ToSummary(dialog);
    }
}
