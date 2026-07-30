using Flirty.Designer.Models;
using Flirty.Runtime.Admin;

namespace Flirty.Designer.Services;

/// <summary>
/// Evaluates the loop markers of a dialog for the loop editor (#41): the loop range (body), the
/// back-jump / exit transitions and the warnings about configurations that behave at runtime differently
/// than intended – above all the <b>cycle without a reachable exit</b> (infinite loop).
/// </summary>
/// <remarks>
/// <para>
/// The body computation deliberately mirrors the core-internal <c>LoopResolver</c>
/// (<c>src/Flirty/Runtime/LoopResolver.cs</c>): <c>(forward from entry, stop at breaking) ∩ (backward to
/// breaking) ∪ {entry, breaking}</c>. The resolver itself is not reusable – it is <c>internal</c> in the
/// core and works on a <c>Dialog</c> entity with loaded navigations, while the designer only has the
/// navigation-free view <see cref="DialogDetail"/>. Same boundary as
/// <see cref="DesignerExpressionContext"/> ↔ <c>SessionExpressionContextBuilder</c>; a test in
/// <c>tests/Flirty.Tests/Designer/LoopAnalyzerTests</c> guards against divergence by comparing both
/// implementations on the same graph.
/// </para>
/// <para>
/// The reachability of the exit mirrors the <c>TransitionResolver</c>: the first <b>non</b>-default whose
/// condition matches wins (an empty expression always matches), otherwise the first default. An
/// unconditional back-jump before every exit therefore makes the exit unreachable.
/// </para>
/// </remarks>
internal static class LoopAnalyzer
{
    /// <summary>Analyzes all loop markers of the dialog.</summary>
    /// <param name="detail">The dialog including its graph (from <c>GetDialogQuery</c>).</param>
    /// <returns>One analysis result per marker, in the order of <see cref="DialogDetail.Loops"/>.</returns>
    public static IReadOnlyList<LoopInsight> Analyze(DialogDetail detail)
    {
        ArgumentNullException.ThrowIfNull(detail);

        var bodies = detail.Loops.ToDictionary(loop => loop.Id, loop => ComputeBody(detail, loop));

        return [.. detail.Loops.Select(loop => Describe(detail, loop, bodies))];
    }

    /// <summary>Analyzes a single loop marker of the dialog.</summary>
    /// <param name="detail">The dialog including its graph.</param>
    /// <param name="loopId">The primary key of the marker.</param>
    /// <returns>The analysis result, or <see langword="null"/> when the dialog does not contain the marker.</returns>
    public static LoopInsight? Analyze(DialogDetail detail, Guid loopId)
    {
        ArgumentNullException.ThrowIfNull(detail);

        return Analyze(detail).FirstOrDefault(insight => insight.Loop.Id == loopId);
    }

    /// <summary>
    /// Does the transition point to an earlier (or the same) question <b>in list order</b>? Then a cycle
    /// forms – that is, a loop as soon as a marker is added.
    /// </summary>
    /// <remarks>
    /// Deliberately over list order and not over the layering of the layout, so that the list view, the
    /// graph edge and the loop suggestion make the <b>same</b> statement. The overload with a prepared
    /// order exists for <see cref="DialogGraphBuilder"/>, which needs it per edge; the rule itself lives
    /// only here.
    /// </remarks>
    /// <param name="order">The question ids in list order.</param>
    /// <param name="transition">The transition to check.</param>
    /// <returns><see langword="true"/> when the transition jumps back.</returns>
    public static bool IsBackJump(IReadOnlyList<Guid> order, TransitionDetail transition)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(transition);

        var from = IndexOf(order, transition.FromQuestionId);
        var target = IndexOf(order, transition.TargetQuestionId);

