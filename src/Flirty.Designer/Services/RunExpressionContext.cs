using Flirty.Runtime;
using Flirty.Runtime.Admin;

namespace Flirty.Designer.Services;

/// <summary>
/// Snapshot of the expression bindings of a running test run – the same three building blocks that the
/// core places into the <c>ExpressionContext</c> at runtime.
/// </summary>
/// <param name="Answers">
/// The last given answer per question, indexed by the domain question key; values are
/// raw JSON text.
/// </param>
/// <param name="Collections">
/// The answers of the loops collected per iteration, indexed by the <c>CollectionKey</c>.
/// </param>
/// <param name="IterationIndex">
/// The zero-based iteration index of the currently open question or <see langword="null"/> if it
/// lies outside a loop or no question is open.
/// </param>
/// <remarks>
/// <see langword="public"/> – since #104 the type is <c>[Parameter]</c> of the <c>GraphRunInspector</c>, which
/// shows the bindings at the selected node. Razor generates components <see langword="public"/>, an
/// <see langword="internal"/> type on a parameter would be CS0053 and under
/// <c>TreatWarningsAsErrors</c> a build error (the same rationale as with
/// <see cref="Flirty.Designer.Models.AnswerInputModel"/>). It is still built exclusively by the
/// <see cref="RunExpressionContext"/>.
/// </remarks>
public sealed record RunExpressionSnapshot(
    IReadOnlyDictionary<string, string?> Answers,
    IReadOnlyDictionary<string, IReadOnlyList<string?>> Collections,
    int? IterationIndex);

/// <summary>
/// Builds the expression bindings of a running test run (#43), so that the test runner can show
/// <b>what</b> the transition and trigger conditions currently compute with.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately mirrors the core-internal <c>SessionExpressionContextBuilder</c>
/// (<c>src/Flirty/Runtime/SessionExpressionContextBuilder.cs</c>) together with the
/// <c>LoopResolver</c> rules it uses: the resolver is <c>internal</c> and works on a <c>Dialog</c> entity
/// with loaded navigations, while the designer only has the navigation-free views
/// <see cref="DialogDetail"/> and <see cref="ResumeDialogResult"/>. The same delimitation as with
/// <see cref="DesignerExpressionContext"/> and <see cref="LoopAnalyzer"/>; against a drift
/// a test in <c>tests/Flirty.Tests/Designer/RunExpressionContextTests</c> secures it, comparing both
/// implementations on the same graph and the same session.
/// </para>
/// <para>
/// The range computation of the loops comes from <see cref="LoopAnalyzer.ComputeBody"/> – not rebuilt
/// again.
/// </para>
/// </remarks>
internal static class RunExpressionContext
{
    /// <summary>
    /// Builds the snapshot from the dialog graph and the read session state.
    /// </summary>
    /// <param name="detail">The dialog together with the graph (from <c>GetDialogQuery</c>).</param>
    /// <param name="state">The session state (from <c>ResumeDialogQuery</c>).</param>
    /// <returns>The bindings at the current point in time of the run.</returns>
    public static RunExpressionSnapshot Build(DialogDetail detail, ResumeDialogResult state)
    {
        ArgumentNullException.ThrowIfNull(detail);
        ArgumentNullException.ThrowIfNull(state);

        var known = detail.Questions.Select(question => question.Id).ToHashSet();

        // Per question the answer with the highest Sequence – within a loop therefore the one of the
        // current iteration (identical to the SessionExpressionContextBuilder).
        var answers = state.Answers
            .Where(answer => known.Contains(answer.QuestionId))
            .GroupBy(answer => answer.QuestionId)
            .ToDictionary(
                group => group.OrderByDescending(answer => answer.Sequence).First().QuestionKey,
                group => (string?)group.OrderByDescending(answer => answer.Sequence).First().Value,
                StringComparer.Ordinal);

        var collections = new Dictionary<string, IReadOnlyList<string?>>(StringComparer.Ordinal);
        foreach (var loop in detail.Loops)
        {
            collections[loop.CollectionKey] = CollectEntries(detail, state, loop);
        }

        return new RunExpressionSnapshot(answers, collections, ResolveIterationIndex(state));
    }

    /// <summary>
    /// Collects the values of a loop collection: the answers of the <b>entry question</b> in the
    /// most recent loop instance, ordered by iteration index. As long as the loop was not entered,
    /// the list stays empty – the key is bound nonetheless, otherwise
    /// <c>skills.Count &gt; 0</c> would not be evaluable before the first iteration.
    /// </summary>
    /// <param name="detail">The dialog together with the graph.</param>
    /// <param name="state">The session state.</param>
    /// <param name="loop">The loop marker.</param>
    /// <returns>The collected raw values per iteration.</returns>
    private static IReadOnlyList<string?> CollectEntries(
        DialogDetail detail, ResumeDialogResult state, LoopDetail loop)
    {
        var body = LoopAnalyzer.ComputeBody(detail, loop);

        var bodyAnswers = state.Answers
            .Where(answer => answer.LoopInstanceId is not null && body.Contains(answer.QuestionId))
            .ToList();

        if (bodyAnswers.Count == 0)
        {
            return [];
        }

        var instanceId = bodyAnswers.OrderByDescending(answer => answer.Sequence).First().LoopInstanceId!.Value;

        return
        [
            .. bodyAnswers
                .Where(answer => answer.LoopInstanceId == instanceId
                    && answer.QuestionId == loop.EntryQuestionId)
                .OrderBy(answer => answer.IterationIndex ?? 0)
                .Select(answer => (string?)answer.Value),
        ];
    }

    /// <summary>
    /// The iteration index with which the conditions of the currently open question compute: the last
    /// assigned index of this question. Without an open question (completed session) or outside a
    /// loop it is <see langword="null"/>.
    /// </summary>
    /// <param name="state">The session state.</param>
    /// <returns>The iteration index or <see langword="null"/>.</returns>
    private static int? ResolveIterationIndex(ResumeDialogResult state)
    {
        if (state.CurrentQuestion is not { } current)
        {
            return null;
        }

        return state.Answers
            .Where(answer => answer.QuestionId == current.Id && answer.IterationIndex is not null)
            .OrderByDescending(answer => answer.Sequence)
            .FirstOrDefault()?.IterationIndex;
    }
}
