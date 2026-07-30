using Flirty.Domain;
using Flirty.Persistence;
using Mediator;

namespace Flirty.Runtime.Admin;

/// <summary>
/// Deletes the dialog <see cref="Id"/> together with its entire configuration graph (questions, options,
/// transitions, loops, triggers are removed via database cascade).
/// </summary>
/// <remarks>
/// As long as <b>running</b> sessions (<see cref="SessionStatus.InProgress"/>) stand on this dialog
/// version, the deletion is rejected: their rows survive the dialog (no foreign-key cascade from
/// <c>DialogSessions</c> to <c>Dialogs</c>), but would afterwards be neither resumable nor readable –
/// every access ends in a conflict because the pinned dialog version is missing. To delete anyway, end
/// the sessions first with <see cref="AbandonDialogSessionsCommand"/>.
/// </remarks>
/// <param name="Id">The primary key of the dialog to delete.</param>
public sealed record DeleteDialogCommand(Guid Id) : ICommand<Unit>;

/// <summary>Handler for <see cref="DeleteDialogCommand"/>.</summary>
internal sealed class DeleteDialogCommandHandler : ICommandHandler<DeleteDialogCommand, Unit>
{
    private readonly IDialogAdminStore _store;

    /// <summary>Creates the handler over the given <see cref="IDialogAdminStore"/>.</summary>
    /// <param name="store">The writing repository for the configuration graph.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is <see langword="null"/>.</exception>
    public DeleteDialogCommandHandler(IDialogAdminStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    /// <exception cref="ConfigurationNotFoundException">No dialog with the given id exists.</exception>
    /// <exception cref="InvalidOperationException">
    /// Sessions are still running on this dialog version (count in the message).
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
                $"On the dialog '{dialog.Key}' (version {dialog.Version}) {activeSessions} session(s) "
              + "are still running. They would survive the deletion but would afterwards be neither "
              + "resumable nor readable. End them first (AbandonDialogSessions) or wait for them to "
              + "complete.");
        }

        _store.Remove(dialog);
        await _store.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
