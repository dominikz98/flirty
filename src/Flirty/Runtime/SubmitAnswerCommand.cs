using System.ComponentModel.DataAnnotations;
using Flirty.Domain;
using Flirty.Expressions;
using Flirty.Persistence;
using Flirty.Placeholders;
using Mediator;

namespace Flirty.Runtime;

/// <summary>
/// Submits the answer <see cref="Value"/> to the currently open question <see cref="QuestionId"/> of the
/// running session <see cref="SessionId"/>: the answer is persisted, then the
/// outgoing transitions of the question are evaluated and the session is advanced to the next question
/// or completed if no transition applies anymore.
/// </summary>
/// <param name="SessionId">The primary key of the running <see cref="DialogSession"/>.</param>
/// <param name="QuestionId">
/// The id of the question to be answered. It must correspond to the currently open question of the session
/// (<see cref="DialogSession.CurrentQuestionId"/>); editing earlier answers is
/// reserved for the <c>EditAnswerCommand</c> (#28).
/// </param>
/// <param name="Value">
/// The submitted answer value as raw JSON text (format depends on the question type, e.g. the
/// <see cref="AnswerOption.Value"/> of a choice).
/// </param>
public sealed record SubmitAnswerCommand(
    [property: Required] Guid SessionId,
    [property: Required] Guid QuestionId,
    [property: Required] string Value) : ICommand<SubmitAnswerResult>, IAnswerCommand;

/// <summary>
/// Handler for <see cref="SubmitAnswerCommand"/>: validates session and question, persists the answer,
/// evaluates the transitions (branching) via the <see cref="IExpressionEvaluator"/> and advances the
/// session or completes it.
/// </summary>
internal sealed class SubmitAnswerCommandHandler : ICommandHandler<SubmitAnswerCommand, SubmitAnswerResult>
{
    private readonly IDialogStore _store;
    private readonly IExpressionEvaluator _evaluator;
    private readonly IPublisher _publisher;
    private readonly PlaceholderRenderer _renderer;

    /// <summary>
    /// Creates the handler over the given <see cref="IDialogStore"/>,
    /// <see cref="IExpressionEvaluator"/>, <see cref="IPublisher"/> and <see cref="PlaceholderRenderer"/>.
    /// </summary>
    /// <param name="store">The repository for dialogs and sessions.</param>
    /// <param name="evaluator">The engine for evaluating the transition condition expressions.</param>
    /// <param name="publisher">The Mediator publisher for the in-process trigger notifications.</param>
    /// <param name="renderer">The renderer that fills message placeholders in the delivered question.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="store"/>, <paramref name="evaluator"/>, <paramref name="publisher"/> or
    /// <paramref name="renderer"/> is <see langword="null"/>.
    /// </exception>
    public SubmitAnswerCommandHandler(
        IDialogStore store, IExpressionEvaluator evaluator, IPublisher publisher, PlaceholderRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(evaluator);
        ArgumentNullException.ThrowIfNull(publisher);
        ArgumentNullException.ThrowIfNull(renderer);
        _store = store;
        _evaluator = evaluator;
        _publisher = publisher;
        _renderer = renderer;
    }

