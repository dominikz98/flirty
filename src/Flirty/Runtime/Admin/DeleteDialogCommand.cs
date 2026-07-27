using Flirty.Domain;
using Flirty.Persistence;
using Mediator;

namespace Flirty.Runtime.Admin;

/// <summary>
/// Löscht den Dialog <see cref="Id"/> samt seinem gesamten Konfigurationsgraphen (Fragen, Optionen,
/// Übergänge, Schleifen, Trigger werden per Datenbank-Cascade mit entfernt).
/// </summary>
/// <remarks>
/// Solange <b>laufende</b> Sessions (<see cref="SessionStatus.InProgress"/>) auf dieser Dialogversion
/// stehen, wird das Löschen abgelehnt: Ihre Zeilen überleben den Dialog (keine Fremdschlüssel-Cascade
/// von <c>DialogSessions</c> auf <c>Dialogs</c>), wären danach aber weder fortsetzbar noch lesbar – jeder
/// Zugriff endet in einem Konflikt, weil die gepinnte Dialogversion fehlt. Wer trotzdem löschen will,
/// beendet die Sessions vorher mit <see cref="AbandonDialogSessionsCommand"/>.
/// </remarks>
/// <param name="Id">Der Primärschlüssel des zu löschenden Dialogs.</param>
public sealed record DeleteDialogCommand(Guid Id) : ICommand<Unit>;

/// <summary>Handler für <see cref="DeleteDialogCommand"/>.</summary>
internal sealed class DeleteDialogCommandHandler : ICommandHandler<DeleteDialogCommand, Unit>
{
    private readonly IDialogAdminStore _store;

    /// <summary>Erstellt den Handler über den angegebenen <see cref="IDialogAdminStore"/>.</summary>
    /// <param name="store">Das schreibende Repository für den Konfigurationsgraphen.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> ist <see langword="null"/>.</exception>
    public DeleteDialogCommandHandler(IDialogAdminStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    /// <exception cref="ConfigurationNotFoundException">Kein Dialog mit der angegebenen Id existiert.</exception>
    /// <exception cref="InvalidOperationException">
    /// Auf dieser Dialogversion laufen noch Sessions (Anzahl in der Meldung).
    /// </exception>
    public async ValueTask<Unit> Handle(DeleteDialogCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var dialog = await _store.GetDialogAsync(command.Id, cancellationToken)
            ?? throw ConfigurationNotFoundException.ForDialog(command.Id);

        var activeSessions = await _store.CountActiveSessionsAsync(command.Id, cancellationToken);
        if (activeSessions > 0)
        {
            throw new InvalidOperationException(
                $"Auf dem Dialog '{dialog.Key}' (Version {dialog.Version}) laufen noch {activeSessions} "
              + "Session(s). Sie würden das Löschen überleben, wären danach aber weder fortsetzbar noch "
              + "lesbar. Beende sie zuerst (AbandonDialogSessions) oder warte ihren Abschluss ab.");
        }

        _store.Remove(dialog);
        await _store.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
