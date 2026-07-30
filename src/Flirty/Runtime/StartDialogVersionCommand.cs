using System.ComponentModel.DataAnnotations;
using Flirty.Domain;
using Flirty.Persistence;
using Mediator;

namespace Flirty.Runtime;

/// <summary>
/// Starts a session against the <b>concrete dialog version</b> <see cref="DialogId"/> for the user
/// <see cref="ExternalUserKey"/> – <b>regardless of the publication status</b>. Intended for preview
/// and test scenarios (designer test runner, issue #43) in which a draft is to be played through
/// before it is published.
/// </summary>
/// <remarks>
/// <para>
/// Counterpart to <see cref="StartDialogCommand"/>, which deliberately starts only the <b>published</b>
/// version of a business key. The rest of the runtime is not affected by the distinction:
/// the session pins the <see cref="DialogSession.DialogId"/>, and resume/submit/edit load their
/// dialog version via <see cref="IDialogStore.GetDialogAsync"/> – likewise regardless of the
/// publication status.
/// </para>
/// <para>
/// If a running (<see cref="SessionStatus.InProgress"/>) session of <b>this</b> dialog version already
/// exists for this user, it is resumed (resume) instead of creating a new one – identical to
/// <see cref="StartDialogCommand"/>.
/// </para>
/// </remarks>
/// <param name="DialogId">The primary key of the dialog version to start.</param>
/// <param name="ExternalUserKey">The business user key of the host app (e.g. user id).</param>
public sealed record StartDialogVersionCommand(
    [property: Required] Guid DialogId,
    [property: Required] string ExternalUserKey) : ICommand<StartDialogResult>;

/// <summary>
/// Handler for <see cref="StartDialogVersionCommand"/>: loads the given dialog version, decides
/// between resume and fresh start and returns the currently open question.
/// </summary>
internal sealed class StartDialogVersionCommandHandler
    : ICommandHandler<StartDialogVersionCommand, StartDialogResult>
{
    private readonly IDialogStore _store;
    private readonly IPublisher _publisher;

    /// <summary>
    /// Creates the handler over the given <see cref="IDialogStore"/> and <see cref="IPublisher"/>.
    /// </summary>
    /// <param name="store">The repository for dialogs and sessions.</param>
    /// <param name="publisher">The Mediator publisher for the in-process trigger notifications.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="store"/> or <paramref name="publisher"/> is <see langword="null"/>.
    /// </exception>
    public StartDialogVersionCommandHandler(IDialogStore store, IPublisher publisher)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(publisher);
        _store = store;
        _publisher = publisher;
    }

    /// <inheritdoc />
    /// <exception cref="ConfigurationNotFoundException">
    /// No dialog version with the given id exists.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The dialog has no entry question, or the current question cannot be resolved
    /// (misconfiguration).
    /// </exception>
    public async ValueTask<StartDialogResult> Handle(
        StartDialogVersionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Deliberately GetDialogAsync (not GetPublishedDialogAsync): the publication status is ignored
        // here, which is exactly how the command differs from StartDialogCommand.
        var dialog = await _store.GetDialogAsync(command.DialogId, cancellationToken)
            ?? throw ConfigurationNotFoundException.ForDialog(command.DialogId);

        var existing = await _store.FindActiveSessionAsync(
            dialog.Id, command.ExternalUserKey, cancellationToken);
        if (existing is not null)
        {
            return new StartDialogResult(
                existing.Id, IsResumed: true,
                QuestionProjection.ResolveQuestion(dialog, existing.CurrentQuestionId));
        }

        if (dialog.StartQuestionId is null)
        {
            throw new InvalidOperationException(
                $"The dialog '{dialog.Key}' has no entry question (StartQuestionId).");
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

        // In-process trigger (EPIC 4): as with StartDialogCommand, only the genuine fresh start reports
        // DialogStarted; a resume does not.
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
            QuestionProjection.ResolveQuestion(dialog, session.CurrentQuestionId));
    }
}