    /// <inheritdoc />
    /// <exception cref="SessionNotFoundException">
    /// No session with the given <see cref="SubmitAnswerCommand.SessionId"/> exists.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The session is no longer open (<see cref="SessionStatus.InProgress"/>), the given question
    /// is not the currently open one, the pinned dialog version is missing, or the branching is
    /// misconfigured (no matching transition and no default, or an unknown target question).
    /// </exception>
    public async ValueTask<SubmitAnswerResult> Handle(
        SubmitAnswerCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var session = await _store.GetSessionAsync(command.SessionId, cancellationToken)
            ?? throw SessionNotFoundException.ForId(command.SessionId);

        if (session.Status != SessionStatus.InProgress)
        {
            throw new InvalidOperationException(
                $"The session '{session.Id}' is not open (status: {session.Status}) and does not accept answers.");
        }

        if (command.QuestionId != session.CurrentQuestionId)
        {
            throw new InvalidOperationException(
                $"The question '{command.QuestionId}' is not the currently open question "
                + $"('{session.CurrentQuestionId}') of session '{session.Id}'.");
        }

        // Load the dialog version pinned by the session (regardless of the publication status).
        var dialog = await _store.GetDialogAsync(session.DialogId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"The dialog version '{session.DialogId}' pinned by session '{session.Id}' does not exist.");

        if (dialog.Questions.All(question => question.Id != command.QuestionId))
        {
            throw new InvalidOperationException(
                $"The question '{command.QuestionId}' does not belong to dialog '{dialog.Key}'.");
        }

        var answer = PersistAnswer(dialog, session, command);

        var target = new TransitionResolver(_evaluator).ResolveTransitionTarget(dialog, session, command.QuestionId);
        if (target is null)
        {
            Complete(session);
            await _store.SaveChangesAsync(cancellationToken);

            // In-process trigger (EPIC 4): first report the answer, then the transition result (completion),
            // finally the dialog completion along with the answers given.
            await PublishAnswerAsync(session, dialog, answer, cancellationToken);
            await _publisher.Publish(
                new QuestionAnsweredNotification(
                    session.Id, dialog.Key, command.QuestionId, NextQuestionId: null, IsCompleted: true),
                cancellationToken);
            await _publisher.Publish(
                new DialogCompletedNotification(
                    session.Id, dialog.Key, SessionAnswerProjection.Project(dialog, session)),
                cancellationToken);

            return new SubmitAnswerResult(session.Id, IsCompleted: true, NextQuestion: null);
        }

        session.CurrentQuestionId = target;
        await _store.SaveChangesAsync(cancellationToken);

        // In-process trigger (EPIC 4): report the answer, then the transition result with the follow-up question.
        await PublishAnswerAsync(session, dialog, answer, cancellationToken);
        await _publisher.Publish(
            new QuestionAnsweredNotification(
                session.Id, dialog.Key, command.QuestionId, NextQuestionId: target, IsCompleted: false),
            cancellationToken);

        return new SubmitAnswerResult(
            session.Id, IsCompleted: false,
            await _renderer.RenderAsync(dialog, session, target, cancellationToken));
    }

    /// <summary>
    /// Publishes the <see cref="AnswerSubmittedNotification"/> for the just-persisted answer
    /// (incl. any loop assignment).
    /// </summary>
    private ValueTask PublishAnswerAsync(
        DialogSession session, Dialog dialog, SessionAnswer answer, CancellationToken cancellationToken)
        => _publisher.Publish(
            new AnswerSubmittedNotification(
                session.Id,
                dialog.Key,
                answer.QuestionId,
                answer.Value,
                answer.LoopInstanceId,
                answer.IterationIndex),
            cancellationToken);

    /// <summary>
    /// Appends the submitted answer as a new <see cref="SessionAnswer"/> to the tracked session.
    /// The Guid key is deliberately not pre-populated (store-generated on save); the
    /// <see cref="SessionAnswer.Sequence"/> continues the order within the session. If the
    /// question lies within a loop range, <see cref="SessionAnswer.LoopInstanceId"/> and
    /// <see cref="SessionAnswer.IterationIndex"/> are additionally set via the <see cref="LoopResolver"/> (the
    /// assignment computes on the prior state and must therefore happen before appending); outside a
    /// loop both stay <see langword="null"/>.
    /// </summary>
    /// <returns>The newly appended <see cref="SessionAnswer"/> (among other things for the trigger notification).</returns>
    private static SessionAnswer PersistAnswer(Dialog dialog, DialogSession session, SubmitAnswerCommand command)
    {
        var nextSequence = session.Answers.Count == 0
            ? 0
            : session.Answers.Max(answer => answer.Sequence) + 1;

        var assignment = new LoopResolver(dialog).ResolveAssignment(session, command.QuestionId);

        var answer = new SessionAnswer
        {
            SessionId = session.Id,
            QuestionId = command.QuestionId,
            Value = command.Value,
            AnsweredAt = DateTimeOffset.UtcNow,
            Sequence = nextSequence,
            LoopInstanceId = assignment.LoopInstanceId,
            IterationIndex = assignment.IterationIndex,
        };

        session.Answers.Add(answer);
        return answer;
    }

    /// <summary>Completes the session: status, completion timestamp and clearing the open question.</summary>
    private static void Complete(DialogSession session)
    {
        session.Status = SessionStatus.Completed;
        session.CompletedAt = DateTimeOffset.UtcNow;
        session.CurrentQuestionId = null;
    }
}
