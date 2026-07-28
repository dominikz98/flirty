using Flirty.Domain;
using Flirty.Persistence;
using Mediator;

namespace Flirty.Runtime.Admin;

/// <summary>
/// Verwirft alle gespeicherten Canvas-Positionen des Dialogs <see cref="DialogId"/>. Danach ordnet
/// wieder das Auto-Layout des Designers an – die Anordnung ist damit auf den Ausgangszustand
/// zurückgesetzt, der Graph selbst bleibt unberührt.
/// </summary>
/// <remarks>
/// Wie <see cref="SetDialogLayoutCommand"/> <b>ohne</b> <c>DialogEditGuard</c>: Eine
/// <see cref="DialogLayout"/>-Zeile gehört nicht zum Graphen, ihr Verwerfen ist keine Graph-Änderung
/// (ADR 0007).
/// </remarks>
/// <param name="DialogId">Die Id des Dialogs, dessen Layout verworfen wird.</param>
public sealed record ResetDialogLayoutCommand(Guid DialogId) : ICommand<Unit>;

/// <summary>Handler für <see cref="ResetDialogLayoutCommand"/>.</summary>
internal sealed class ResetDialogLayoutCommandHandler : ICommandHandler<ResetDialogLayoutCommand, Unit>
{
    private readonly IDialogAdminStore _store;

    /// <summary>Erstellt den Handler über den angegebenen <see cref="IDialogAdminStore"/>.</summary>
    /// <param name="store">Das schreibende Repository für den Konfigurationsgraphen.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> ist <see langword="null"/>.</exception>
    public ResetDialogLayoutCommandHandler(IDialogAdminStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    /// <exception cref="ConfigurationNotFoundException">Kein Dialog mit der angegebenen Id existiert.</exception>
    public async ValueTask<Unit> Handle(ResetDialogLayoutCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Auch hier bewusst kein DialogEditGuard – siehe SetDialogLayoutCommand.
        _ = await _store.GetDialogAsync(command.DialogId, cancellationToken)
            ?? throw ConfigurationNotFoundException.ForDialog(command.DialogId);

        var rows = await _store.GetLayoutAsync(command.DialogId, cancellationToken);
        if (rows.Count > 0)
        {
            _store.RemoveRange(rows);
            await _store.SaveChangesAsync(cancellationToken);
        }

        return Unit.Value;
    }
}
