using Flirty.Persistence;
using Mediator;

namespace Flirty.Runtime.Admin;

/// <summary>
/// Publishes the dialog <see cref="Id"/> (<c>IsPublished = true</c>) so that it becomes startable via
/// the runtime (<see cref="StartDialogCommand"/>). Requires a set entry question.
/// </summary>
/// <param name="Id">The primary key of the dialog to publish.</param>
public sealed record PublishDialogCommand(Guid Id) : ICommand<DialogSummary>;

/// <summary>Handler for <see cref="PublishDialogCommand"/>.</summary>
internal sealed class PublishDialogCommandHandler : ICommandHandler<PublishDialogCommand, DialogSummary>
{
    private readonly IDialogAdminStore _store;

    /// <summary>Creates the handler over the given <see cref="IDialogAdminStore"/>.</summary>
    /// <param name="store">The writing repository for the configuration graph.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is <see langword="null"/>.</exception>
    public PublishDialogCommandHandler(IDialogAdminStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    /// <exception cref="ConfigurationNotFoundException">No dialog with the given id exists.</exception>
    /// <exception cref="InvalidOperationException">The dialog has no entry question (StartQuestionId).</exception>
    public async ValueTask<DialogSummary> Handle(PublishDialogCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var dialog = await _store.GetDialogAsync(command.Id, cancellationToken)
            ?? throw ConfigurationNotFoundException.ForDialog(command.Id);

        if (dialog.StartQuestionId is null)
        {
            throw new InvalidOperationException(
                $"The dialog '{dialog.Key}' cannot be published without an entry question (StartQuestionId).");
        }

        var now = DateTimeOffset.UtcNow;

        // Per key at most one version is in production: publishing a version retires the
        // previous one. Without that, older versions would continue to carry the status "published", even
        // though StartDialogAsync starts only the highest published version anyway. Running sessions of the
        // old version stay unaffected – they depend on their dialog id, not on the status.
        foreach (var previous in await _store.GetPublishedVersionsAsync(dialog.Key, dialog.Id, cancellationToken))
        {
            previous.IsPublished = false;
            previous.UpdatedAt = now;
        }

        dialog.IsPublished = true;
        dialog.UpdatedAt = now;
        await _store.SaveChangesAsync(cancellationToken);

        return AdminProjection.ToSummary(dialog);
    }
}
