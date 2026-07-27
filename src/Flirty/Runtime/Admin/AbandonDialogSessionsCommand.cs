using Flirty.Domain;
using Flirty.Persistence;
using Mediator;

namespace Flirty.Runtime.Admin;

/// <summary>
/// Beendet alle <b>laufenden</b> Sessions der Dialogversion <see cref="DialogId"/>, indem ihr Status auf
/// <see cref="SessionStatus.Abandoned"/> gesetzt wird. Antworten und Verlauf bleiben erhalten.
/// </summary>
/// <remarks>
/// Gegenstück zur Löschschranke aus <see cref="DeleteDialogCommand"/>: Wer eine Dialogversion samt
/// Graph entfernen will, beendet damit zuvor die Sessions, die sonst unlesbar zurückblieben. Bewusst
/// <b>kein</b> Löschen der Sessions – die Engine kennt keine Session-Löschung, und die Antwortdaten
/// sind in der Regel die eigentliche Ausbeute eines Dialogs.
/// <para>
/// Eine abgebrochene Session lässt sich nicht fortsetzen: <c>SubmitAnswerCommand</c> und
/// <c>EditAnswerCommand</c> arbeiten nur auf laufenden Sessions, und <c>StartDialogCommand</c> findet
/// als Resume-Kandidat ebenfalls nur laufende. Ein erneuter Start desselben Anwenders beginnt also
/// eine neue Session.
/// </para>
/// </remarks>
/// <param name="DialogId">Der Primärschlüssel der Dialogversion, deren Sessions beendet werden.</param>
public sealed record AbandonDialogSessionsCommand(Guid DialogId) : ICommand<AbandonSessionsResult>;

/// <summary>Ergebnis von <see cref="AbandonDialogSessionsCommand"/>.</summary>
/// <param name="DialogId">Die Dialogversion, deren Sessions beendet wurden.</param>
/// <param name="AbandonedSessions">Die Anzahl der beendeten Sessions (<c>0</c>, wenn keine lief).</param>
public sealed record AbandonSessionsResult(Guid DialogId, int AbandonedSessions);

/// <summary>Handler für <see cref="AbandonDialogSessionsCommand"/>.</summary>
internal sealed class AbandonDialogSessionsCommandHandler
    : ICommandHandler<AbandonDialogSessionsCommand, AbandonSessionsResult>
{
    private readonly IDialogAdminStore _store;

    /// <summary>Erstellt den Handler über den angegebenen <see cref="IDialogAdminStore"/>.</summary>
    /// <param name="store">Das schreibende Repository für den Konfigurationsgraphen.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> ist <see langword="null"/>.</exception>
    public AbandonDialogSessionsCommandHandler(IDialogAdminStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    /// <exception cref="ConfigurationNotFoundException">Kein Dialog mit der angegebenen Id existiert.</exception>
    public async ValueTask<AbandonSessionsResult> Handle(
        AbandonDialogSessionsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        _ = await _store.GetDialogAsync(command.DialogId, cancellationToken)
            ?? throw ConfigurationNotFoundException.ForDialog(command.DialogId);

        var sessions = await _store.GetActiveSessionsAsync(command.DialogId, cancellationToken);
        if (sessions.Count == 0)
        {
            return new AbandonSessionsResult(command.DialogId, 0);
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var session in sessions)
        {
            session.Status = SessionStatus.Abandoned;
            session.CompletedAt = now;
        }

        await _store.SaveChangesAsync(cancellationToken);

        return new AbandonSessionsResult(command.DialogId, sessions.Count);
    }
}
