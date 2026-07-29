using Flirty.Designer.Models;
using Flirty.Runtime.Admin;

namespace Flirty.Designer.Services;

/// <summary>
/// Reports transition configurations that take effect differently at runtime than intended – the rules mirror
/// the <c>TransitionResolver</c> of the engine: the first matching <b>non</b>-default in
/// <c>Priority</c> order wins (an empty expression always matches), otherwise the first default; if
/// nothing matches, the runtime throws.
/// </summary>
/// <remarks>
/// <para>
/// The rules stood privately, up to #101, in <c>Components/Pages/DialogEditor.razor</c>. They have moved
/// here, because the graph view needs the same findings – there, however,
/// <b>at the affected node or at the affected edge</b> instead of as a running-text list. Wording and
/// order are taken over unchanged; the texts are a contract towards tests and the E2E suite.
/// The same build form as <see cref="LoopAnalyzer"/>: static class, <see cref="DialogDetail"/> in,
/// located warnings out, no DI.
/// </para>
/// <para>
/// What is <b>not</b> here: the loop findings (those are delivered by <see cref="LoopAnalyzer"/>) and the
/// reachability in the graph (that is first known by the <c>DialogGraphBuilder</c>, because it depends on the
/// entry question).
/// </para>
/// </remarks>
internal static class TransitionWarningAnalyzer
{
    /// <summary>The outgoing transitions of a question in evaluation order.</summary>
    /// <param name="detail">The dialog together with the graph.</param>
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
    /// <returns>The warnings to display (empty if everything is coherent).</returns>
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
                "Kein Default-Übergang: Trifft zur Laufzeit keine Bedingung zu, bricht die Session mit "
                + "einem Fehler ab."));
        }

        if (defaults.Count > 1)
        {
            warnings.Add(GraphWarning.ForQuestion(
                from,
                "Mehrere Default-Übergänge – es greift nur der oberste."));
        }

        var decoratedDefault = defaults.FirstOrDefault(
            transition => !string.IsNullOrWhiteSpace(transition.Expression));
        if (decoratedDefault is not null)
        {
            warnings.Add(GraphWarning.ForTransition(
                decoratedDefault.Id,
                from,
                "Die Bedingung eines Default-Übergangs wird zur Laufzeit nicht ausgewertet."));
        }

        if (unconditional.Transition is not null && unconditional.Index < outgoing.Count - 1)
        {
            warnings.Add(GraphWarning.ForTransition(
                unconditional.Transition.Id,
                from,
                $"Der bedingungslose Übergang an Position {unconditional.Index + 1} greift immer – die "
                + "nachfolgenden Übergänge werden nie geprüft."));
        }

        return warnings;
    }

    /// <summary>
    /// Checks the transitions of the whole graph – questions in dialog order, questions without outgoing
    /// transitions skipped (they end regularly and are no finding).
    /// </summary>
    /// <remarks>
    /// Transitions with an unknown source question are left out here: they are never evaluated and
    /// have no node on which a warning could hang. The <c>DialogEditor</c> reports them
    /// separately (<c>Orphans()</c>), the graph view likewise.
    /// </remarks>
    /// <param name="detail">The dialog together with the graph.</param>
    /// <returns>The open warnings; empty if the graph is coherent.</returns>
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
