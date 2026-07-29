using Flirty.Domain;

namespace Flirty.Runtime;

/// <summary>
/// Projects the answers so far of a <see cref="DialogSession"/> into navigation-free
/// <see cref="SessionAnswerView"/> for the runtime API. Central reuse point for
/// <see cref="ResumeDialogQueryHandler"/> (read state) and the completion notification
/// (<see cref="DialogCompletedNotification"/>).
/// </summary>
internal static class SessionAnswerProjection
{
    /// <summary>
    /// Resolves, per answer, the business <see cref="Question.Key"/> from the pinned dialog version and
    /// orders ascending by <see cref="SessionAnswer.Sequence"/> (chronological order).
    /// </summary>
    /// <param name="dialog">The dialog version pinned by the session (provides the question keys).</param>
    /// <param name="session">The session whose answers are projected.</param>
    /// <returns>The projected answers in chronological order; empty if none were given.</returns>
    public static IReadOnlyList<SessionAnswerView> Project(Dialog dialog, DialogSession session)
    {
        var keyByQuestionId = dialog.Questions.ToDictionary(question => question.Id, question => question.Key);

        return session.Answers
            .OrderBy(answer => answer.Sequence)
            .Select(answer => new SessionAnswerView(
                answer.QuestionId,
                keyByQuestionId.GetValueOrDefault(answer.QuestionId, string.Empty),
                answer.Value,
                answer.Sequence,
                answer.AnsweredAt,
                answer.LoopInstanceId,
                answer.IterationIndex))
            .ToList();
    }
}
