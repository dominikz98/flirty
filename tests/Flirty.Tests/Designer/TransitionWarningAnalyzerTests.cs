using Flirty.Designer.Models;
using Flirty.Designer.Services;
using Flirty.Domain;
using Flirty.Runtime.Admin;
using Flirty.Tests.Persistence;

namespace Flirty.Tests.Designer;

/// <summary>
/// Tests for the <see cref="TransitionWarningAnalyzer"/> – the transition warnings that lived
/// privately in the <c>DialogEditor</c> until #101 and have fed the graph view since. Two things are
/// nailed down here: the <b>wordings</b> (the list view and the E2E suite hang on them) and the
/// <b>placement</b> on the node or on the edge (without it the canvas cannot show the findings at the
/// element that causes them).
/// </summary>
public sealed class TransitionWarningAnalyzerTests
{
    private static readonly Guid DialogId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid FromId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid TargetId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid OtherTargetId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    /// <summary>
    /// Without a default and without an unconditional transition, nothing can take effect at runtime.
    /// The warning hangs on the <b>question</b> – no single transition is to blame.
    /// </summary>
    [Fact]
    public void Analyze_reports_a_missing_default_on_the_source_question()
    {
        IReadOnlyList<TransitionDetail> outgoing = [Transition(0, expression: "a == 1")];

        var warning = Assert.Single(TransitionWarningAnalyzer.Analyze(outgoing));

        Assert.Equal(GraphElementKind.Question, warning.Kind);
        Assert.Equal(FromId, warning.ElementId);
        Assert.Equal(FromId, warning.QuestionId);
        Assert.Contains("No default transition", warning.Text, StringComparison.Ordinal);
    }

