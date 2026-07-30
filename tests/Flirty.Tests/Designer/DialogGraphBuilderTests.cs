using Flirty.Designer.Models;
using Flirty.Designer.Services;
using Flirty.Domain;
using Flirty.Runtime.Admin;
using Flirty.Tests.Persistence;

namespace Flirty.Tests.Designer;

/// <summary>
/// Tests for the <see cref="DialogGraphBuilder"/> – the drawing model of the graph view (#101). What
/// is checked above all is that the domain model's statements arrive on the canvas <b>honestly</b>:
/// the regular completion as a completion and not as a broken edge, unreachable questions as such,
/// and every warning at the element that causes it.
/// </summary>
public sealed class DialogGraphBuilderTests
{
    /// <summary>
    /// The three markers that make the flow readable. The completion is the trickiest: a question
    /// without an outgoing transition is the <b>regular</b> end of the dialog (the
    /// <c>TransitionResolver</c> returns <see langword="null"/> there) – without a marker that reads
    /// like a missing edge.
    /// </summary>
    [Fact]
    public void Build_marks_the_entry_the_completion_and_unreachable_questions()
    {
        var dialog = TestDialogFactory.BuildBranchingDialog(Guid.NewGuid(), out var ids);
        var orphanId = Guid.NewGuid();
        dialog.Questions.Add(new Question
        {
            Id = orphanId,
            DialogId = dialog.Id,
            Key = "verwaist",
            Text = "Nie erreichbar",
            Type = QuestionType.FreeText,
            Order = 9,
        });

        var model = DialogGraphBuilder.Build(AdminProjection.ToDetail(dialog));

        var start = model.Node(ids.RoleQuestionId)!;
        Assert.True(start.IsStart);
        Assert.False(start.IsTerminal);
        Assert.False(start.IsUnreachable);

        var terminal = model.Node(ids.DevQuestionId)!;
        Assert.True(terminal.IsTerminal);
        Assert.False(terminal.IsStart);

        var orphan = model.Node(orphanId)!;
        Assert.True(orphan.IsUnreachable);
        Assert.Contains(
            orphan.Warnings,
            warning => warning.Text.Contains("Not reachable", StringComparison.Ordinal));
    }

    /// <summary>
    /// Without an entry question, reachability cannot be determined at all. The finding therefore
    /// belongs on the dialog – and explicitly <b>not</b> on every node, otherwise the whole graph
    /// would be red although only a single field is missing.
    /// </summary>
    [Fact]
    public void Build_warns_at_dialog_level_when_the_entry_question_is_missing()
    {
        var dialog = TestDialogFactory.BuildBranchingDialog(Guid.NewGuid(), out _);
        dialog.StartQuestionId = null;

        var model = DialogGraphBuilder.Build(AdminProjection.ToDetail(dialog));

        Assert.Contains(
            model.DialogWarnings,
            warning => warning.Kind == GraphElementKind.Dialog
                && warning.Text.Contains("No entry question", StringComparison.Ordinal));
        Assert.All(model.Nodes, node => Assert.False(node.IsUnreachable));
    }

    /// <summary>
    /// The core of the acceptance criterion "warnings appear at the affected element": the group
    /// warning sits on the node, the edge warning on the edge – and none gets lost.
    /// </summary>
    [Fact]
    public void Build_places_transition_warnings_on_the_node_and_on_the_edge()
    {
        var dialog = TestDialogFactory.BuildBranchingDialog(Guid.NewGuid(), out var ids);

        // The formerly conditional transition becomes unconditional and thereby shadows the default behind it.
        var blocking = dialog.Transitions.First(transition => !transition.IsDefault);
        blocking.Expression = null;

        var model = DialogGraphBuilder.Build(AdminProjection.ToDetail(dialog));

        Assert.Contains(
            model.Edge(blocking.Id)!.Warnings,
            warning => warning.Text.Contains("always matches", StringComparison.Ordinal));

        // And the group warning stays on the node: several defaults are nobody's individual fault.
        dialog.Transitions.First(transition => transition.IsDefault).IsDefault = true;
        var second = dialog.Transitions.First(transition => !transition.IsDefault);
        second.IsDefault = true;

        var withTwoDefaults = DialogGraphBuilder.Build(AdminProjection.ToDetail(dialog));

        Assert.Contains(
            withTwoDefaults.Node(ids.RoleQuestionId)!.Warnings,
            warning => warning.Text.Contains("Multiple default transitions", StringComparison.Ordinal));
    }

