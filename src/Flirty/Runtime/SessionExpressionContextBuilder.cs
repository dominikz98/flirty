using Flirty.Domain;
using Flirty.Expressions;

namespace Flirty.Runtime;

/// <summary>
/// Baut den <see cref="ExpressionContext"/> einer laufenden <see cref="DialogSession"/> aus deren bisherigen
/// Antworten auf. Gemeinsame Quelle für den Branching-Kernel (<see cref="TransitionResolver"/>, #26/#28) und
/// die Outbound-Webhook-Auslieferung (<c>WebhookNotificationHandler</c>, #33), damit die Kontext-Bildung
/// (Antworten nach <see cref="Question.Key"/>, Loop-Collections, Iterationsindex) nur an <b>einer</b> Stelle
/// existiert.
/// </summary>
internal static class SessionExpressionContextBuilder
{
    /// <summary>
    /// Baut den Auswertungskontext: je Frage wird die zuletzt gegebene Antwort (höchste
    /// <see cref="SessionAnswer.Sequence"/>) auf den fachlichen <see cref="Question.Key"/> abgebildet
    /// (innerhalb einer Schleife also die Antwort der aktuellen Iteration). Zusätzlich werden die je Iteration
    /// gesammelten Loop-Collections und – sofern <paramref name="currentQuestionId"/> angegeben ist – der
    /// Iterationsindex dieser Frage über den <see cref="LoopResolver"/> befüllt.
    /// </summary>
    /// <param name="dialog">Die gepinnte Dialogversion samt Fragen, Übergängen und Schleifen.</param>
    /// <param name="session">Die Session, deren Antworten den Kontext speisen.</param>
    /// <param name="currentQuestionId">
    /// Die Frage, deren Iterationsindex ermittelt wird (die gerade beantwortete bzw. aktuelle Frage), oder
    /// <see langword="null"/>, wenn kein Frage-Bezug besteht (z. B. beim Dialog-Abschluss) – dann ist der
    /// Iterationsindex <see langword="null"/>.
    /// </param>
    /// <returns>Der aufgebaute, unveränderliche <see cref="ExpressionContext"/>.</returns>
    public static ExpressionContext Build(Dialog dialog, DialogSession session, Guid? currentQuestionId)
    {
        var keyByQuestionId = dialog.Questions.ToDictionary(question => question.Id, question => question.Key);

        var answers = session.Answers
            .Where(answer => keyByQuestionId.ContainsKey(answer.QuestionId))
            .GroupBy(answer => answer.QuestionId)
            .ToDictionary(
                group => keyByQuestionId[group.Key],
                group => (string?)group.OrderByDescending(answer => answer.Sequence).First().Value);

        var loops = new LoopResolver(dialog);
        return new ExpressionContext(
            session,
            DateTimeOffset.UtcNow,
            answers,
            loops.BuildCollections(session),
            currentQuestionId is { } questionId ? loops.ResolveIterationIndex(session, questionId) : null);
    }
}
