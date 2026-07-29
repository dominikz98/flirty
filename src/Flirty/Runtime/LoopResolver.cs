using Flirty.Domain;
using Flirty.Expressions;

namespace Flirty.Runtime;

/// <summary>
/// Encapsulates the entire loop runtime logic of a pinned dialog version (issue #29): the computation
/// of the loop range (body) per <see cref="LoopDefinition"/>, the assignment of
/// <see cref="SessionAnswer.LoopInstanceId"/>/<see cref="SessionAnswer.IterationIndex"/> when
/// persisting an answer, as well as the build-up of the collections gathered per iteration and the
/// current iteration index for the <see cref="ExpressionContext"/>.
/// </summary>
/// <remarks>
/// Loops arise exclusively via the existing branching (a <see cref="Transition"/> points to
/// an earlier question = cycle); the <see cref="LoopDefinition"/> is only the marker layer on top. There is
/// deliberately no separate runtime special path (cf. <c>docs/ARCHITECTURE.md</c> §10/§11.5). The body is
/// precomputed once in the constructor from the transition graph; the remaining operations derive their
/// state from the existing <see cref="SessionAnswer"/> rows (no additional session field).
/// </remarks>
internal sealed class LoopResolver
{
    private readonly List<LoopScope> _loops;
    private readonly Dictionary<Guid, LoopDefinition> _loopByQuestion;

    /// <summary>
    /// Creates the resolver for the given pinned dialog version and precomputes the body of each
    /// loop from its transitions.
    /// </summary>
    /// <param name="dialog">The pinned dialog version along with <see cref="Dialog.Loops"/> and <see cref="Dialog.Transitions"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="dialog"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// Two loop ranges overlap (nested/overlapping loops are not supported in the MVP).
    /// </exception>
    public LoopResolver(Dialog dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);

        _loops = [.. dialog.Loops.Select(loop => new LoopScope(loop, ComputeBody(dialog, loop)))];
        _loopByQuestion = new Dictionary<Guid, LoopDefinition>();

