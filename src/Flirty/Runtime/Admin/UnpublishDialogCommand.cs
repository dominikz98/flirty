using Flirty.Persistence;
using Mediator;

namespace Flirty.Runtime.Admin;

/// <summary>
/// Nimmt die Veröffentlichung des Dialogs <see cref="Id"/> zurück (<c>IsPublished = false</c>).
/// Empfohlen, bevor ein produktiver Dialog editiert wird.
/// </summary>
/// <param name="Id">Der Primärschlüssel des zu entpublizierenden Dialogs.</param>
public sealed record UnpublishDialogCommand(Guid Id) : ICommand<DialogSummary>;

/// <summary>Handler für <see cref="UnpublishDialogCommand"/>.</summary>
internal sealed class UnpublishDialogCommandHandler : ICommandHandler<UnpublishDialogCommand, DialogSummary>
{
    private readonly IDialogAdminStore _store;

    /// <summary>Erstellt den Handler über den angegebenen <see cref="IDialogAdminStore"/>.</summary>
    /// <param name="store">Das schreibende Repository für den Konfigurationsgraphen.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> ist <see langword="null"/>.</exception>
    public UnpublishDialogCommandHandler(IDialogAdminStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    /// <exception cref="ConfigurationNotFoundException">Kein Dialog mit der angegebenen Id existiert.</exception>
    public async ValueTask<DialogSummary> Handle(UnpublishDialogCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var dialog = await _store.GetDialogAsync(command.Id, cancellationToken)
            ?? throw ConfigurationNotFoundException.ForDialog(command.Id);

        dialog.IsPublished = false;
        dialog.UpdatedAt = DateTimeOffset.UtcNow;
        await _store.SaveChangesAsync(cancellationToken);

        return AdminProjection.ToSummary(dialog);
    }
}
