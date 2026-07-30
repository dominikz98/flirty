namespace Flirty.Runtime;

/// <summary>
/// Public facade over the dialog runtime of the Flirty engine. Encapsulates the sending of the
/// Mediator commands so that host apps can use the engine conveniently without using
/// <see cref="Mediator.ISender"/> themselves. Whoever needs the full pipeline (incl. their own
/// behaviors/notifications) can still send the commands directly via
/// <see cref="Mediator.ISender"/>.
/// </summary>
public interface IFlirtyEngine
{
    /// <summary>
    /// Starts the published dialog with the given key for the user, or resumes an already running
    /// session (resume), and returns the currently open question.
    /// </summary>
    /// <param name="dialogKey">The business, stable key of the dialog to start.</param>
    /// <param name="externalUserKey">The business user key of the host app (e.g. user id).</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The (new or resumed) session along with the current question.</returns>
    /// <exception cref="DialogNotFoundException">
    /// No published dialog with the given key exists.
    /// </exception>
    Task<StartDialogResult> StartDialogAsync(
        string dialogKey, string externalUserKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts the <b>concrete dialog version</b> with the given id for the user – <b>regardless
    /// of the publication status</b> – or resumes an already running session of this version
    /// (resume) and returns the currently open question. Intended for preview/test scenarios in which a
    /// draft is to be played through before it is published (designer test runner, #43).
    /// </summary>
    /// <remarks>
    /// For the productive start <see cref="StartDialogAsync"/> is intended: it resolves via the business
    /// key and starts published dialogs only.
    /// </remarks>
    /// <param name="dialogId">The primary key of the dialog version to start.</param>
    /// <param name="externalUserKey">The business user key of the host app (e.g. user id).</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The (new or resumed) session along with the current question.</returns>
    /// <exception cref="ConfigurationNotFoundException">
    /// No dialog version with the given <paramref name="dialogId"/> exists.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The dialog has no entry question, or the current question cannot be resolved.
    /// </exception>
    Task<StartDialogResult> StartDialogVersionAsync(
        Guid dialogId, string externalUserKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Submits an answer to the currently open question of a running session: persists the
    /// answer, evaluates the transitions (branching) and returns the next question or signals the
    /// completion of the dialog.
    /// </summary>
    /// <param name="sessionId">The primary key of the running session.</param>
    /// <param name="questionId">
    /// The id of the question to be answered; must correspond to the currently open question of the session.
    /// </param>
    /// <param name="value">The submitted answer value as raw JSON text (format depends on the question type).</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The result with the next question or the completion signal.</returns>
    /// <exception cref="SessionNotFoundException">
    /// No session with the given <paramref name="sessionId"/> exists.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The session is no longer open, the given question is not the currently open one, or the
    /// branching is misconfigured.
    /// </exception>
    Task<SubmitAnswerResult> SubmitAnswerAsync(
        Guid sessionId, Guid questionId, string value, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the current state of a session – status, the (possibly) currently open question and the answers
    /// given so far – purely reading, in order to restore a survey e.g. after a reload of the host app.
    /// </summary>
    /// <param name="sessionId">The primary key of the session to query.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The state of the session along with the current question and the answers so far.</returns>
    /// <exception cref="SessionNotFoundException">
    /// No session with the given <paramref name="sessionId"/> exists.
    /// </exception>
    Task<ResumeDialogResult> ResumeDialogAsync(
        Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Edits the answer already given to an earlier question of a session: overwrites the value,
    /// discards (invalidates) all downstream answers and recomputes the path from the edited question
    /// onwards via the branching. An already completed session is reopened if the
    /// recomputation leads to a non-terminal follow-up question.
    /// </summary>
    /// <param name="sessionId">The primary key of the session whose answer is edited.</param>
    /// <param name="questionId">
    /// The id of the question whose answer is to be overwritten; must belong to the dialog and must already
    /// have been answered in this session (not necessarily the currently open question).
    /// </param>
    /// <param name="value">The new answer value as raw JSON text (format depends on the question type).</param>
    /// <param name="iterationIndex">
    /// Optional zero-based iteration index to edit, within a loop, the answer of a specific
    /// iteration; <see langword="null"/> edits the earliest answer of the question.
    /// </param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>
    /// The result with the newly computed follow-up question or the completion signal and the number of
    /// discarded downstream answers.
    /// </returns>
    /// <exception cref="SessionNotFoundException">
    /// No session with the given <paramref name="sessionId"/> exists.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The session is abandoned, the question does not belong to the dialog, the question (or the given
    /// iteration) has not yet been answered, or the branching is misconfigured.
    /// </exception>
    Task<EditAnswerResult> EditAnswerAsync(
        Guid sessionId, Guid questionId, string value, int? iterationIndex = null,
        CancellationToken cancellationToken = default);
}
