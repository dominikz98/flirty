using Flirty.Persistence;
using Mediator;

namespace Flirty.Runtime.Admin;

/// <summary>
/// Unpublishes the dialog <see cref="Id"/> (<c>IsPublished = false</c>).
/// Recommended before a productive dialog is edited.
/// </summary>
/// <param name="Id">The primary key of the dialog to unpublish.</param>
public sealed record UnpublishDialogCommand(Guid Id) : ICommand<DialogSummary>;

/// <summary>Handler for <see cref="UnpublishDialogCommand"/>.</summary>
internal sealed class UnpublishDialogCommandHandler : ICommandHandler<UnpublishDialogCommand, DialogSummary>
{
    private readonly IDialogAdminStore _store;

    /// <summary>Creates the handler over the given <see cref="IDialogAdminStore"/>.</summary>
    /// <param name="store">The writing repository for the configuration graph.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is <see langword="null"/>.</exception>
    public UnpublishDialogCommandHandler(IDialogAdminStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    /// <exception cref="ConfigurationNotFoundException">No dialog with the given id exists.</exception>
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
