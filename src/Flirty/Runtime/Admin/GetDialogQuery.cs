using System.ComponentModel.DataAnnotations;
using Flirty.Persistence;
using Mediator;

namespace Flirty.Runtime.Admin;

/// <summary>
/// Liest den Dialog <see cref="Id"/> samt seinem Konfigurationsgraphen (Fragen inkl. Optionen,
/// Übergänge und Schleifen-Marker). Rein lesend.
/// </summary>
/// <param name="Id">Der Primärschlüssel des abzufragenden Dialogs.</param>
public sealed record GetDialogQuery([property: Required] Guid Id) : IQuery<DialogDetail>;

/// <summary>Handler für <see cref="GetDialogQuery"/>.</summary>
internal sealed class GetDialogQueryHandler : IQueryHandler<GetDialogQuery, DialogDetail>
{
    private readonly IDialogAdminStore _store;

    /// <summary>Erstellt den Handler über den angegebenen <see cref="IDialogAdminStore"/>.</summary>
    /// <param name="store">Das schreibende Repository für den Konfigurationsgraphen.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> ist <see langword="null"/>.</exception>
    public GetDialogQueryHandler(IDialogAdminStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    /// <exception cref="ConfigurationNotFoundException">Kein Dialog mit der angegebenen Id existiert.</exception>
    public async ValueTask<DialogDetail> Handle(GetDialogQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var dialog = await _store.GetDialogGraphAsync(query.Id, cancellationToken)
            ?? throw ConfigurationNotFoundException.ForDialog(query.Id);

        return AdminProjection.ToDetail(dialog);
    }
}
