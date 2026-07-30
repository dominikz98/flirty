using Flirty.Domain;
using Flirty.Expressions;

namespace Flirty.Runtime;

/// <summary>
/// Builds the <see cref="ExpressionContext"/> of a running <see cref="DialogSession"/> from its answers
/// so far. Shared source for the branching kernel (<see cref="TransitionResolver"/>, #26/#28) and
/// the outbound webhook delivery (<c>WebhookNotificationHandler</c>, #33), so that the context building
/// (answers by <see cref="Question.Key"/>, loop collections, iteration index) exists in only <b>one</b> place.
/// </summary>
internal static class SessionExpressionContextBuilder
{
    /// <summary>
    /// Builds the evaluation context: per question the most recently given answer (highest
    /// <see cref="SessionAnswer.Sequence"/>) is mapped onto the business <see cref="Question.Key"/>
    /// (within a loop therefore the answer of the current iteration). In addition, the loop collections
    /// gathered per iteration and – if <paramref name="currentQuestionId"/> is given – the
    /// iteration index of that question are populated via the <see cref="LoopResolver"/>.
    /// </summary>
    /// <param name="dialog">The pinned dialog version along with questions, transitions and loops.</param>
    /// <param name="session">The session whose answers feed the context.</param>
    /// <param name="currentQuestionId">
    /// The question whose iteration index is determined (the just-answered or current question), or
    /// <see langword="null"/> if there is no question reference (e.g. at dialog completion) – then the
    /// iteration index is <see langword="null"/>.
    /// </param>
    /// <returns>The built, immutable <see cref="ExpressionContext"/>.</returns>
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
