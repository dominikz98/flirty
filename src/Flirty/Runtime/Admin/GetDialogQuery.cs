using System.ComponentModel.DataAnnotations;
using Flirty.Persistence;
using Mediator;

namespace Flirty.Runtime.Admin;

/// <summary>
/// Reads the dialog <see cref="Id"/> along with its configuration graph (questions incl. options,
/// transitions, loop markers and triggers) as well as the stored canvas positions. Purely reading.
/// </summary>
/// <param name="Id">The primary key of the dialog to query.</param>
public sealed record GetDialogQuery([property: Required] Guid Id) : IQuery<DialogDetail>;

/// <summary>Handler for <see cref="GetDialogQuery"/>.</summary>
internal sealed class GetDialogQueryHandler : IQueryHandler<GetDialogQuery, DialogDetail>
{
    private readonly IDialogAdminStore _store;

    /// <summary>Creates the handler over the given <see cref="IDialogAdminStore"/>.</summary>
    /// <param name="store">The writing repository for the configuration graph.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is <see langword="null"/>.</exception>
    public GetDialogQueryHandler(IDialogAdminStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    /// <exception cref="ConfigurationNotFoundException">No dialog with the given id exists.</exception>
    public async ValueTask<DialogDetail> Handle(GetDialogQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var dialog = await _store.GetDialogGraphAsync(query.Id, cancellationToken)
            ?? throw ConfigurationNotFoundException.ForDialog(query.Id);

        return AdminProjection.ToDetail(dialog);
    }
}
