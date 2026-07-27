using Flirty.Domain;
using Flirty.Persistence;
using Mediator;

namespace Flirty.Runtime.Admin;

/// <summary>
/// Zählt die <b>laufenden</b> Sessions (<see cref="SessionStatus.InProgress"/>) der Dialogversion
/// <see cref="DialogId"/>. Grundlage dafür, die Löschschranke aus <see cref="DeleteDialogCommand"/>
/// anzuzeigen, <b>bevor</b> jemand löscht – im Designer steht die Zahl am Löschen-Abschnitt.
/// </summary>
/// <remarks>
/// Bewusst <b>ohne</b> HTTP-Endpunkt (wie <c>StartDialogVersionCommand</c>): Die Zahl ist eine
/// Bedien-Hilfe des Konfigurations-Werkzeugs, kein Teil der Laufzeit- oder CRUD-Fläche. Host-Apps, die
/// sie brauchen, senden die Query über den Mediator.
/// </remarks>
/// <param name="DialogId">Der Primärschlüssel der Dialogversion.</param>
public sealed record CountActiveSessionsQuery(Guid DialogId) : IQuery<int>;

/// <summary>Handler für <see cref="CountActiveSessionsQuery"/>.</summary>
internal sealed class CountActiveSessionsQueryHandler : IQueryHandler<CountActiveSessionsQuery, int>
{
    private readonly IDialogAdminStore _store;

    /// <summary>Erstellt den Handler über den angegebenen <see cref="IDialogAdminStore"/>.</summary>
    /// <param name="store">Das Repository für den Konfigurationsgraphen.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> ist <see langword="null"/>.</exception>
    public CountActiveSessionsQueryHandler(IDialogAdminStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    public async ValueTask<int> Handle(CountActiveSessionsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await _store.CountActiveSessionsAsync(query.DialogId, cancellationToken);
    }
}
