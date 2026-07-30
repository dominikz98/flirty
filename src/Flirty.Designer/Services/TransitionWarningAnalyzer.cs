using Flirty.Designer.Models;
using Flirty.Runtime.Admin;

namespace Flirty.Designer.Services;

/// <summary>
/// Reports transition configurations that behave at runtime differently than intended – the rules mirror
/// the engine's <c>TransitionResolver</c>: the first matching <b>non</b>-default in <c>Priority</c> order
/// wins (an empty expression always matches), otherwise the first default; if nothing matches, the
/// runtime throws.
/// </summary>
/// <remarks>
/// <para>
/// Until #101 the rules lived privately in <c>Components/Pages/DialogEditor.razor</c>. They moved here
/// because the graph view needs the same findings – there, however, <b>at the affected node or edge</b>
/// instead of as a running-text list. Wording and order are carried over unchanged; the texts are a
/// contract towards the tests and the E2E suite. Same shape as <see cref="LoopAnalyzer"/>: a static class,
/// <see cref="DialogDetail"/> in, located warnings out, no DI.
/// </para>
/// <para>
/// What is <b>not</b> here: the loop findings (those come from <see cref="LoopAnalyzer"/>) and the
/// reachability in the graph (only the <c>DialogGraphBuilder</c> knows that, because it depends on the
/// entry question).
/// </para>
/// </remarks>
internal static class TransitionWarningAnalyzer
{
    /// <summary>The outgoing transitions of a question in evaluation order.</summary>
    /// <param name="detail">The dialog including its graph.</param>
    /// <param name="questionId">The source question.</param>
    /// <returns>The transitions sorted by <c>Priority</c>.</returns>
    public static IReadOnlyList<TransitionDetail> Outgoing(DialogDetail detail, Guid questionId)
    {
        ArgumentNullException.ThrowIfNull(detail);

        return
        [
            .. detail.Transitions
                .Where(transition => transition.FromQuestionId == questionId)
                .OrderBy(transition => transition.Priority)
        ];
    }

    /// <summary>
    /// Checks the transitions of <b>one</b> source question.
    /// </summary>
    /// <param name="outgoing">The transitions of a source question in evaluation order.</param>
    /// <returns>The warnings to display (empty when everything is consistent).</returns>
    public static IReadOnlyList<GraphWarning> Analyze(IReadOnlyList<TransitionDetail> outgoing)
    {
        ArgumentNullException.ThrowIfNull(outgoing);

        if (outgoing.Count == 0)
        {
            return [];
        }

        // The source question is the same for all transitions – it carries the warnings that cannot be
        // blamed on a single transition.
        var from = outgoing[0].FromQuestionId;

        var warnings = new List<GraphWarning>();
        var defaults = outgoing.Where(transition => transition.IsDefault).ToList();
        var unconditional = outgoing
            .Select((transition, index) => (Transition: transition, Index: index))
            .FirstOrDefault(entry =>
                !entry.Transition.IsDefault && string.IsNullOrWhiteSpace(entry.Transition.Expression));

        if (defaults.Count == 0 && unconditional.Transition is null)
        {
            warnings.Add(GraphWarning.ForQuestion(
                from,
                "No default transition: if no condition matches at runtime, the session aborts with an "
                + "error."));
        }

        if (defaults.Count > 1)
        {
            warnings.Add(GraphWarning.ForQuestion(
                from,
                "Multiple default transitions – only the topmost one applies."));
        }

        var decoratedDefault = defaults.FirstOrDefault(
            transition => !string.IsNullOrWhiteSpace(transition.Expression));
        if (decoratedDefault is not null)
        {
            warnings.Add(GraphWarning.ForTransition(
                decoratedDefault.Id,
                from,
                "The condition of a default transition is not evaluated at runtime."));
        }

        if (unconditional.Transition is not null && unconditional.Index < outgoing.Count - 1)
        {
            warnings.Add(GraphWarning.ForTransition(
                unconditional.Transition.Id,
                from,
                $"The unconditional transition at position {unconditional.Index + 1} always matches – the "
                + "following transitions are never evaluated."));
        }

        return warnings;
    }

    /// <summary>
    /// Checks the transitions of the whole graph – questions in dialog order, questions without outgoing
    /// transitions skipped (they end regularly and are not a finding).
    /// </summary>
    /// <remarks>
    /// Transitions with an unknown source question are left out here: they are never evaluated and have no
    /// node a warning could hang on. The <c>DialogEditor</c> reports them separately (<c>Orphans()</c>),
    /// and so does the graph view.
    /// </remarks>
    /// <param name="detail">The dialog including its graph.</param>
    /// <returns>The open warnings; empty when the graph is consistent.</returns>
    public static IReadOnlyList<GraphWarning> Analyze(DialogDetail detail)
    {
        ArgumentNullException.ThrowIfNull(detail);

        return
        [
            .. from question in detail.Questions
               let outgoing = Outgoing(detail, question.Id)
               where outgoing.Count > 0
               from warning in Analyze(outgoing)
               select warning
        ];
    }
}