        return from >= 0 && target >= 0 && target <= from;
    }

    /// <summary>Like <see cref="IsBackJump(IReadOnlyList{Guid}, TransitionDetail)"/>, but over the dialog.</summary>
    /// <param name="detail">The dialog including its graph.</param>
    /// <param name="transition">The transition to check.</param>
    /// <returns><see langword="true"/> when the transition jumps back.</returns>
    public static bool IsBackJump(DialogDetail detail, TransitionDetail transition)
    {
        ArgumentNullException.ThrowIfNull(detail);

        return IsBackJump([.. detail.Questions.Select(question => question.Id)], transition);
    }

    /// <summary>
    /// Back-jump transitions to which no loop marker (yet) fits. Without a marker the runtime does not
    /// collect the answers of the cycle but overwrites them – so they are reported instead of silently
    /// accepted.
    /// </summary>
    /// <remarks>
    /// Pulled out of the <c>DialogEditor</c>, because the canvas (#103) offers the suggestion <b>at the
    /// cycle</b> instead of in a list. Both views ask the same method – otherwise a back-jump could count
    /// as unmarked in the list and not on the graph.
    /// </remarks>
    /// <param name="detail">The dialog including its graph.</param>
    /// <returns>The unmarked back-jumps in dialog order.</returns>
    public static IReadOnlyList<TransitionDetail> UnmarkedBackJumps(DialogDetail detail)
    {
        ArgumentNullException.ThrowIfNull(detail);

        var order = detail.Questions.Select(question => question.Id).ToList();

        return
        [
            .. detail.Transitions.Where(transition =>
                IsBackJump(order, transition)
                && !detail.Loops.Any(loop => loop.EntryQuestionId == transition.TargetQuestionId
                                          && loop.BreakingQuestionId == transition.FromQuestionId)),
        ];
    }

    private static int IndexOf(IReadOnlyList<Guid> order, Guid id)
    {
        for (var index = 0; index < order.Count; index++)
        {
            if (order[index] == id)
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// Determines the loop range of a marker – identical to the precomputation in the core
    /// <c>LoopResolver</c>. If the entry or breaking question is missing from the dialog, the range is
    /// empty (the marker points into the void and is reported as a warning).
    /// </summary>
    /// <param name="detail">The dialog including its graph.</param>
    /// <param name="loop">The marker to measure.</param>
    /// <returns>The question ids of the loop range.</returns>
    public static HashSet<Guid> ComputeBody(DialogDetail detail, LoopDetail loop)
    {
        ArgumentNullException.ThrowIfNull(detail);
        ArgumentNullException.ThrowIfNull(loop);

        var known = detail.Questions.Select(question => question.Id).ToHashSet();
        if (!known.Contains(loop.EntryQuestionId) || !known.Contains(loop.BreakingQuestionId))
        {
            return [];
        }

        var forward = ReachableForward(detail, loop.EntryQuestionId, stopAt: loop.BreakingQuestionId);
        var backward = ReachableBackward(detail, loop.BreakingQuestionId);

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

    private static LoopInsight Describe(
        DialogDetail detail, LoopDetail loop, IReadOnlyDictionary<Guid, HashSet<Guid>> bodies)
    {
        var body = bodies[loop.Id];
        var entry = Question(detail, loop.EntryQuestionId);
        var breaking = Question(detail, loop.BreakingQuestionId);

        var outgoing = breaking is null
            ? []
            : detail.Transitions
                .Where(transition => transition.FromQuestionId == breaking.Id)
                .OrderBy(transition => transition.Priority)
                .ToList();

        var loopBacks = outgoing.Where(transition => body.Contains(transition.TargetQuestionId)).ToList();
        var exits = outgoing.Where(transition => !body.Contains(transition.TargetQuestionId)).ToList();

        return new LoopInsight(
            loop,
            [.. detail.Questions.Where(question => body.Contains(question.Id))],
            entry,
            breaking,
            loopBacks,
            exits,
            Warnings(detail, loop, bodies, entry, breaking, outgoing, body, exits));
    }

    /// <summary>
    /// The warnings of a marker – with a location, so the graph view (#101) can show them at the frame,
    /// the node or the edge. The <b>texts and their order are unchanged</b>: the loop editor has shown
    /// them this way since #41, and tests depend on the wordings.
    /// </summary>
    private static IReadOnlyList<GraphWarning> Warnings(
        DialogDetail detail,
        LoopDetail loop,
        IReadOnlyDictionary<Guid, HashSet<Guid>> bodies,
        QuestionDetail? entry,
        QuestionDetail? breaking,
        IReadOnlyList<TransitionDetail> outgoing,
        IReadOnlySet<Guid> body,
        IReadOnlyList<TransitionDetail> exits)
    {
        var warnings = new List<GraphWarning>();

        if (entry is null)
        {
            warnings.Add(GraphWarning.ForLoop(
                loop.Id,
                "The entry question no longer belongs to this dialog – the marker points into the void and "
                + "collects nothing. Please choose an existing question or delete the loop."));
        }

        if (breaking is null)
        {
            warnings.Add(GraphWarning.ForLoop(
                loop.Id,
                "The breaking question no longer belongs to this dialog – without it there is no exit from "
                + "the cycle. Please choose an existing question or delete the loop."));
        }

        if (entry is not null && breaking is not null)
        {
            if (!outgoing.Any(transition => transition.TargetQuestionId == entry.Id))
            {
                warnings.Add(GraphWarning.ForQuestion(
                    breaking.Id,
                    $"There is no transition from \"{breaking.Key}\" back to \"{entry.Key}\" – without this "
                    + "back-jump no cycle forms at all, and the answers are not collected per iteration."));
            }

            if (exits.Count == 0)
            {
                warnings.Add(GraphWarning.ForQuestion(
                    breaking.Id,
                    $"The breaking question \"{breaking.Key}\" has no transition out of the loop body – the "
                    + "loop can never be left (infinite loop)."));
            }
            else
            {
                var (reachable, blocker) = InspectExit(outgoing, body);
                if (!reachable)
                {
                    var text =
                        $"The exit from \"{breaking.Key}\" is never evaluated: at runtime a back-jump before "
                        + "it always matches (the first matching non-default wins, an empty expression "
                        + "always matches; otherwise the topmost default). The result is an infinite loop – "
                        + "give the back-jump a condition or sort it after the exit.";

                    // The blocker is the cause – without it only the breaking question is left as location.
                    warnings.Add(blocker is null
                        ? GraphWarning.ForQuestion(breaking.Id, text)
                        : GraphWarning.ForTransition(blocker.Id, breaking.Id, text));
                }
            }
        }

        foreach (var other in detail.Loops.Where(candidate => candidate.Id != loop.Id))
        {
            if (bodies[other.Id].Overlaps(bodies[loop.Id]))
            {
                warnings.Add(GraphWarning.ForLoop(
                    loop.Id,
                    $"The loop body overlaps with the loop \"{other.CollectionKey}\". Nested or overlapping "
                    + "loops are not supported: every session against this dialog aborts with an error "
                    + "already at start."));
            }
        }

        if (!DesignerExpressionContext.IsBindable(loop.CollectionKey))
        {
            warnings.Add(GraphWarning.ForLoop(
                loop.Id,
                $"The collection key \"{loop.CollectionKey}\" is not referenceable in expressions: "
                + DesignerExpressionContext.IdentifierNote(loop.CollectionKey)));
        }
        else if (detail.Questions.Any(question => string.Equals(question.Key, loop.CollectionKey, StringComparison.Ordinal)))
        {
            warnings.Add(GraphWarning.ForLoop(
                loop.Id,
                $"The collection key \"{loop.CollectionKey}\" shadows the question of the same name in the "
                + "expression context – its answer is then no longer reachable in conditions. Rename one of "
                + "the two keys."));
        }

        return warnings;
    }

    /// <summary>
    /// Reproduces the selection of the <c>TransitionResolver</c> and answers whether any exit can apply at
    /// all: the first unconditional non-default always wins; if none is reached, the topmost default
    /// applies.
    /// </summary>
    /// <param name="outgoing">The transitions of the breaking question in evaluation order.</param>
    /// <param name="body">The loop range.</param>
    /// <returns>
    /// Whether the exit is reachable and – if not – which back-jump shadows it. The blocker carries the
    /// warning so the graph view can show it on <b>its</b> edge.
    /// </returns>
    private static (bool Reachable, TransitionDetail? Blocker) InspectExit(
        IReadOnlyList<TransitionDetail> outgoing, IReadOnlySet<Guid> body)
    {
        foreach (var transition in outgoing.Where(transition => !transition.IsDefault))
        {
            if (!body.Contains(transition.TargetQuestionId))
            {
                return (true, null);
            }

            if (string.IsNullOrWhiteSpace(transition.Expression))
            {
                return (false, transition);
            }
        }

        var fallback = outgoing.FirstOrDefault(transition => transition.IsDefault);
        return fallback is not null && !body.Contains(fallback.TargetQuestionId)
            ? (true, null)
            : (false, fallback);
    }

    private static QuestionDetail? Question(DialogDetail detail, Guid questionId)
        => detail.Questions.FirstOrDefault(question => question.Id == questionId);

    /// <summary>Questions reachable forward via outgoing transitions from <paramref name="start"/>; does not expand <paramref name="stopAt"/>.</summary>
    private static HashSet<Guid> ReachableForward(DialogDetail detail, Guid start, Guid stopAt)
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

            foreach (var transition in detail.Transitions.Where(transition => transition.FromQuestionId == current))
            {
                stack.Push(transition.TargetQuestionId);
            }
        }

        return visited;
    }

    /// <summary>Questions from which <paramref name="target"/> is reachable backward via transitions (incl. <paramref name="target"/>).</summary>
    private static HashSet<Guid> ReachableBackward(DialogDetail detail, Guid target)
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

            foreach (var transition in detail.Transitions.Where(transition => transition.TargetQuestionId == current))
            {
                stack.Push(transition.FromQuestionId);
            }
        }

        return visited;
    }
}
