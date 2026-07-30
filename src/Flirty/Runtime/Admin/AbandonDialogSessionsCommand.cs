using Flirty.Domain;
using Flirty.Persistence;
using Mediator;

namespace Flirty.Runtime.Admin;

/// <summary>
/// Ends all <b>running</b> sessions of the dialog version <see cref="DialogId"/> by setting their status to
/// <see cref="SessionStatus.Abandoned"/>. Answers and history are preserved.
/// </summary>
/// <remarks>
/// Counterpart to the deletion barrier from <see cref="DeleteDialogCommand"/>: whoever wants to remove a
/// dialog version along with its graph first ends the sessions that would otherwise remain unreadable.
/// Deliberately <b>no</b> deletion of the sessions – the engine knows no session deletion, and the answer
/// data is usually the actual yield of a dialog.
/// <para>
/// An abandoned session cannot be resumed: <c>SubmitAnswerCommand</c> and
/// <c>EditAnswerCommand</c> work only on running sessions, and <c>StartDialogCommand</c> finds
/// as a resume candidate likewise only running ones. A fresh start of the same user therefore begins
/// a new session.
/// </para>
/// </remarks>
/// <param name="DialogId">The primary key of the dialog version whose sessions are ended.</param>
public sealed record AbandonDialogSessionsCommand(Guid DialogId) : ICommand<AbandonSessionsResult>;

/// <summary>Result of <see cref="AbandonDialogSessionsCommand"/>.</summary>
/// <param name="DialogId">The dialog version whose sessions were ended.</param>
/// <param name="AbandonedSessions">The number of ended sessions (<c>0</c> if none were running).</param>
public sealed record AbandonSessionsResult(Guid DialogId, int AbandonedSessions);

/// <summary>Handler for <see cref="AbandonDialogSessionsCommand"/>.</summary>
internal sealed class AbandonDialogSessionsCommandHandler
    : ICommandHandler<AbandonDialogSessionsCommand, AbandonSessionsResult>
{
    private readonly IDialogAdminStore _store;

    /// <summary>Creates the handler over the given <see cref="IDialogAdminStore"/>.</summary>
    /// <param name="store">The writing repository for the configuration graph.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is <see langword="null"/>.</exception>
    public AbandonDialogSessionsCommandHandler(IDialogAdminStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    /// <exception cref="ConfigurationNotFoundException">No dialog with the given id exists.</exception>
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
