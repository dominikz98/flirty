using Flirty.Persistence;
using Mediator;

namespace Flirty.Runtime.Admin;

/// <summary>
/// Löscht den Dialog <see cref="Id"/> samt seinem gesamten Konfigurationsgraphen (Fragen, Optionen,
/// Übergänge, Schleifen, Trigger werden per Datenbank-Cascade mit entfernt). Laufende Sessions eines
/// gelöschten Dialogs lassen sich danach nicht mehr fortsetzen.
/// </summary>
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
    public async ValueTask<Unit> Handle(DeleteDialogCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var dialog = await _store.GetDialogAsync(command.Id, cancellationToken)
            ?? throw ConfigurationNotFoundException.ForDialog(command.Id);

        _store.Remove(dialog);
        await _store.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
