using Flirty.Domain;
using Flirty.Persistence;
using Mediator;

namespace Flirty.Runtime.Admin;

/// <summary>
/// Discards all stored canvas positions of the dialog <see cref="DialogId"/>. Afterwards the designer's
/// auto-layout arranges again – the arrangement is thereby reset to the initial state,
/// the graph itself stays untouched.
/// </summary>
/// <remarks>
/// Like <see cref="SetDialogLayoutCommand"/>, <b>without</b> <c>DialogEditGuard</c>: a
/// <see cref="DialogLayout"/> row does not belong to the graph, discarding it is not a graph change
/// (ADR 0007).
/// </remarks>
/// <param name="DialogId">The id of the dialog whose layout is discarded.</param>
public sealed record ResetDialogLayoutCommand(Guid DialogId) : ICommand<Unit>;

/// <summary>Handler for <see cref="ResetDialogLayoutCommand"/>.</summary>
internal sealed class ResetDialogLayoutCommandHandler : ICommandHandler<ResetDialogLayoutCommand, Unit>
{
    private readonly IDialogAdminStore _store;

    /// <summary>Creates the handler over the given <see cref="IDialogAdminStore"/>.</summary>
    /// <param name="store">The writing repository for the configuration graph.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is <see langword="null"/>.</exception>
    public ResetDialogLayoutCommandHandler(IDialogAdminStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    /// <exception cref="ConfigurationNotFoundException">No dialog with the given id exists.</exception>
    public async ValueTask<Unit> Handle(ResetDialogLayoutCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Deliberately no DialogEditGuard here either – see SetDialogLayoutCommand.
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
