using Flirty.Runtime.Admin;

namespace Flirty.Designer.Services;

/// <summary>
/// The new priority of a transition after a reordering.
/// </summary>
/// <param name="Transition">The affected transition (unchanged – the update command needs its fields).</param>
/// <param name="Priority">The <c>Priority</c> to write.</param>
internal sealed record TransitionPriority(TransitionDetail Transition, int Priority);

/// <summary>
/// The computation rules by which the designer changes the graph: next <c>Order</c>, next
/// <c>Priority</c> and the reordering of the evaluation order.
/// </summary>
/// <remarks>
/// <para>
/// Extracted from the <c>@code</c> block of <c>DialogEditor.razor</c>, so that list view and
/// canvas gestures (#103) apply the <b>same</b> rules. Two priority algorithms would be exactly the
/// silent divergence that no one notices, until two views assert different orders – and
/// the acceptance criterion "everything appears immediately in the list view too" makes them visible.
/// </para>
/// <para>
/// The own file has one more reason: <c>tests/Flirty.Tests/Designer</c> renders no
/// Razor components (no bUnit). What lies in the <c>@code</c> block is not checkable; a pure
/// function over <see cref="DialogDetail"/> is checkable directly.
/// </para>
/// </remarks>
internal static class GraphEditing
{
    /// <summary>The <c>Order</c> for a question newly appended at the end.</summary>
    /// <param name="detail">The dialog together with the graph.</param>
    /// <returns>The next free sort number.</returns>
    public static int NextOrder(DialogDetail detail)
    {
        ArgumentNullException.ThrowIfNull(detail);

        return detail.Questions.Count == 0 ? 0 : detail.Questions.Max(question => question.Order) + 1;
    }

    /// <summary>
    /// The <c>Priority</c> for a new transition – evaluated last within its source question.
    /// </summary>
    /// <remarks>
    /// Deliberately per source question and not dialog-wide: the <c>TransitionResolver</c> compares only the
    /// transitions of <b>one</b> question. A dialog-wide running number would not be wrong, but the
    /// position display ("Position 3") would show holes that no one can explain.
    /// </remarks>
    /// <param name="detail">The dialog together with the graph.</param>
    /// <param name="fromQuestionId">The source question.</param>
    /// <returns>The next free priority.</returns>
    public static int NextPriority(DialogDetail detail, Guid fromQuestionId)
    {
        ArgumentNullException.ThrowIfNull(detail);

        var siblings = detail.Transitions
            .Where(transition => transition.FromQuestionId == fromQuestionId)
            .ToList();

        return siblings.Count == 0 ? 0 : siblings.Max(transition => transition.Priority) + 1;
    }

    /// <summary>
    /// Swaps two positions of the evaluation order and afterwards writes the
    /// <b>position index</b> as the new priority.
    /// </summary>
    /// <remarks>
    /// The position index instead of a swap of the two numbers: only so are duplicate or
    /// gapped <c>Priority</c> values repaired along the way. With two transitions of identical priority
    /// a pure number swap would remain ineffective – the order would look unchanged, although the
    /// user has moved it.
    /// </remarks>
    /// <param name="ordered">The outgoing transitions of a question in evaluation order.</param>
    /// <param name="from">The current position.</param>
    /// <param name="to">The target position.</param>
    /// <returns>
    /// Only the transitions whose priority actually changes – empty if there is nothing to write
    /// (position outside the list, unchanged position, priorities already congruent).
    /// </returns>
    public static IReadOnlyList<TransitionPriority> Reorder(
        IReadOnlyList<TransitionDetail> ordered,
        int from,
        int to)
    {
        ArgumentNullException.ThrowIfNull(ordered);

        if (from < 0 || from >= ordered.Count || to < 0 || to >= ordered.Count || from == to)
        {
            return [];
        }

        var moved = ordered.ToList();
        (moved[from], moved[to]) = (moved[to], moved[from]);

        return
        [
            .. moved
                .Select((transition, index) => new TransitionPriority(transition, index))
                .Where(entry => entry.Transition.Priority != entry.Priority),
        ];
    }
}
