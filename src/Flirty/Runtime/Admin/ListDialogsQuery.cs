using Flirty.Persistence;
using Mediator;

namespace Flirty.Runtime.Admin;

/// <summary>
/// Reads all configured dialogs as a metadata list (without the graph), sorted by key and
/// version. Purely reading.
/// </summary>
public sealed record ListDialogsQuery() : IQuery<IReadOnlyList<DialogSummary>>;

/// <summary>Handler for <see cref="ListDialogsQuery"/>.</summary>
internal sealed class ListDialogsQueryHandler : IQueryHandler<ListDialogsQuery, IReadOnlyList<DialogSummary>>
{
    private readonly IDialogAdminStore _store;

    /// <summary>Creates the handler over the given <see cref="IDialogAdminStore"/>.</summary>
    /// <param name="store">The writing repository for the configuration graph.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is <see langword="null"/>.</exception>
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
