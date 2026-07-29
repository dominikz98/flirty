using System.ComponentModel.DataAnnotations;
using Flirty.Domain;
using Flirty.Persistence;
using Mediator;

namespace Flirty.Runtime;

/// <summary>
/// Reads the current state of session <see cref="SessionId"/>: its status, the currently open
/// question (as long as the session is still running) and the answers given so far. Purely reading – the
/// session is not modified. The <b>resume-or-new</b> of a session per user, by contrast, is reserved for
/// the <see cref="StartDialogCommand"/>.
/// </summary>
/// <param name="SessionId">The primary key of the <see cref="DialogSession"/> to query.</param>
public sealed record ResumeDialogQuery(
    [property: Required] Guid SessionId) : IQuery<ResumeDialogResult>;

/// <summary>
/// Handler for <see cref="ResumeDialogQuery"/>: loads the session along with its answers and the dialog
/// version pinned by it, projects the currently open question and the answers so far into navigation-free
/// views and returns the composed <see cref="ResumeDialogResult"/>.
/// </summary>
internal sealed class ResumeDialogQueryHandler : IQueryHandler<ResumeDialogQuery, ResumeDialogResult>
{
    private readonly IDialogStore _store;

    /// <summary>Creates the handler over the given <see cref="IDialogStore"/>.</summary>
    /// <param name="store">The repository for dialogs and sessions.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is <see langword="null"/>.</exception>
    public ResumeDialogQueryHandler(IDialogStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    /// <exception cref="SessionNotFoundException">
    /// No session with the given <see cref="ResumeDialogQuery.SessionId"/> exists.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The dialog version pinned by the session no longer exists, or the currently open question
    /// does not belong to the dialog graph (misconfiguration).
    /// </exception>
    public async ValueTask<ResumeDialogResult> Handle(
        ResumeDialogQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var session = await _store.GetSessionAsync(query.SessionId, cancellationToken)
            ?? throw SessionNotFoundException.ForId(query.SessionId);

        // Load the dialog version pinned by the session (regardless of the publication status) –
        // it provides the business question keys and the graph for the question projection.
        var dialog = await _store.GetDialogAsync(session.DialogId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"The dialog version '{session.DialogId}' pinned by session '{session.Id}' does not exist.");

        var answers = SessionAnswerProjection.Project(dialog, session);

        var currentQuestion = session.CurrentQuestionId is Guid questionId
            ? QuestionProjection.ResolveQuestion(dialog, questionId)
            : null;

        return new ResumeDialogResult(session.Id, session.Status, currentQuestion, answers);
    }
}
