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
/// Pulled out of the <c>@code</c> block of <c>DialogEditor.razor</c>, so that list view and
/// canvas gestures (#103) apply the <b>same</b> rules. Two priority algorithms would be exactly the
/// silent divergence that no one notices until two views claim different orders – and the
/// acceptance criterion "everything appears immediately in the list view too" makes it visible.
/// </para>
/// <para>
/// The dedicated file has another reason: <c>tests/Flirty.Tests/Designer</c> does not render
/// Razor components (no bUnit). What sits in the <c>@code</c> block is not testable; a pure
/// function over <see cref="DialogDetail"/> is directly testable.
/// </para>
/// </remarks>
internal static class GraphEditing
{
    /// <summary>The <c>Order</c> for a question newly appended at the end.</summary>
    /// <param name="detail">The dialog including graph.</param>
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
    /// Deliberately per source question and not dialog-wide: the <c>TransitionResolver</c> only compares
    /// the transitions of <b>one</b> question. A dialog-wide running number would not be wrong, but the
    /// position display ("Position 3") would show gaps that no one can explain.
    /// </remarks>
    /// <param name="detail">The dialog including graph.</param>
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
    /// Swaps two positions of the evaluation order and then writes the <b>position index</b> as the
    /// new priority.
    /// </summary>
    /// <remarks>
    /// The position index instead of swapping the two numbers: only this way are duplicate or
    /// gappy <c>Priority</c> values repaired along the way. With two transitions of identical priority
    /// a pure number swap would have no effect – the order would look unchanged even though the
    /// user has moved it.
    /// </remarks>
    /// <param name="ordered">The outgoing transitions of a question in evaluation order.</param>
    /// <param name="from">The current position.</param>
    /// <param name="to">The target position.</param>
    /// <returns>
    /// Only the transitions whose priority actually changes – empty when there is nothing to write
    /// (position outside the list, unchanged position, priorities already coincident).
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