        foreach (var scope in _loops)
        {
            foreach (var questionId in scope.Body)
            {
                if (!_loopByQuestion.TryAdd(questionId, scope.Loop))
                {
                    throw new InvalidOperationException(
                        $"The question '{questionId}' in dialog '{dialog.Key}' belongs to multiple "
                        + "loop ranges; nested or overlapping loops are not supported.");
                }
            }
        }
    }

    /// <summary>
    /// Determines the loop assignment for an answer to <paramref name="questionId"/> that is
    /// <b>about to be persisted</b>. Must be called before appending the new answer (computes
    /// on the prior state of the already stored answers).
    /// </summary>
    /// <param name="session">The tracked session including its answers so far.</param>
    /// <param name="questionId">The id of the question whose answer is about to be persisted.</param>
    /// <returns>
    /// The <see cref="LoopAssignment"/> to set. Outside any loop both values are
    /// <see langword="null"/> (unchanged non-loop behavior).
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> is <see langword="null"/>.</exception>
    public LoopAssignment ResolveAssignment(DialogSession session, Guid questionId)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (!_loopByQuestion.TryGetValue(questionId, out var loop))
        {
            return default;
        }

        var body = BodyOf(loop);
        var priorBodyAnswers = session.Answers
            .Where(answer => answer.LoopInstanceId is not null && body.Contains(answer.QuestionId))
            .OrderBy(answer => answer.Sequence)
            .ToList();

        // First entry into the loop: fresh instance, iteration 0.
        if (priorBodyAnswers.Count == 0)
        {
            return new LoopAssignment(Guid.NewGuid(), 0);
        }

        var instanceId = priorBodyAnswers[^1].LoopInstanceId!.Value;
        var instanceAnswers = priorBodyAnswers.Where(answer => answer.LoopInstanceId == instanceId).ToList();
        var maxIteration = instanceAnswers.Max(answer => answer.IterationIndex ?? 0);

        // Loop-back: if the entry question is answered again in the running iteration, the
        // next iteration begins. All other (follow-up) questions stay in the current iteration.
        var startsNextIteration = questionId == loop.EntryQuestionId
            && instanceAnswers.Any(answer =>
                answer.QuestionId == loop.EntryQuestionId && answer.IterationIndex == maxIteration);

        return new LoopAssignment(instanceId, startsNextIteration ? maxIteration + 1 : maxIteration);
    }

    /// <summary>
    /// Builds the loop collections gathered per iteration for the <see cref="ExpressionContext"/>: per
    /// <see cref="LoopDefinition.CollectionKey"/> the <see cref="SessionAnswer.Value"/> of the entry question
    /// per iteration of the most recent loop instance, ordered by <see cref="SessionAnswer.IterationIndex"/>.
    /// Each <see cref="LoopDefinition.CollectionKey"/> is always bound (empty list as long as the
    /// loop has not yet been entered), so that expressions like <c>positions.Count &gt; 0</c> are evaluable
    /// even before the first iteration.
    /// </summary>
    /// <param name="session">The session including its answers so far.</param>
    /// <returns>The collections, indexed by <see cref="LoopDefinition.CollectionKey"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> is <see langword="null"/>.</exception>
    public IReadOnlyDictionary<string, IReadOnlyList<string?>> BuildCollections(DialogSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var result = new Dictionary<string, IReadOnlyList<string?>>(StringComparer.Ordinal);

        foreach (var scope in _loops)
        {
            var bodyAnswers = session.Answers
                .Where(answer => answer.LoopInstanceId is not null && scope.Body.Contains(answer.QuestionId))
                .ToList();

            IReadOnlyList<string?> entries = [];
            if (bodyAnswers.Count > 0)
            {
                var instanceId = bodyAnswers.OrderByDescending(answer => answer.Sequence).First().LoopInstanceId!.Value;
                entries = bodyAnswers
                    .Where(answer => answer.LoopInstanceId == instanceId
                        && answer.QuestionId == scope.Loop.EntryQuestionId)
                    .OrderBy(answer => answer.IterationIndex ?? 0)
                    .Select(answer => (string?)answer.Value)
                    .ToList();
            }

            result[scope.Loop.CollectionKey] = entries;
        }

        return result;
    }

    /// <summary>
    /// Determines the iteration index of the most recently given answer to <paramref name="questionId"/>,
    /// provided the question lies within a loop range; otherwise <see langword="null"/>.
    /// </summary>
    /// <param name="session">The session including its answers so far.</param>
    /// <param name="questionId">The id of the question just answered.</param>
    /// <returns>The current zero-based iteration index, or <see langword="null"/> outside a loop.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> is <see langword="null"/>.</exception>
    public int? ResolveIterationIndex(DialogSession session, Guid questionId)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (!_loopByQuestion.ContainsKey(questionId))
        {
            return null;
        }

        return session.Answers
            .Where(answer => answer.QuestionId == questionId && answer.IterationIndex is not null)
            .OrderByDescending(answer => answer.Sequence)
            .FirstOrDefault()?.IterationIndex;
    }

    private IReadOnlySet<Guid> BodyOf(LoopDefinition loop)
        => _loops.First(scope => ReferenceEquals(scope.Loop, loop)).Body;

    /// <summary>
    /// Computes the loop range as <c>(reachable forward from Entry) ∩ (reachable backward to
    /// Breaking) ∪ {Entry, Breaking}</c>. The forward search stops at the breaking question (its
    /// loop-back/exit edges are not followed); this keeps branches that exit the cycle early
    /// (in F, not in B) and questions upstream of the cycle (in B, not in F) outside the body.
    /// </summary>
    private static HashSet<Guid> ComputeBody(Dialog dialog, LoopDefinition loop)
    {
        var forward = ReachableForward(dialog, loop.EntryQuestionId, stopAt: loop.BreakingQuestionId);
        var backward = ReachableBackward(dialog, loop.BreakingQuestionId);

        var body = new HashSet<Guid>();
        foreach (var questionId in forward)
        {
            if (backward.Contains(questionId))
            {
                body.Add(questionId);
            }
        }

        body.Add(loop.EntryQuestionId);
        body.Add(loop.BreakingQuestionId);
        return body;
    }

    /// <summary>Questions reachable forward via outgoing transitions from <paramref name="start"/>; does not expand <paramref name="stopAt"/>.</summary>
    private static HashSet<Guid> ReachableForward(Dialog dialog, Guid start, Guid stopAt)
    {
        var visited = new HashSet<Guid>();
        var stack = new Stack<Guid>();
        stack.Push(start);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!visited.Add(current) || current == stopAt)
            {
                continue;
            }

            foreach (var transition in dialog.Transitions.Where(t => t.FromQuestionId == current))
            {
                stack.Push(transition.TargetQuestionId);
            }
        }

        return visited;
    }

    /// <summary>Questions from which <paramref name="target"/> is reachable backward via transitions (incl. <paramref name="target"/>).</summary>
    private static HashSet<Guid> ReachableBackward(Dialog dialog, Guid target)
    {
        var visited = new HashSet<Guid>();
        var stack = new Stack<Guid>();
        stack.Push(target);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!visited.Add(current))
            {
                continue;
            }

            foreach (var transition in dialog.Transitions.Where(t => t.TargetQuestionId == current))
            {
                stack.Push(transition.FromQuestionId);
            }
        }

        return visited;
    }

    /// <summary>Links a <see cref="LoopDefinition"/> with its precomputed loop range (question ids).</summary>
    private sealed record LoopScope(LoopDefinition Loop, HashSet<Guid> Body);
}

/// <summary>
/// The loop assignment to set when persisting an answer: the instance id of the loop and the
/// zero-based iteration index. Both are <see langword="null"/> if the answer is given outside any
/// loop.
/// </summary>
/// <param name="LoopInstanceId">The instance id of the running loop, or <see langword="null"/> outside.</param>
/// <param name="IterationIndex">The zero-based iteration index, or <see langword="null"/> outside.</param>
internal readonly record struct LoopAssignment(Guid? LoopInstanceId, int? IterationIndex);
