using System.ComponentModel.DataAnnotations;
using Flirty.Domain;
using Flirty.Expressions;
using Flirty.Persistence;
using Mediator;

namespace Flirty.Runtime;

/// <summary>
/// Edits the answer already given to an earlier question <see cref="QuestionId"/> of session
/// <see cref="SessionId"/>: the existing answer value is <b>overwritten</b> by <see cref="Value"/>,
/// all <b>downstream</b> answers (those given after the edited question) are discarded
/// (invalidated) and the path is <b>recomputed</b> from the edited question onwards via the transitions
/// (branching). An already completed session is reopened if the recomputation leads to a non-terminal
/// follow-up question.
/// </summary>
/// <param name="SessionId">The primary key of the <see cref="DialogSession"/> whose answer is edited.</param>
/// <param name="QuestionId">
/// The id of the question whose answer is to be overwritten. It must belong to the pinned dialog and
/// must already have been answered in this session (unlike <see cref="SubmitAnswerCommand"/>
/// it does <b>not</b> have to be the currently open question).
/// </param>
/// <param name="Value">
/// The new answer value as raw JSON text (format depends on the question type, e.g. the
/// <see cref="AnswerOption.Value"/> of a choice).
/// </param>
/// <param name="IterationIndex">
/// Optional zero-based iteration index to edit, within a loop, the answer of a specific iteration
/// (a question can carry one answer per iteration). If it stays
/// <see langword="null"/>, the earliest answer of the question is edited – as outside loops
/// (iteration 0 for a loop question).
/// </param>
public sealed record EditAnswerCommand(
    [property: Required] Guid SessionId,
    [property: Required] Guid QuestionId,
    [property: Required] string Value,
    int? IterationIndex = null) : ICommand<EditAnswerResult>, IAnswerCommand;

/// <summary>
/// Handler for <see cref="EditAnswerCommand"/>: overwrites the existing answer, invalidates the
/// downstream answers and recomputes the path from the edited question onwards via the
/// <see cref="TransitionResolver"/> (advancing, completion or reopening of the session).
/// </summary>
internal sealed class EditAnswerCommandHandler : ICommandHandler<EditAnswerCommand, EditAnswerResult>
{
    private readonly IDialogStore _store;
    private readonly IExpressionEvaluator _evaluator;
    private readonly IPublisher _publisher;

