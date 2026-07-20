using System.ComponentModel.DataAnnotations;
using Flirty.Domain;
using Flirty.Persistence;
using Mediator;

namespace Flirty.Runtime.Admin;

/// <summary>
/// Legt einen neuen (unveröffentlichten) Dialog mit dem fachlichen Schlüssel <see cref="Key"/> an.
/// Version wird auf <c>1</c> und <c>IsPublished</c> auf <see langword="false"/> gesetzt; die
/// Einstiegsfrage (<c>StartQuestionId</c>) bleibt zunächst offen und wird per
/// <see cref="UpdateDialogCommand"/> festgelegt, sobald Fragen existieren.
/// </summary>
/// <param name="Key">Der fachliche, stabile Schlüssel des Dialogs (muss eindeutig sein).</param>
/// <param name="Name">Der Anzeigename des Dialogs.</param>
/// <param name="Description">Die optionale Beschreibung des Dialogs.</param>
public sealed record CreateDialogCommand(
    [property: Required] string Key,
    [property: Required] string Name,
    string? Description) : ICommand<DialogSummary>;

/// <summary>Handler für <see cref="CreateDialogCommand"/>.</summary>
internal sealed class CreateDialogCommandHandler : ICommandHandler<CreateDialogCommand, DialogSummary>
{
    private readonly IDialogAdminStore _store;

    /// <summary>Erstellt den Handler über den angegebenen <see cref="IDialogAdminStore"/>.</summary>
    /// <param name="store">Das schreibende Repository für den Konfigurationsgraphen.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> ist <see langword="null"/>.</exception>
    public CreateDialogCommandHandler(IDialogAdminStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Es existiert bereits ein Dialog mit diesem Schlüssel.</exception>
    public async ValueTask<DialogSummary> Handle(CreateDialogCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await _store.DialogKeyExistsAsync(command.Key, cancellationToken: cancellationToken))
        {
            throw new InvalidOperationException(
                $"Es existiert bereits ein Dialog mit dem Schlüssel '{command.Key}'.");
        }

        var now = DateTimeOffset.UtcNow;
        var dialog = new Dialog
        {
            Id = Guid.NewGuid(),
            Key = command.Key,
            Name = command.Name,
            Description = command.Description,
            Version = 1,
            IsPublished = false,
            StartQuestionId = null,
            CreatedAt = now,
            UpdatedAt = now,
        };

        _store.Add(dialog);
        await _store.SaveChangesAsync(cancellationToken);

        return AdminProjection.ToSummary(dialog);
    }
}