    /// <summary>
    /// The loop findings spread across two places: what concerns the marker hangs on the frame, what
    /// concerns the breaking question hangs on its node.
    /// </summary>
    [Fact]
    public void Build_places_the_loop_findings_on_the_frame_and_on_the_breaking_question()
    {
        var dialog = TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out var ids);
        dialog.Transitions.Remove(
            dialog.Transitions.First(transition => transition.TargetQuestionId == ids.SummaryQuestionId));
        dialog.Loops.First().CollectionKey = "meine-positionen";

        var model = DialogGraphBuilder.Build(AdminProjection.ToDetail(dialog));

        var frame = Assert.Single(model.Loops);
        Assert.Contains(
            frame.Warnings,
            warning => warning.Text.Contains("not referenceable", StringComparison.Ordinal));

        Assert.Contains(
            model.Node(ids.MoreQuestionId)!.Warnings,
            warning => warning.Text.Contains("infinite loop", StringComparison.Ordinal));
    }

    /// <summary>The frame encloses exactly the nodes of the range computed by the <c>LoopAnalyzer</c>.</summary>
    [Fact]
    public void Build_frames_the_LoopAnalyzer_body_as_a_bounding_box()
    {
        var dialog = TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out var ids);
        var model = DialogGraphBuilder.Build(AdminProjection.ToDetail(dialog));

        var frame = Assert.Single(model.Loops);
        Assert.Equal("positions", frame.CollectionKey);

        foreach (var id in new[] { ids.PositionQuestionId, ids.MoreQuestionId })
        {
            var node = model.Node(id)!;
            Assert.True(node.X >= frame.X, $"{node.Key} liegt links des Rahmens.");
            Assert.True(node.Y >= frame.Y, $"{node.Key} lies above the frame.");
            Assert.True(node.X + GraphMetrics.NodeWidth <= frame.X + frame.Width);
            Assert.True(node.Y + GraphMetrics.NodeHeight <= frame.Y + frame.Height);
            Assert.True(node.InLoop);
        }

        // The question behind the exit does not belong to it and must not be framed.
        var outside = model.Node(ids.SummaryQuestionId)!;
        Assert.False(outside.InLoop);
        Assert.True(outside.Y > frame.Y + frame.Height);

        Assert.True(model.Node(ids.PositionQuestionId)!.IsLoopEntry);
        Assert.True(model.Node(ids.MoreQuestionId)!.IsLoopBreaking);
    }

    /// <summary>
    /// A marker whose questions were deleted has no range – then there is no frame, only the warning.
    /// A frame around nothing would be worse than none.
    /// </summary>
    [Fact]
    public void Build_draws_no_frame_for_a_marker_pointing_into_the_void()
    {
        var dialog = TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out _);
        dialog.Loops.First().EntryQuestionId = Guid.NewGuid();

        var frame = Assert.Single(DialogGraphBuilder.Build(AdminProjection.ToDetail(dialog)).Loops);

        Assert.Equal(0, frame.Width);
        Assert.Equal(0, frame.Height);
        Assert.NotEmpty(frame.Warnings);
    }

    /// <summary>
    /// Triggers hang where they fire: <c>AfterQuestion</c> on the node, everything else on the scope
    /// markers. <c>AfterAnswer</c> has no natural place and deliberately lands on the start marker –
    /// hung on every node it would show the same configuration many times over.
    /// </summary>
    [Fact]
    public void Build_hangs_triggers_on_the_question_or_on_the_scope_markers()
    {
        var dialog = TestDialogFactory.BuildBranchingDialog(Guid.NewGuid(), out var ids);
        dialog.Triggers.Add(Trigger(dialog.Id, TriggerScope.AfterQuestion, ids.RoleQuestionId));
        dialog.Triggers.Add(Trigger(dialog.Id, TriggerScope.OnDialogStarted, null));
        dialog.Triggers.Add(Trigger(dialog.Id, TriggerScope.AfterAnswer, null));
        dialog.Triggers.Add(Trigger(dialog.Id, TriggerScope.OnDialogCompleted, null));

        var model = DialogGraphBuilder.Build(AdminProjection.ToDetail(dialog));

        var chip = Assert.Single(model.Node(ids.RoleQuestionId)!.Triggers);
        Assert.Contains("Webhook", chip.Label, StringComparison.Ordinal);
        Assert.Empty(model.Node(ids.DevQuestionId)!.Triggers);

        Assert.Equal(2, model.StartMarker!.Triggers.Count);
        Assert.Single(model.EndMarker!.Triggers);

        // The markers lie outside the node area – the drawing surface grows upwards and downwards.
        Assert.True(model.MinY < 0);
        Assert.True(model.Height > GraphLayout.Compute(AdminProjection.ToDetail(dialog)).Height);
    }

    /// <summary>Without such triggers there are no markers either – the canvas stays free of empty shapes.</summary>
    [Fact]
    public void Build_shows_no_markers_without_scope_triggers()
    {
        var model = DialogGraphBuilder.Build(
            AdminProjection.ToDetail(TestDialogFactory.BuildBranchingDialog(Guid.NewGuid(), out _)));

        Assert.Null(model.StartMarker);
        Assert.Null(model.EndMarker);
        Assert.Equal(0, model.MinY);
    }

    /// <summary>
    /// Transitions and triggers pointing at deleted questions cannot be drawn. They do not disappear
    /// silently but are reported separately – like the existing hint in the list view.
    /// </summary>
    [Fact]
    public void Build_reports_orphaned_transitions_and_triggers_separately()
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
        dialog.Triggers.Add(Trigger(dialog.Id, TriggerScope.AfterQuestion, Guid.NewGuid()));

        var model = DialogGraphBuilder.Build(AdminProjection.ToDetail(dialog));

        Assert.Single(model.OrphanTransitions);
        Assert.Single(model.OrphanTriggers);
        Assert.Equal(2, model.Edges.Count);
        Assert.All(model.Nodes, node => Assert.Empty(node.Triggers));
    }

    /// <summary>
    /// For screen readers the <c>aria-label</c> is a node's only rendition. Everything that otherwise
    /// exists only as a colour or a position has to appear in it.
    /// </summary>
    [Fact]
    public void Build_describes_every_node_completely_in_words()
    {
        var dialog = TestDialogFactory.BuildBranchingDialog(Guid.NewGuid(), out var ids);
        var model = DialogGraphBuilder.Build(AdminProjection.ToDetail(dialog));

        var start = model.Node(ids.RoleQuestionId)!.AriaLabel;
        Assert.Contains("Question role", start, StringComparison.Ordinal);
        Assert.Contains("Single choice", start, StringComparison.Ordinal);
        Assert.Contains("required", start, StringComparison.Ordinal);
        Assert.Contains("2 answer options", start, StringComparison.Ordinal);
        Assert.Contains("entry question", start, StringComparison.Ordinal);
        Assert.Contains("2 outgoing transitions", start, StringComparison.Ordinal);

        var terminal = model.Node(ids.PmQuestionId)!.AriaLabel;
        Assert.Contains("terminal, no outgoing transition", terminal, StringComparison.Ordinal);
        Assert.Contains("optional", terminal, StringComparison.Ordinal);

        // Edges carry their full statement too – they are not focusable, but they are readable aloud.
        Assert.All(model.Edges, edge => Assert.Contains("Transition", edge.AriaLabel, StringComparison.Ordinal));
    }

    /// <summary>The summary replaces the picture for everyone who cannot see it.</summary>
    [Fact]
    public void Build_summarizes_the_graph_in_words()
    {
        var dialog = TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out _);

        var summary = DialogGraphBuilder.Build(AdminProjection.ToDetail(dialog)).Summary;

        Assert.Contains("3 questions", summary, StringComparison.Ordinal);
        Assert.Contains("3 transitions", summary, StringComparison.Ordinal);
        Assert.Contains("1 loop", summary, StringComparison.Ordinal);
        Assert.Contains("no warnings", summary, StringComparison.Ordinal);
    }

    /// <summary>
    /// The edge label names the evaluation position – the same 1-based counting as the list view's
    /// "#" column, which the warning texts refer to as well.
    /// </summary>
    [Fact]
    public void Build_labels_edges_with_the_condition_and_the_evaluation_position()
    {
        var dialog = TestDialogFactory.BuildBranchingDialog(Guid.NewGuid(), out _);
        var model = DialogGraphBuilder.Build(AdminProjection.ToDetail(dialog));

        var conditional = model.Edges.Single(edge => !edge.IsDefault);
        Assert.Equal(1, conditional.Position);
        Assert.Contains("role ==", conditional.Label, StringComparison.Ordinal);

        var fallback = model.Edges.Single(edge => edge.IsDefault);
        Assert.Equal(2, fallback.Position);
        Assert.Equal("Default", fallback.Label);
    }

    /// <summary>
    /// The "back jump" badge follows the <b>list order</b> – the same statement as in the
    /// <c>DialogEditor</c>, so that list and graph do not claim different things. The layout's drawing
    /// form is a different question (layering) and must not bleed through here.
    /// </summary>
    [Fact]
    public void Build_marks_back_jumps_by_the_list_order()
    {
        var dialog = TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out var ids);
        var loopBack = dialog.Transitions.First(
            transition => transition.TargetQuestionId == ids.PositionQuestionId);

        var model = DialogGraphBuilder.Build(AdminProjection.ToDetail(dialog));

        Assert.True(model.Edge(loopBack.Id)!.IsBackJump);
        Assert.All(
            model.Edges.Where(edge => edge.TransitionId != loopBack.Id),
            edge => Assert.False(edge.IsBackJump));
    }

    /// <summary>
    /// The same dialog yields the same model – the determinism promise carries this far.
    /// </summary>
    /// <remarks>
    /// What is compared are the scalar values, not the records themselves: a <c>record</c> compares
    /// its collection properties over <c>EqualityComparer&lt;T&gt;.Default</c>, so for lists over the
    /// <b>reference</b>. Two calls inevitably produce different list instances; a direct
    /// <c>Assert.Equal</c> on the nodes would therefore check object identity instead of arrangement.
    /// </remarks>
    [Fact]
    public void Build_returns_the_same_result_for_the_same_dialog()
    {
        var detail = AdminProjection.ToDetail(TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out _));

        var first = DialogGraphBuilder.Build(detail);
        var second = DialogGraphBuilder.Build(detail);

        Assert.Equal(
            first.Nodes.Select(node => (node.QuestionId, node.X, node.Y, node.AriaLabel)),
            second.Nodes.Select(node => (node.QuestionId, node.X, node.Y, node.AriaLabel)));
        Assert.Equal(
            first.Edges.Select(edge => (edge.TransitionId, edge.Path, edge.LabelX, edge.LabelY, edge.Label)),
            second.Edges.Select(edge => (edge.TransitionId, edge.Path, edge.LabelX, edge.LabelY, edge.Label)));
        Assert.Equal(
            first.Loops.Select(loop => (loop.LoopId, loop.X, loop.Y, loop.Width, loop.Height)),
            second.Loops.Select(loop => (loop.LoopId, loop.X, loop.Y, loop.Width, loop.Height)));
        Assert.Equal(first.Summary, second.Summary);
    }

    /// <summary>An empty dialog yields an empty but valid model – the page must not fail.</summary>
    [Fact]
    public void Build_tolerates_a_dialog_without_questions()
    {
        var dialog = TestDialogFactory.NewDialog("leer", 1, "Leer");

        var model = DialogGraphBuilder.Build(AdminProjection.ToDetail(dialog));

        Assert.Empty(model.Nodes);
        Assert.Empty(model.Edges);
        Assert.Empty(model.DialogWarnings);
        Assert.Contains("0 questions", model.Summary, StringComparison.Ordinal);
    }

    /// <summary>
    /// The loop frame follows a moved node: it arises as a bounding box over the body's positions, not
    /// over the layering. If it did not follow along, a node of its own loop would lie outside that
    /// loop's frame.
    /// </summary>
    [Fact]
    public void Build_stretches_the_loop_frame_over_the_stored_position()
    {
        var detail = AdminProjection.ToDetail(TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out var ids));
        var before = Assert.Single(DialogGraphBuilder.Build(detail).Loops);

        var moved = detail with
        {
            Layout =
            [
                new DialogLayoutDetail(
                    Guid.NewGuid(), detail.Dialog.Id, LayoutElementKind.Question,
                    ids.MoreQuestionId, 1200, 900),
            ],
        };

        var model = DialogGraphBuilder.Build(moved);
        var frame = Assert.Single(model.Loops);

        Assert.True(frame.Width > before.Width);
        Assert.True(frame.X + frame.Width >= 1200 + GraphMetrics.NodeWidth);
        Assert.True(frame.Y + frame.Height >= 900 + GraphMetrics.NodeHeight);

        // And the node knows about its own position – the marking in the card hangs on that.
        Assert.True(model.Node(ids.MoreQuestionId)!.IsPinned);
        Assert.Contains("own position", model.Node(ids.MoreQuestionId)!.AriaLabel, StringComparison.Ordinal);
    }

    private static TriggerDefinition Trigger(Guid dialogId, TriggerScope scope, Guid? questionId)
        => new()
        {
            Id = Guid.NewGuid(),
            DialogId = dialogId,
            Scope = scope,
            QuestionId = questionId,
            Kind = TriggerKind.Webhook,
            Config = """{"url":"https://example.test/hook"}""",
        };
}
