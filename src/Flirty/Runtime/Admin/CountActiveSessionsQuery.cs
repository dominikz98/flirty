using Flirty.Domain;
using Flirty.Persistence;
using Mediator;

namespace Flirty.Runtime.Admin;

/// <summary>
/// Counts the <b>running</b> sessions (<see cref="SessionStatus.InProgress"/>) of the dialog version
/// <see cref="DialogId"/>. Basis for showing the deletion barrier from <see cref="DeleteDialogCommand"/>
/// <b>before</b> anyone deletes – in the designer the number is shown at the delete section.
/// </summary>
/// <remarks>
/// Deliberately <b>without</b> an HTTP endpoint (like <c>StartDialogVersionCommand</c>): the number is an
/// operating aid of the configuration tool, not part of the runtime or CRUD surface. Host apps that
/// need it send the query via the Mediator.
/// </remarks>
/// <param name="DialogId">The primary key of the dialog version.</param>
public sealed record CountActiveSessionsQuery(Guid DialogId) : IQuery<int>;

/// <summary>Handler for <see cref="CountActiveSessionsQuery"/>.</summary>
internal sealed class CountActiveSessionsQueryHandler : IQueryHandler<CountActiveSessionsQuery, int>
{
    private readonly IDialogAdminStore _store;

    /// <summary>Creates the handler over the given <see cref="IDialogAdminStore"/>.</summary>
    /// <param name="store">The repository for the configuration graph.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is <see langword="null"/>.</exception>
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
