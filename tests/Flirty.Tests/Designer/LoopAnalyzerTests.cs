using Flirty.Designer.Models;
using Flirty.Designer.Services;
using Flirty.Domain;
using Flirty.Runtime;
using Flirty.Runtime.Admin;
using Flirty.Tests.Persistence;

namespace Flirty.Tests.Designer;

/// <summary>
/// Tests for the loop editor's <see cref="LoopAnalyzer"/> (#41): computing the loop range, sorting
/// the transitions into back jumps and exits as well as the warnings – above all the cycle without a
/// reachable exit (an infinite loop). The core check is the match against the core
/// <see cref="LoopResolver"/>: the designer recomputes the range because the resolver is not
/// reusable – the two must not drift apart nonetheless.
/// </summary>
public sealed class LoopAnalyzerTests
{
    /// <summary>
    /// The analyzer mirrors <c>LoopResolver.ComputeBody</c>. Since the range is private there, it is
    /// queried indirectly: <see cref="LoopResolver.ResolveAssignment"/> assigns an instance id
    /// exactly for questions inside the loop range. Both run on the same graph – the designer graph
    /// arises from the entity via <c>AdminProjection</c>, so that no deviation can hide in the test
    /// data.
    /// </summary>
    [Theory]
    [InlineData("more == \"yes\"")]
    [InlineData("positions.Count < 2")]
    public void ComputeBody_matches_the_engines_LoopResolver(string loopBackExpression)
    {
        var dialog = TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out _, loopBackExpression);
        var detail = AdminProjection.ToDetail(dialog);
        var resolver = new LoopResolver(dialog);
        var session = NewSession(dialog);

        var fromResolver = dialog.Questions
            .Where(question => resolver.ResolveAssignment(session, question.Id).LoopInstanceId is not null)
            .Select(question => question.Id)
            .ToHashSet();

        var fromAnalyzer = LoopAnalyzer.ComputeBody(detail, detail.Loops[0]);

