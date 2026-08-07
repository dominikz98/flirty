using System.ComponentModel.DataAnnotations;
using Flirty.Domain;
using Flirty.Persistence;
using Flirty.Placeholders;
using Mediator;

namespace Flirty.Runtime;

/// <summary>
/// Starts the published dialog with the business key <see cref="DialogKey"/> for the
/// user <see cref="ExternalUserKey"/>. If a running
/// (<see cref="SessionStatus.InProgress"/>) session of the currently published dialog version already
/// exists for this user, it is resumed (resume) instead of creating a new one.
/// </summary>
/// <param name="DialogKey">The business, stable key of the dialog to start.</param>
/// <param name="ExternalUserKey">The business user key of the host app (e.g. user id).</param>
public sealed record StartDialogCommand(
    [property: Required] string DialogKey,
    [property: Required] string ExternalUserKey) : ICommand<StartDialogResult>;

/// <summary>
/// Handler for <see cref="StartDialogCommand"/>: resolves the published dialog, decides
/// between resume and fresh start and returns the currently open question.
/// </summary>
internal sealed class StartDialogCommandHandler : ICommandHandler<StartDialogCommand, StartDialogResult>
{
    private readonly IDialogStore _store;
    private readonly IPublisher _publisher;
    private readonly PlaceholderRenderer _renderer;

    /// <summary>
    /// Creates the handler over the given <see cref="IDialogStore"/>, <see cref="IPublisher"/> and
    /// <see cref="PlaceholderRenderer"/>.
    /// </summary>
    /// <param name="store">The repository for dialogs and sessions.</param>
    /// <param name="publisher">The Mediator publisher for the in-process trigger notifications.</param>
    /// <param name="renderer">The renderer that fills message placeholders in the delivered question.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="store"/>, <paramref name="publisher"/> or <paramref name="renderer"/> is
    /// <see langword="null"/>.
    /// </exception>
    public StartDialogCommandHandler(IDialogStore store, IPublisher publisher, PlaceholderRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(publisher);
        ArgumentNullException.ThrowIfNull(renderer);
        _store = store;
        _publisher = publisher;
        _renderer = renderer;
    }

    /// <inheritdoc />
    /// <exception cref="DialogNotFoundException">
    /// No published dialog with the given key exists.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The published dialog has no entry question, or the current question cannot be
    /// resolved (misconfiguration).
    /// </exception>
    public async ValueTask<StartDialogResult> Handle(
        StartDialogCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var dialog = await _store.GetPublishedDialogAsync(command.DialogKey, cancellationToken)
            ?? throw DialogNotFoundException.ForKey(command.DialogKey);

        // Resume: FindActiveSessionAsync filters on dialog.Id (= the just-published version),
        // so a found session is pinned exactly to this dialog graph.
        var existing = await _store.FindActiveSessionAsync(
            dialog.Id, command.ExternalUserKey, cancellationToken);
        if (existing is not null)
        {
            return new StartDialogResult(
                existing.Id, IsResumed: true,
                await _renderer.RenderAsync(dialog, existing, existing.CurrentQuestionId, cancellationToken));
        }

        if (dialog.StartQuestionId is null)
        {
            throw new InvalidOperationException(
                $"The published dialog '{dialog.Key}' has no entry question (StartQuestionId).");
        }

        var session = new DialogSession
        {
            Id = Guid.NewGuid(),
            DialogId = dialog.Id,
            DialogVersion = dialog.Version,
            ExternalUserKey = command.ExternalUserKey,
            Status = SessionStatus.InProgress,
            CurrentQuestionId = dialog.StartQuestionId,
            StartedAt = DateTimeOffset.UtcNow,
        };

        _store.AddSession(session);
        await _store.SaveChangesAsync(cancellationToken);

        // In-process trigger (EPIC 4): only the genuine fresh start reports DialogStarted; a resume does not.
        await _publisher.Publish(
            new DialogStartedNotification(
                session.Id,
                dialog.Id,
                dialog.Key,
                command.ExternalUserKey,
                session.CurrentQuestionId,
                session.StartedAt),
            cancellationToken);

        return new StartDialogResult(
            session.Id, IsResumed: false,
            await _renderer.RenderAsync(dialog, session, session.CurrentQuestionId, cancellationToken));
    }
}