    /// <summary>
    /// Creates the handler over the given <see cref="IDialogStore"/>,
    /// <see cref="IExpressionEvaluator"/> and <see cref="IPublisher"/>.
    /// </summary>
    /// <param name="store">The repository for dialogs and sessions.</param>
    /// <param name="evaluator">The engine for evaluating the transition condition expressions.</param>
    /// <param name="publisher">The Mediator publisher for the in-process trigger notifications.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="store"/>, <paramref name="evaluator"/> or <paramref name="publisher"/> is
    /// <see langword="null"/>.
    /// </exception>
    public EditAnswerCommandHandler(IDialogStore store, IExpressionEvaluator evaluator, IPublisher publisher)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(evaluator);
        ArgumentNullException.ThrowIfNull(publisher);
        _store = store;
        _evaluator = evaluator;
        _publisher = publisher;
    }

    /// <inheritdoc />
    /// <exception cref="SessionNotFoundException">
    /// No session with the given <see cref="EditAnswerCommand.SessionId"/> exists.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The session is abandoned (<see cref="SessionStatus.Abandoned"/>), the pinned dialog version
    /// is missing, the question does not belong to the dialog, the question (or the given iteration) has not
    /// yet been answered in this session, or the branching is misconfigured (no matching transition and
    /// no default, or an unknown target question).
    /// </exception>
    public async ValueTask<EditAnswerResult> Handle(
        EditAnswerCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var session = await _store.GetSessionAsync(command.SessionId, cancellationToken)
            ?? throw SessionNotFoundException.ForId(command.SessionId);

        // Editing is allowed for running and completed sessions (subsequent correction),
        // but not for abandoned ones.
        if (session.Status == SessionStatus.Abandoned)
        {
            throw new InvalidOperationException(
                $"The session '{session.Id}' is abandoned (status: {session.Status}) and cannot be edited.");
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

        // Find the answer to the question that is to be edited; without an existing answer there is nothing
        // to edit. Within a loop the optional IterationIndex selects the iteration specifically,
        // otherwise the earliest answer of the question applies as before.
        var candidates = session.Answers.Where(answer => answer.QuestionId == command.QuestionId);
        var target = (command.IterationIndex is int iteration
                ? candidates.FirstOrDefault(answer => answer.IterationIndex == iteration)
                : candidates.OrderBy(answer => answer.Sequence).FirstOrDefault())
            ?? throw new InvalidOperationException(
                $"The question '{command.QuestionId}' has not yet been answered in session '{session.Id}'"
                + (command.IterationIndex is int it ? $" in iteration {it}" : string.Empty)
                + " and therefore cannot be edited.");

        // Overwrite the answer (Sequence is preserved, the timestamp reflects the edit).
        target.Value = command.Value;
        target.AnsweredAt = DateTimeOffset.UtcNow;

        var invalidatedCount = InvalidateDownstream(session, target.Sequence);

        var next = new TransitionResolver(_evaluator).ResolveTransitionTarget(dialog, session, command.QuestionId);
        if (next is null)
        {
            Complete(session);
            await _store.SaveChangesAsync(cancellationToken);

            // In-process trigger (EPIC 4): if the recomputation completes the session, DialogCompleted
            // is reported. A mere reopen deliberately triggers no notification.
            await _publisher.Publish(
                new DialogCompletedNotification(
                    session.Id, dialog.Key, SessionAnswerProjection.Project(dialog, session)),
                cancellationToken);

            return new EditAnswerResult(session.Id, IsCompleted: true, NextQuestion: null, invalidatedCount);
        }

        Reopen(session, next.Value);
        await _store.SaveChangesAsync(cancellationToken);
        return new EditAnswerResult(
            session.Id, IsCompleted: false, QuestionProjection.ResolveQuestion(dialog, next.Value), invalidatedCount);
    }

    /// <summary>
    /// Discards all downstream answers of the session – those with a <see cref="SessionAnswer.Sequence"/>
    /// above the edited answer – from the tracked answer graph. Removing them from the
    /// collection deletes the rows on <see cref="IDialogStore.SaveChangesAsync"/> (cascade/orphan delete)
    /// and at the same time keeps the in-memory context consistent for the subsequent path recomputation.
    /// </summary>
    /// <param name="session">The tracked session.</param>
    /// <param name="editedSequence">The <see cref="SessionAnswer.Sequence"/> of the edited answer.</param>
    /// <returns>The number of discarded downstream answers.</returns>
    private static int InvalidateDownstream(DialogSession session, int editedSequence)
    {
        var downstream = session.Answers
            .Where(answer => answer.Sequence > editedSequence)
            .ToList();

        foreach (var answer in downstream)
        {
            session.Answers.Remove(answer);
        }

        return downstream.Count;
    }

    /// <summary>
    /// (Re)opens the session for the newly computed follow-up question: sets it to
    /// <see cref="SessionStatus.InProgress"/>, clears any completion timestamp and re-aligns the
    /// currently open question. For a running session this acts as a plain repositioning of the question and
    /// reopens a previously completed session.
    /// </summary>
    private static void Reopen(DialogSession session, Guid currentQuestionId)
    {
        session.Status = SessionStatus.InProgress;
        session.CompletedAt = null;
        session.CurrentQuestionId = currentQuestionId;
    }

    /// <summary>Completes the session: status, completion timestamp and clearing the open question.</summary>
    private static void Complete(DialogSession session)
    {
        session.Status = SessionStatus.Completed;
        session.CompletedAt = DateTimeOffset.UtcNow;
        session.CurrentQuestionId = null;
    }
}
