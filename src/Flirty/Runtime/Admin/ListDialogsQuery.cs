using Flirty.Persistence;
using Mediator;

namespace Flirty.Runtime.Admin;

/// <summary>
/// Liest alle konfigurierten Dialoge als Metadaten-Liste (ohne Graph), sortiert nach Schlüssel und
/// Version. Rein lesend.
/// </summary>
public sealed record ListDialogsQuery() : IQuery<IReadOnlyList<DialogSummary>>;

/// <summary>Handler für <see cref="ListDialogsQuery"/>.</summary>
internal sealed class ListDialogsQueryHandler : IQueryHandler<ListDialogsQuery, IReadOnlyList<DialogSummary>>
{
    private readonly IDialogAdminStore _store;

    /// <summary>Erstellt den Handler über den angegebenen <see cref="IDialogAdminStore"/>.</summary>
    /// <param name="store">Das schreibende Repository für den Konfigurationsgraphen.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> ist <see langword="null"/>.</exception>
    public ListDialogsQueryHandler(IDialogAdminStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<DialogSummary>> Handle(
        ListDialogsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var dialogs = await _store.ListDialogsAsync(cancellationToken);
        return [.. dialogs.Select(AdminProjection.ToSummary)];
    }
}
