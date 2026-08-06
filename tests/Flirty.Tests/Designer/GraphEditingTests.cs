using Flirty.Designer.Services;
using Flirty.Domain;
using Flirty.Runtime.Admin;

namespace Flirty.Tests.Designer;

/// <summary>
/// Tests for the computation rules of the graph editing (#103): the next <c>Order</c>, the next
/// <c>Priority</c> and the resorting of the evaluation order.
/// </summary>
/// <remarks>
/// Until #103 these rules lived privately in the <c>@code</c> block of <c>DialogEditor.razor</c> and
/// were therefore untestable – the designer has no bUnit, no Razor component is rendered. Pulling
/// them out was the precondition for list view and canvas provably computing the same order.
/// </remarks>
public sealed class GraphEditingTests
{
    private static readonly Guid DialogId = Guid.NewGuid();

    [Fact]
    public void NextOrder_starts_at_zero_when_the_dialog_is_empty()
        => Assert.Equal(0, GraphEditing.NextOrder(Dialog()));

    [Fact]
    public void NextOrder_hangs_on_the_largest_number_not_on_the_count()
    {
        // Gaps in the order values come from deleting. Whoever takes the count instead of the maximum
        // hands out a number that is already taken.
        var detail = Dialog(Question("a", 0), Question("b", 7));

        Assert.Equal(8, GraphEditing.NextOrder(detail));
    }

    [Fact]
    public void NextPriority_counts_per_source_question_not_dialog_wide()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var detail = Dialog(
            questions: [Question("a", 0, first), Question("b", 1, second)],
            transitions:
            [
                Transition(first, second, priority: 0),
                Transition(first, second, priority: 1),
                Transition(second, first, priority: 0),
            ]);

        // The second question has only one transition – counted dialog-wide this would yield 2, and
        // the position display would show a gap nobody can explain.
        Assert.Equal(2, GraphEditing.NextPriority(detail, first));
        Assert.Equal(1, GraphEditing.NextPriority(detail, second));
    }

    [Fact]
    public void NextPriority_starts_at_zero_without_outgoing_transitions()
        => Assert.Equal(0, GraphEditing.NextPriority(Dialog(Question("a", 0)), Guid.NewGuid()));

    [Fact]
    public void Reorder_writes_the_position_index_as_the_priority()
    {
        var from = Guid.NewGuid();
        var target = Guid.NewGuid();
        var ordered = new[]
        {
            Transition(from, target, priority: 0),
            Transition(from, target, priority: 1),
            Transition(from, target, priority: 2),
        };

        var changed = GraphEditing.Reorder(ordered, 2, 0);

        // Positions 0 and 2 are swapped; position 1 stays where it is and must therefore not be
        // written.
        Assert.Equal(2, changed.Count);
        Assert.Equal(ordered[2].Id, changed[0].Transition.Id);
        Assert.Equal(0, changed[0].Priority);
        Assert.Equal(ordered[0].Id, changed[1].Transition.Id);
        Assert.Equal(2, changed[1].Priority);
    }

    [Fact]
    public void Reorder_repairs_duplicate_priorities()
    {
        var from = Guid.NewGuid();
        var target = Guid.NewGuid();

        // Two equal priorities: a pure number swap would have no effect – the order would look
        // unchanged although the user moved it. That is exactly why the index is written.
        var ordered = new[]
        {
            Transition(from, target, priority: 5),
            Transition(from, target, priority: 5),
        };

        var changed = GraphEditing.Reorder(ordered, 0, 1);

        Assert.Equal(2, changed.Count);
        Assert.Equal([0, 1], changed.Select(entry => entry.Priority));
    }

    [Fact]
    public void Reorder_reports_nothing_when_nothing_changes()
    {
        var from = Guid.NewGuid();
        var target = Guid.NewGuid();
        var ordered = new[] { Transition(from, target, priority: 0), Transition(from, target, priority: 1) };

        Assert.Empty(GraphEditing.Reorder(ordered, 1, 1));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(0, 2)]
    [InlineData(2, 0)]
    public void Reorder_ignores_positions_outside_the_list(int from, int to)
    {
        var source = Guid.NewGuid();
        var target = Guid.NewGuid();
        var ordered = new[] { Transition(source, target, priority: 0), Transition(source, target, priority: 1) };

        Assert.Empty(GraphEditing.Reorder(ordered, from, to));
    }

    private static DialogDetail Dialog(params QuestionDetail[] questions)
        => Dialog(questions, []);

    private static DialogDetail Dialog(
        IReadOnlyList<QuestionDetail> questions,
        IReadOnlyList<TransitionDetail> transitions)
        => new(
            new DialogSummary(
                DialogId, "dialog", "Dialog", null, 1, false, null,
                DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch),
            questions,
            transitions,
            [],
            [],
            []);

    private static QuestionDetail Question(string key, int order, Guid? id = null)
        => new(
            id ?? Guid.NewGuid(), DialogId, key, $"Frage {key}", QuestionType.FreeText, null, order,
            false, null, []);

    private static TransitionDetail Transition(Guid from, Guid target, int priority)
        => new(Guid.NewGuid(), DialogId, from, target, null, priority, false);
}
