using Flirty.Domain;

namespace Flirty.Runtime;

/// <summary>
/// Projiziert die bisherigen Antworten einer <see cref="DialogSession"/> in navigationsfreie
/// <see cref="SessionAnswerView"/> für die Laufzeit-API. Zentraler Wiederverwendungspunkt für
/// <see cref="ResumeDialogQueryHandler"/> (Lese-Zustand) und die Abschluss-Notification
/// (<see cref="DialogCompletedNotification"/>).
/// </summary>
internal static class SessionAnswerProjection
{
    /// <summary>
    /// Löst je Antwort den fachlichen <see cref="Question.Key"/> aus der gepinnten Dialogversion auf und
    /// ordnet aufsteigend nach <see cref="SessionAnswer.Sequence"/> (chronologische Reihenfolge).
    /// </summary>
    /// <param name="dialog">Die von der Session gepinnte Dialogversion (liefert die Frage-Schlüssel).</param>
    /// <param name="session">Die Session, deren Antworten projiziert werden.</param>
    /// <returns>Die projizierten Antworten in chronologischer Reihenfolge; leer, wenn keine gegeben wurden.</returns>
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