        Assert.Equal(fromResolver, fromAnalyzer);
    }

    /// <summary>
    /// The loop range covers the entry and the breaking question, but not the downstream question –
    /// its answers carry no iteration index at runtime.
    /// </summary>
    [Fact]
    public void Analyze_computes_the_range_the_back_jump_and_the_exit()
    {
        var dialog = TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out var ids);
        var detail = AdminProjection.ToDetail(dialog);

        var insight = Assert.Single(LoopAnalyzer.Analyze(detail));

        Assert.Equal(
            [ids.PositionQuestionId, ids.MoreQuestionId],
            insight.Body.Select(question => question.Id));
        Assert.Equal(ids.PositionQuestionId, insight.EntryQuestion!.Id);
        Assert.Equal(ids.MoreQuestionId, insight.BreakingQuestion!.Id);
        Assert.Equal(ids.PositionQuestionId, Assert.Single(insight.LoopBackTransitions).TargetQuestionId);
        Assert.Equal(ids.SummaryQuestionId, Assert.Single(insight.ExitTransitions).TargetQuestionId);
        Assert.Empty(insight.Warnings);
    }

    /// <summary>A single-question loop (<c>Entry == Breaking</c>) is allowed and yields exactly that question.</summary>
    [Fact]
    public void Analyze_a_single_question_loop_yields_only_the_entry_question()
    {
        var dialog = TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out var ids);
        dialog.Loops.First().EntryQuestionId = ids.MoreQuestionId;
        var detail = AdminProjection.ToDetail(dialog);

        var insight = Assert.Single(LoopAnalyzer.Analyze(detail));

        Assert.Equal([ids.MoreQuestionId], insight.Body.Select(question => question.Id));
    }

    [Fact]
    public void Analyze_warns_when_the_back_jump_is_missing()
    {
        var dialog = TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out var ids);
        dialog.Transitions.Remove(
            dialog.Transitions.First(transition => transition.TargetQuestionId == ids.PositionQuestionId
                                                && transition.FromQuestionId == ids.MoreQuestionId));
        var detail = AdminProjection.ToDetail(dialog);

        var insight = Assert.Single(LoopAnalyzer.Analyze(detail));

        Assert.Contains(insight.Warnings, warning => warning.Contains("no cycle", StringComparison.Ordinal));
    }

    /// <summary>Without a transition out of the range the loop can never be left.</summary>
    [Fact]
    public void Analyze_warns_on_a_missing_exit()
    {
        var dialog = TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out var ids);
        dialog.Transitions.Remove(
            dialog.Transitions.First(transition => transition.TargetQuestionId == ids.SummaryQuestionId));
        var detail = AdminProjection.ToDetail(dialog);

        var insight = Assert.Single(LoopAnalyzer.Analyze(detail));

        Assert.Empty(insight.ExitTransitions);
        Assert.Contains(insight.Warnings, warning => warning.Contains("infinite loop", StringComparison.Ordinal));
    }

    /// <summary>
    /// An unconditional back jump placed before the exit always takes effect at runtime – the exit is
    /// never evaluated. Exactly the <c>TransitionResolver</c>'s rule: the first matching non-default
    /// wins.
    /// </summary>
    [Fact]
    public void Analyze_warns_when_an_unconditional_back_jump_shadows_the_exit()
    {
        var dialog = TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out var ids);
        dialog.Transitions
            .First(transition => transition.FromQuestionId == ids.MoreQuestionId
                              && transition.TargetQuestionId == ids.PositionQuestionId)
            .Expression = null;
        var detail = AdminProjection.ToDetail(dialog);

        var insight = Assert.Single(LoopAnalyzer.Analyze(detail));

        Assert.NotEmpty(insight.ExitTransitions);
        Assert.Contains(insight.Warnings, warning => warning.Contains("never evaluated", StringComparison.Ordinal));
    }

    /// <summary>
    /// If the exit stands before the unconditional back jump it takes effect – the same configuration
    /// must then produce no warning any more.
    /// </summary>
    [Fact]
    public void Analyze_accepts_an_exit_before_the_unconditional_back_jump()
    {
        var dialog = TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out var ids);
        var loopBack = dialog.Transitions.First(transition => transition.FromQuestionId == ids.MoreQuestionId
                                                          && transition.TargetQuestionId == ids.PositionQuestionId);
        var exit = dialog.Transitions.First(transition => transition.TargetQuestionId == ids.SummaryQuestionId);
        loopBack.Expression = null;
        loopBack.Priority = 1;
        exit.Expression = "more == \"no\"";
        exit.IsDefault = false;
        exit.Priority = 0;
        var detail = AdminProjection.ToDetail(dialog);

        var insight = Assert.Single(LoopAnalyzer.Analyze(detail));

        Assert.Empty(insight.Warnings);
    }

    /// <summary>
    /// The <see cref="LoopResolver"/> makes overlapping loop ranges fail already in the constructor –
    /// every session against the dialog then breaks. The analyzer has to make that visible beforehand.
    /// </summary>
    [Fact]
    public void Analyze_warns_on_overlapping_loops()
    {
        var dialog = TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out var ids);
        dialog.Loops.Add(new LoopDefinition
        {
            Id = Guid.NewGuid(),
            DialogId = dialog.Id,
            CollectionKey = "zweite",
            EntryQuestionId = ids.PositionQuestionId,
            BreakingQuestionId = ids.MoreQuestionId,
        });
        var detail = AdminProjection.ToDetail(dialog);

        Assert.Throws<InvalidOperationException>(() => new LoopResolver(dialog));
        Assert.All(
            LoopAnalyzer.Analyze(detail),
            insight => Assert.Contains(
                insight.Warnings, warning => warning.Contains("overlaps", StringComparison.Ordinal)));
    }

    [Fact]
    public void Analyze_warns_when_the_collection_key_shadows_a_question()
    {
        var dialog = TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out _);
        dialog.Loops.First().CollectionKey = "summary";
        var detail = AdminProjection.ToDetail(dialog);

        var insight = Assert.Single(LoopAnalyzer.Analyze(detail));

        Assert.Contains(insight.Warnings, warning => warning.Contains("shadows", StringComparison.Ordinal));
    }

    /// <summary>
    /// A key that is not an identifier (or is shadowed by <c>now</c>/<c>iterationIndex</c>/
    /// <c>session</c>) cannot be referenced in any condition – the loop would be unusable.
    /// </summary>
    [Theory]
    [InlineData("meine-positionen")]
    [InlineData("iterationIndex")]
    public void Analyze_warns_on_a_collection_key_that_cannot_be_referenced(string collectionKey)
    {
        var dialog = TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out _);
        dialog.Loops.First().CollectionKey = collectionKey;
        var detail = AdminProjection.ToDetail(dialog);

        var insight = Assert.Single(LoopAnalyzer.Analyze(detail));

        Assert.Contains(
            insight.Warnings, warning => warning.Contains("not referenceable", StringComparison.Ordinal));
    }

    /// <summary>If the marker points at a deleted question, the range stays empty and is reported.</summary>
    [Fact]
    public void Analyze_warns_on_a_marker_pointing_at_an_unknown_question()
    {
        var dialog = TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out _);
        dialog.Loops.First().EntryQuestionId = Guid.NewGuid();
        var detail = AdminProjection.ToDetail(dialog);

        var insight = Assert.Single(LoopAnalyzer.Analyze(detail));

        Assert.Empty(insight.Body);
        Assert.Null(insight.EntryQuestion);
        Assert.Contains(insight.Warnings, warning => warning.Contains("entry question", StringComparison.Ordinal));
    }

    /// <summary>
    /// Since #101 every warning carries a location, so that the graph view can show it at the
    /// affected element. A warning without a target would be invisible on the canvas – which is why
    /// every one has to have one, and the reference has to point at an element of <b>this</b> dialog.
    /// </summary>
    [Fact]
    public void Analyze_places_every_warning_at_an_element()
    {
        // A marker without a back jump and without an exit: produces warnings on the loop and on the
        // breaking question.
        var dialog = TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out var ids);
        dialog.Transitions.Clear();
        dialog.Loops.First().CollectionKey = "more";
        var detail = AdminProjection.ToDetail(dialog);

        var insight = Assert.Single(LoopAnalyzer.Analyze(detail));

        Assert.NotEmpty(insight.TargetedWarnings);
        Assert.All(insight.TargetedWarnings, warning =>
        {
            Assert.NotEqual(GraphElementKind.Dialog, warning.Kind);
            Assert.NotNull(warning.ElementId);
        });
        Assert.Contains(
            insight.TargetedWarnings,
            warning => warning.Kind == GraphElementKind.Question && warning.ElementId == ids.MoreQuestionId);
        Assert.Contains(
            insight.TargetedWarnings,
            warning => warning.Kind == GraphElementKind.Loop && warning.ElementId == detail.Loops[0].Id);
    }

    /// <summary>
    /// The shadowed exit has a concrete cause – the back jump that always takes effect before it. The
    /// warning hangs on <b>its</b> edge, otherwise the canvas cannot show which connection has to be
    /// changed.
    /// </summary>
    [Fact]
    public void Analyze_places_the_shadowed_exit_on_the_shadowing_back_jump()
    {
        var dialog = TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out var ids);
        var loopBack = dialog.Transitions.First(
            transition => transition.TargetQuestionId == ids.PositionQuestionId);
        loopBack.Expression = null;
        var detail = AdminProjection.ToDetail(dialog);

        var insight = Assert.Single(LoopAnalyzer.Analyze(detail));

        var warning = Assert.Single(
            insight.TargetedWarnings,
            candidate => candidate.Text.Contains("never evaluated", StringComparison.Ordinal));
        Assert.Equal(GraphElementKind.Transition, warning.Kind);
        Assert.Equal(loopBack.Id, warning.ElementId);
        Assert.Equal(ids.MoreQuestionId, warning.QuestionId);
    }

    /// <summary>
    /// Since #101 <c>Warnings</c> is a computed view of <c>TargetedWarnings</c>. Loop and dialog
    /// editor read exclusively that view – wording and order have to stay congruent.
    /// </summary>
    [Fact]
    public void Analyze_returns_the_warnings_in_unchanged_order_and_wording()
    {
        var dialog = TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out _);
        dialog.Transitions.Clear();
        var detail = AdminProjection.ToDetail(dialog);

        var insight = Assert.Single(LoopAnalyzer.Analyze(detail));

        Assert.Equal(insight.TargetedWarnings.Select(warning => warning.Text), insight.Warnings);
    }

    // ---- Back-jump detection (pulled out of the DialogEditor, #103) --------------------------------

    /// <summary>
    /// A back jump points at an earlier question <b>of the list order</b> – deliberately not at a
    /// higher layer of the layout, so that list view and graph edge claim the same thing.
    /// </summary>
    [Fact]
    public void IsBackJump_recognizes_only_backward_edges()
    {
        var dialog = TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out var ids);
        var detail = AdminProjection.ToDetail(dialog);

        var backward = detail.Transitions.Single(
            transition => transition.FromQuestionId == ids.MoreQuestionId
                && transition.TargetQuestionId == ids.PositionQuestionId);
        var forward = detail.Transitions.Single(
            transition => transition.FromQuestionId == ids.PositionQuestionId
                && transition.TargetQuestionId == ids.MoreQuestionId);

        Assert.True(LoopAnalyzer.IsBackJump(detail, backward));
        Assert.False(LoopAnalyzer.IsBackJump(detail, forward));
    }

    /// <summary>A reference to itself is a cycle – <c>target &lt;= from</c> includes it.</summary>
    [Fact]
    public void IsBackJump_counts_the_self_reference()
    {
        var dialog = TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out var ids);
        var detail = AdminProjection.ToDetail(dialog);
        var self = new TransitionDetail(
            Guid.NewGuid(), dialog.Id, ids.MoreQuestionId, ids.MoreQuestionId, null, 9, false);

        Assert.True(LoopAnalyzer.IsBackJump(detail, self));
    }

    /// <summary>
    /// The matching marker makes a back jump "marked". Exactly this list feeds the suggestions – in
    /// the list view (#41) as well as at the cycle on the canvas (#103).
    /// </summary>
    [Fact]
    public void UnmarkedBackJumps_excludes_marked_back_jumps()
    {
        var dialog = TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out _);

        Assert.Empty(LoopAnalyzer.UnmarkedBackJumps(AdminProjection.ToDetail(dialog)));

        // Without a marker the same cycle remains – the answers would be overwritten at runtime
        // instead of collected, and that is exactly what the suggestion points at.
        dialog.Loops.Clear();
        var withoutMarker = LoopAnalyzer.UnmarkedBackJumps(AdminProjection.ToDetail(dialog));

        var backward = Assert.Single(withoutMarker);
        Assert.True(LoopAnalyzer.IsBackJump(AdminProjection.ToDetail(dialog), backward));
    }

    /// <summary>
    /// A marker on a <b>different</b> question pair does not count: it describes a different cycle.
    /// </summary>
    [Fact]
    public void UnmarkedBackJumps_checks_the_question_pair_not_just_the_existence_of_a_marker()
    {
        var dialog = TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out var ids);
        var loop = dialog.Loops.Single();
        loop.BreakingQuestionId = ids.SummaryQuestionId;

        Assert.Single(LoopAnalyzer.UnmarkedBackJumps(AdminProjection.ToDetail(dialog)));
    }

    /// <summary>Forward edges never appear among the suggestions.</summary>
    [Fact]
    public void UnmarkedBackJumps_contains_no_forward_edges()
    {
        var dialog = TestDialogFactory.BuildBranchingDialog(Guid.NewGuid(), out _);

        Assert.Empty(LoopAnalyzer.UnmarkedBackJumps(AdminProjection.ToDetail(dialog)));
    }

    private static DialogSession NewSession(Dialog dialog)
        => new()
        {
            Id = Guid.NewGuid(),
            DialogId = dialog.Id,
            DialogVersion = dialog.Version,
            ExternalUserKey = "designer",
            Status = SessionStatus.InProgress,
            StartedAt = TestDialogFactory.SampleTime,
        };
}