    /// <summary>Several defaults are a property of the group, so of the question.</summary>
    [Fact]
    public void Analyze_reports_several_defaults_on_the_source_question()
    {
        IReadOnlyList<TransitionDetail> outgoing =
        [
            Transition(0, isDefault: true),
            Transition(1, isDefault: true, target: OtherTargetId),
        ];

        var warning = Assert.Single(TransitionWarningAnalyzer.Analyze(outgoing));

        Assert.Equal(GraphElementKind.Question, warning.Kind);
        Assert.Equal(FromId, warning.ElementId);
        Assert.Contains("Multiple default transitions", warning.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The ignored condition is the property of <b>one</b> transition – the warning has to point at
    /// its edge, not at the question. That is exactly what the canvas needs.
    /// </summary>
    [Fact]
    public void Analyze_reports_the_ignored_condition_on_the_affected_default_transition()
    {
        var decorated = Transition(1, expression: "a == 1", isDefault: true, target: OtherTargetId);
        IReadOnlyList<TransitionDetail> outgoing = [Transition(0, expression: "b == 2"), decorated];

        var warning = Assert.Single(
            TransitionWarningAnalyzer.Analyze(outgoing),
            candidate => candidate.Kind == GraphElementKind.Transition);

        Assert.Equal(decorated.Id, warning.ElementId);
        Assert.Equal(FromId, warning.QuestionId);
        Assert.Contains("not evaluated", warning.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// An unconditional non-default always takes effect and shadows everything after it. That is an
    /// edge statement too – the position in the text is 1-based, as in the list view.
    /// </summary>
    [Fact]
    public void Analyze_reports_the_shadowing_unconditional_transition_on_the_edge()
    {
        var blocking = Transition(0);
        IReadOnlyList<TransitionDetail> outgoing = [blocking, Transition(1, isDefault: true, target: OtherTargetId)];

        var warning = Assert.Single(
            TransitionWarningAnalyzer.Analyze(outgoing),
            candidate => candidate.Kind == GraphElementKind.Transition);

        Assert.Equal(blocking.Id, warning.ElementId);
        Assert.Equal(
            "The unconditional transition at position 1 always matches – the following transitions are "
            + "never evaluated.",
            warning.Text);
    }

    /// <summary>
    /// The most important test of the rework from #101: the four full texts are a contract. The
    /// <c>DialogEditor</c> shows them unchanged, the E2E suite and the publish confirmation hang on
    /// them. Whoever rewords something here changes the UI – and has to do so deliberately.
    /// </summary>
    [Fact]
    public void Analyze_returns_the_existing_wordings_unchanged()
    {
        IReadOnlyList<TransitionDetail> outgoing =
        [
            Transition(0),
            Transition(1, expression: "a == 1", isDefault: true, target: OtherTargetId),
            Transition(2, isDefault: true, target: TargetId),
        ];

        var texts = TransitionWarningAnalyzer.Analyze(outgoing).Select(warning => warning.Text);

        Assert.Equal(
            [
                "Multiple default transitions – only the topmost one applies.",
                "The condition of a default transition is not evaluated at runtime.",
                "The unconditional transition at position 1 always matches – the following transitions are "
                + "never evaluated.",
            ],
            texts);
    }

    /// <summary>The engine's consistent branching dialog produces no warning.</summary>
    [Fact]
    public void Analyze_reports_nothing_for_a_consistent_graph()
    {
        var detail = AdminProjection.ToDetail(TestDialogFactory.BuildBranchingDialog(Guid.NewGuid(), out _));

        Assert.Empty(TransitionWarningAnalyzer.Analyze(detail));
    }

    /// <summary>
    /// Across the whole graph the check runs in question order; questions without outgoing
    /// transitions end regularly and are no finding. Both have to stay that way, because the
    /// <c>DialogEditor</c> shows the order unchanged as a list.
    /// </summary>
    [Fact]
    public void Analyze_over_the_dialog_follows_the_question_order_and_skips_terminal_questions()
    {
        var dialog = TestDialogFactory.BuildBranchingDialog(Guid.NewGuid(), out var ids);

        // The default is dropped -> the entry question has only a conditional edge left.
        dialog.Transitions.Remove(dialog.Transitions.First(transition => transition.IsDefault));

        // And the question that used to be terminal gets an incomplete group too.
        dialog.Transitions.Add(new Transition
        {
            Id = Guid.NewGuid(),
            DialogId = dialog.Id,
            FromQuestionId = ids.DevQuestionId,
            TargetQuestionId = ids.PmQuestionId,
            Expression = "devDetail == \"csharp\"",
            Priority = 0,
        });

        var warnings = TransitionWarningAnalyzer.Analyze(AdminProjection.ToDetail(dialog));

        Assert.Equal([ids.RoleQuestionId, ids.DevQuestionId], warnings.Select(warning => warning.QuestionId));
        Assert.All(warnings, warning => Assert.Contains("No default transition", warning.Text, StringComparison.Ordinal));
    }

    /// <summary>
    /// Transitions with an unknown source question are never evaluated and have no node a warning
    /// could hang on – they stay out of this here and are reported separately.
    /// </summary>
    [Fact]
    public void Analyze_over_the_dialog_ignores_orphaned_transitions()
    {
        var dialog = TestDialogFactory.BuildBranchingDialog(Guid.NewGuid(), out _);
        dialog.Transitions.Add(new Transition
        {
            Id = Guid.NewGuid(),
            DialogId = dialog.Id,
            FromQuestionId = Guid.NewGuid(),
            TargetQuestionId = dialog.StartQuestionId!.Value,
            Priority = 0,
        });

        Assert.Empty(TransitionWarningAnalyzer.Analyze(AdminProjection.ToDetail(dialog)));
    }

    /// <summary>The outgoing transitions come in evaluation order, not in storage order.</summary>
    [Fact]
    public void Outgoing_sorts_by_priority()
    {
        var dialog = TestDialogFactory.BuildBranchingDialog(Guid.NewGuid(), out var ids);
        foreach (var transition in dialog.Transitions)
        {
            transition.Priority = transition.IsDefault ? 0 : 1;
        }

        var outgoing = TransitionWarningAnalyzer.Outgoing(AdminProjection.ToDetail(dialog), ids.RoleQuestionId);

        Assert.Equal([true, false], outgoing.Select(transition => transition.IsDefault));
    }

    private static TransitionDetail Transition(
        int priority, string? expression = null, bool isDefault = false, Guid? target = null)
        => new(
            Guid.Parse($"55555555-5555-5555-5555-{priority:D12}"),
            DialogId,
            FromId,
            target ?? TargetId,
            expression,
            priority,
            isDefault);
}
