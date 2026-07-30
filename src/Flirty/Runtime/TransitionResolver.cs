using Flirty.Domain;
using Flirty.Expressions;

namespace Flirty.Runtime;

/// <summary>
/// Shared branching kernel of the dialog runtime: starting from an answered question, evaluates the
/// configured <see cref="Transition"/>s of a pinned dialog version and returns the next
/// question or the completion. Shared by <see cref="SubmitAnswerCommandHandler"/> (#26) and
/// <see cref="EditAnswerCommandHandler"/> (#28) so that the transition logic exists in only <b>one</b>
/// place.
/// </summary>
internal sealed class TransitionResolver
{
    private readonly IExpressionEvaluator _evaluator;

    /// <summary>Creates the resolver over the given expression engine.</summary>
    /// <param name="evaluator">The engine for evaluating the transition condition expressions.</param>
    /// <exception cref="ArgumentNullException"><paramref name="evaluator"/> is <see langword="null"/>.</exception>
    public TransitionResolver(IExpressionEvaluator evaluator)
    {
        ArgumentNullException.ThrowIfNull(evaluator);
        _evaluator = evaluator;
    }

    /// <summary>
    /// Evaluates the outgoing transitions of the question <paramref name="questionId"/> and returns the
    /// target question id of the applying transition. <see langword="null"/> is returned if the question
    /// has <b>no</b> outgoing transitions (regular completion). If transitions exist but neither
    /// a conditional transition nor a default applies, the dialog is rejected as misconfigured.
    /// </summary>
    /// <param name="dialog">The pinned dialog version along with transitions and questions.</param>
    /// <param name="session">The running session whose answers feed the expression context.</param>
    /// <param name="questionId">The id of the answered question whose transitions are evaluated.</param>
    /// <returns>The target question id of the applying transition, or <see langword="null"/> on completion.</returns>
    /// <exception cref="InvalidOperationException">
    /// Transitions exist, but none applies and there is no default, or the target question of the
    /// applying transition does not belong to the dialog graph.
    /// </exception>
    public Guid? ResolveTransitionTarget(Dialog dialog, DialogSession session, Guid questionId)
    {
        var outgoing = dialog.Transitions
            .Where(transition => transition.FromQuestionId == questionId)
            .OrderBy(transition => transition.Priority)
            .ToList();

        if (outgoing.Count == 0)
        {
            return null;
        }

        var context = SessionExpressionContextBuilder.Build(dialog, session, questionId);
        var match = outgoing.FirstOrDefault(transition => !transition.IsDefault && ConditionHolds(transition, context))
            ?? outgoing.FirstOrDefault(transition => transition.IsDefault);

        if (match is null)
        {
            throw new InvalidOperationException(
                $"For the question '{questionId}' in dialog '{dialog.Key}' no transition applies and "
                + "no default transition is configured.");
        }

        if (dialog.Questions.All(question => question.Id != match.TargetQuestionId))
        {
            throw new InvalidOperationException(
                $"The transition '{match.Id}' in dialog '{dialog.Key}' points to the unknown target question "
                + $"'{match.TargetQuestionId}'.");
        }

        return match.TargetQuestionId;
    }

    /// <summary>
    /// Checks whether the transition applies: a <see langword="null"/>/empty expression counts as
    /// unconditionally applying (the short-circuit rests with the runtime); otherwise the
    /// <see cref="IExpressionEvaluator"/> decides.
    /// </summary>
    private bool ConditionHolds(Transition transition, ExpressionContext context)
        => string.IsNullOrWhiteSpace(transition.Expression)
            || _evaluator.Evaluate(transition.Expression, context);
}
