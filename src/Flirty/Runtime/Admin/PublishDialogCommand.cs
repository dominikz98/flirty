using Flirty.Persistence;
using Mediator;

namespace Flirty.Runtime.Admin;

/// <summary>
/// Veröffentlicht den Dialog <see cref="Id"/> (<c>IsPublished = true</c>), sodass er über die
/// Laufzeit (<see cref="StartDialogCommand"/>) startbar wird. Setzt eine festgelegte Einstiegsfrage
/// voraus.
/// </summary>
/// <param name="Id">Der Primärschlüssel des zu veröffentlichenden Dialogs.</param>
public sealed record PublishDialogCommand(Guid Id) : ICommand<DialogSummary>;

/// <summary>Handler für <see cref="PublishDialogCommand"/>.</summary>
internal sealed class PublishDialogCommandHandler : ICommandHandler<PublishDialogCommand, DialogSummary>
{
    private readonly IDialogAdminStore _store;

    /// <summary>Erstellt den Handler über den angegebenen <see cref="IDialogAdminStore"/>.</summary>
    /// <param name="store">Das schreibende Repository für den Konfigurationsgraphen.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> ist <see langword="null"/>.</exception>
    public PublishDialogCommandHandler(IDialogAdminStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    /// <exception cref="ConfigurationNotFoundException">Kein Dialog mit der angegebenen Id existiert.</exception>
    /// <exception cref="InvalidOperationException">Der Dialog besitzt keine Einstiegsfrage (StartQuestionId).</exception>
    public async ValueTask<DialogSummary> Handle(PublishDialogCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var dialog = await _store.GetDialogAsync(command.Id, cancellationToken)
            ?? throw ConfigurationNotFoundException.ForDialog(command.Id);

        if (dialog.StartQuestionId is null)
        {
            throw new InvalidOperationException(
                $"Der Dialog '{dialog.Key}' kann ohne Einstiegsfrage (StartQuestionId) nicht veröffentlicht werden.");
        }

        var now = DateTimeOffset.UtcNow;

        // Je Schlüssel ist höchstens eine Version produktiv: Das Veröffentlichen einer Version zieht die
        // bisherige zurück. Ohne das trügen ältere Versionen weiter den Status „veröffentlicht", obwohl
        // StartDialogAsync ohnehin nur die höchste veröffentlichte Version startet. Laufende Sessions der
        // alten Version bleiben davon unberührt – sie hängen an ihrer Dialog-Id, nicht am Status.
        foreach (var previous in await _store.GetPublishedVersionsAsync(dialog.Key, dialog.Id, cancellationToken))
        {
            previous.IsPublished = false;
            previous.UpdatedAt = now;
        }

        dialog.IsPublished = true;
        dialog.UpdatedAt = now;
        await _store.SaveChangesAsync(cancellationToken);

        return AdminProjection.ToSummary(dialog);
    }
}
