using System.Globalization;
using Flirty.Designer.Models;
using Flirty.Designer.Services;
using Flirty.Domain;
using Flirty.Runtime.Admin;
using Flirty.Tests.Persistence;

namespace Flirty.Tests.Designer;

/// <summary>
/// Tests for the auto-layout of the graph view (#101). The emphasis is on <b>determinism</b>: the
/// same graph has to yield the same coordinates, otherwise E2E selectors and screenshots wobble. That
/// is checked against the three sources non-determinism usually seeps in from – hash iteration order,
/// newly assigned Guids and the storage order of the transitions.
/// </summary>
public sealed class GraphLayoutTests
{
    /// <summary>
    /// Two calls on the same input have to be congruent. The test catches every place where a
    /// <c>HashSet</c> or <c>Dictionary</c> iteration bleeds through into the result.
    /// </summary>
    [Fact]
    public void Compute_returns_the_same_coordinates_for_the_same_graph()
    {
        var detail = AdminProjection.ToDetail(TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out _));

        var first = GraphLayout.Compute(detail);
        var second = GraphLayout.Compute(detail);

        Assert.Equal(first.Nodes, second.Nodes);
        Assert.Equal(first.Edges, second.Edges);
        Assert.Equal(first.Crossings, second.Crossings);
        Assert.Equal(first.Width, second.Width);
        Assert.Equal(first.Height, second.Height);
    }

    /// <summary>
    /// The layout must not hang on the Guids: when cloning, <c>CreateDialogVersionCommand</c> assigns
    /// <b>every</b> question a new Guid (ADR 0005 – the only way to evolve a published dialog). A
    /// guid-based layout would therefore reshuffle with every new version.
    /// </summary>
    [Fact]
    public void Compute_does_not_depend_on_the_Guids()
    {
        var first = Layout(AdminProjection.ToDetail(TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out _)));
        var second = Layout(AdminProjection.ToDetail(TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out _)));

        Assert.Equal(first, second);

        static (int Layer, int Slot, double X, double Y)[] Layout(DialogDetail detail)
        {
            var result = GraphLayout.Compute(detail);
            var byOrder = detail.Questions.ToDictionary(question => question.Id, question => question.Order);

            return
            [
                .. result.Nodes
                    .OrderBy(node => byOrder[node.QuestionId])
                    .Select(node => (node.Layer, node.Slot, node.X, node.Y))
            ];
        }
    }

    /// <summary>
    /// <c>DialogDetail.Transitions</c> is sorted globally by <c>Priority</c> – with equal priorities
    /// in different source questions the order is therefore arbitrary. The layout must not be
    /// impressed by that.
    /// </summary>
    [Fact]
    public void Compute_is_independent_of_the_global_transition_order()
    {
        var detail = Branching();
        var reversed = detail with { Transitions = [.. detail.Transitions.Reverse()] };

        var expected = GraphLayout.Compute(detail);
        var actual = GraphLayout.Compute(reversed);

        Assert.Equal(expected.Nodes, actual.Nodes);
        Assert.Equal(
            expected.Edges.OrderBy(edge => edge.TransitionId),
            actual.Edges.OrderBy(edge => edge.TransitionId));
    }

    /// <summary>The entry question sits on layer 0, its successors one layer below.</summary>
    [Fact]
    public void Compute_layers_starting_from_the_entry_question()
    {
        var dialog = TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out var ids);
        var result = GraphLayout.Compute(AdminProjection.ToDetail(dialog));

        Assert.Equal(0, Node(result, ids.PositionQuestionId).Layer);
        Assert.Equal(1, Node(result, ids.MoreQuestionId).Layer);
        Assert.Equal(2, Node(result, ids.SummaryQuestionId).Layer);
    }

    /// <summary>
    /// A loop's back jump is recognized as such and kept out of the layering – otherwise the cycle
    /// would pull the entry question behind its own breaking question.
    /// </summary>
    [Fact]
    public void Compute_breaks_backward_edges_open()
    {
        var dialog = TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out var ids);
        var loopBack = dialog.Transitions.First(
            transition => transition.TargetQuestionId == ids.PositionQuestionId);

        var result = GraphLayout.Compute(AdminProjection.ToDetail(dialog));

        Assert.Equal(
            GraphEdgeShape.BackJump,
            result.Edges.Single(edge => edge.TransitionId == loopBack.Id).Shape);
        Assert.Equal(0, Node(result, ids.PositionQuestionId).Layer);
    }

    /// <summary>
    /// Questions with no path from the entry question sit behind the reachable graph – separated by
    /// an empty layer, so that the band is readable as such.
    /// </summary>
    [Fact]
    public void Compute_arranges_unreachable_questions_behind_the_reachable_ones()
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

        var result = GraphLayout.Compute(AdminProjection.ToDetail(dialog));

        var orphan = Node(result, orphanId);
        var deepest = Node(result, ids.DevQuestionId).Layer;

        Assert.False(orphan.IsReachable);
        Assert.True(orphan.Layer >= deepest + 2, $"Erwartet mindestens {deepest + 2}, war {orphan.Layer}.");
        Assert.True(Node(result, ids.RoleQuestionId).IsReachable);
    }

    /// <summary>
    /// Without an entry question set there is no reference point for reachability. Marking all
    /// questions as "unreachable" would be misleading – the finding belongs on the dialog.
    /// </summary>
    [Fact]
    public void Compute_without_an_entry_question_marks_no_question_as_unreachable()
    {
        var detail = Branching();
        var headless = detail with { Dialog = detail.Dialog with { StartQuestionId = null } };

        var result = GraphLayout.Compute(headless);

        Assert.All(result.Nodes, node => Assert.True(node.IsReachable));
        Assert.Equal(3, result.Nodes.Count);
    }

    /// <summary>Two nodes must never overlap – otherwise one hides the other.</summary>
    [Fact]
    public void Compute_overlaps_no_nodes()
    {
        var dialog = TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out _);
        var result = GraphLayout.Compute(AdminProjection.ToDetail(dialog));

        foreach (var left in result.Nodes)
        {
            foreach (var right in result.Nodes.Where(candidate => candidate.QuestionId != left.QuestionId))
            {
                var apart = left.X + GraphMetrics.NodeWidth <= right.X
                    || right.X + GraphMetrics.NodeWidth <= left.X
                    || left.Y + GraphMetrics.NodeHeight <= right.Y
                    || right.Y + GraphMetrics.NodeHeight <= left.Y;

                Assert.True(apart, $"Nodes at ({left.X}|{left.Y}) and ({right.X}|{right.Y}) overlap.");
            }
        }
    }

    /// <summary>
    /// Several transitions between the same two questions have to stay distinguishable: they are
    /// fanned out and get separate anchor points for their labels. Congruent edges would simply be
    /// one on the canvas.
    /// </summary>
    [Fact]
    public void Compute_fans_out_multiple_edges_between_the_same_nodes()
    {
        var detail = Branching();
        var first = detail.Transitions[0];
        var parallel = first with { Id = Guid.NewGuid(), Expression = "role == \"other\"", Priority = 5 };
        var withParallel = detail with { Transitions = [.. detail.Transitions, parallel] };

        var result = GraphLayout.Compute(withParallel);

        var left = result.Edges.Single(edge => edge.TransitionId == first.Id);
        var right = result.Edges.Single(edge => edge.TransitionId == parallel.Id);

        Assert.Equal(2, left.FanCount);
        Assert.Equal(2, right.FanCount);
        Assert.NotEqual(left.FanIndex, right.FanIndex);
        Assert.NotEqual(left.Path, right.Path);
        Assert.NotEqual(left.LabelX, right.LabelX);
    }

    /// <summary>
    /// The crossing reduction has to take effect: in pure author order the edges <c>a -&gt; d</c> and
    /// <c>b -&gt; c</c> cross; the barycenter turns the lower layer around and resolves that.
    /// </summary>
    [Fact]
    public void Compute_reduces_crossings()
    {
        var result = GraphLayout.Compute(CrossingGraph(out var ids));

        Assert.Equal(0, result.Crossings);

        // The lower layer stands against the author order – that is exactly the resorting.
        Assert.True(Node(result, ids.D).Slot < Node(result, ids.C).Slot);
    }

    /// <summary>The consistent loop dialog gets by without a single crossing.</summary>
    [Fact]
    public void Compute_arranges_the_loop_dialog_without_crossings()
    {
        var dialog = TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out _);

        Assert.Equal(0, GraphLayout.Compute(AdminProjection.ToDetail(dialog)).Crossings);
    }

    /// <summary>A dialog without questions yields an empty but valid drawing surface.</summary>
    [Fact]
    public void Compute_tolerates_a_dialog_without_questions()
    {
        var detail = Branching() with { Questions = [], Transitions = [] };

        var result = GraphLayout.Compute(detail);

        Assert.Empty(result.Nodes);
        Assert.Empty(result.Edges);
        Assert.True(result.Width > 0);
        Assert.True(result.Height > 0);
    }

    /// <summary>
    /// The drawing surface never falls below the minimum dimensions – not even for an empty or tiny
    /// graph.
    /// </summary>
    /// <remarks>
    /// Since #103 the canvas is a <b>drop surface</b>: the first building block is dragged onto it
    /// from the palette. Before the lower bound, an empty dialog's surface was 80 x 80 px – no target
    /// one can point at, and that is exactly where every new dialog begins.
    /// </remarks>
    [Fact]
    public void Compute_keeps_the_minimum_surface()
    {
        var empty = GraphLayout.Compute(Branching() with { Questions = [], Transitions = [] });
        var small = GraphLayout.Compute(Branching());

        Assert.Equal(GraphMetrics.MinCanvasWidth, empty.Width);
        Assert.Equal(GraphMetrics.MinCanvasHeight, empty.Height);
        Assert.True(small.Width >= GraphMetrics.MinCanvasWidth);
        Assert.True(small.Height >= GraphMetrics.MinCanvasHeight);
    }

    /// <summary>
    /// Transitions to deleted questions cannot be drawn – they have no node to start from and
    /// therefore drop out of the layout (not out of the display: the builder reports them separately).
    /// </summary>
    [Fact]
    public void Compute_skips_transitions_to_unknown_questions()
    {
        var detail = Branching();
        var orphan = detail.Transitions[0] with { Id = Guid.NewGuid(), TargetQuestionId = Guid.NewGuid() };
        var withOrphan = detail with { Transitions = [.. detail.Transitions, orphan] };

        var result = GraphLayout.Compute(withOrphan);

        Assert.DoesNotContain(result.Edges, edge => edge.TransitionId == orphan.Id);
        Assert.Equal(detail.Transitions.Count, result.Edges.Count);
    }

    /// <summary>
    /// The display culture is configurable (<c>DesignerApp.DisplayCulture</c>). Under any
    /// comma-decimal culture, a coordinate with a decimal <b>comma</b> silently takes an SVG path
    /// apart – the comma is a separator there. There would be neither an exception nor a message,
    /// only a wrong picture.
    /// </summary>
    [Fact]
    public void Paths_carry_a_decimal_point_even_under_de_DE()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            Assert.Equal("12.5", SvgFormat.N(12.5));

            var result = GraphLayout.Compute(CrossingGraph(out _));

            Assert.All(result.Edges, edge => Assert.DoesNotContain(',', edge.Path));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    // ---- Stored positions (#102) --------------------------------------------------------------------

    /// <summary>
    /// A stored position beats the computed one – that is the whole purpose of the
    /// <c>DialogLayout</c> table. The remaining nodes stay where the auto-layout put them.
    /// </summary>
    [Fact]
    public void A_stored_position_overrides_the_auto_layout()
    {
        var detail = AdminProjection.ToDetail(TestDialogFactory.BuildBranchingDialog(Guid.NewGuid(), out var ids));
        var automatic = GraphLayout.Compute(detail);

        var pinned = GraphLayout.Compute(WithLayout(detail, (ids.RoleQuestionId, 700, 500)));

        var node = Node(pinned, ids.RoleQuestionId);
        Assert.True(node.IsPinned);
        Assert.Equal(700, node.X);
        Assert.Equal(500, node.Y);

        // Structure unchanged: only the position moves, not the layer.
        Assert.Equal(Node(automatic, ids.RoleQuestionId).Layer, node.Layer);

        var untouched = Node(pinned, ids.DevQuestionId);
        Assert.False(untouched.IsPinned);
        Assert.Equal(Node(automatic, ids.DevQuestionId).X, untouched.X);
        Assert.Equal(Node(automatic, ids.DevQuestionId).Y, untouched.Y);
    }

    /// <summary>
    /// The edges follow along. Without that the node would sit somewhere other than its connections –
    /// the actual reason why the positions feed into the layout and not only into the drawing model.
    /// </summary>
    [Fact]
    public void Edges_follow_the_stored_position()
    {
        var detail = AdminProjection.ToDetail(TestDialogFactory.BuildBranchingDialog(Guid.NewGuid(), out var ids));
        var automatic = GraphLayout.Compute(detail);

        var pinned = GraphLayout.Compute(WithLayout(detail, (ids.DevQuestionId, 900, 600)));

        var incoming = detail.Transitions.Single(
            transition => transition.TargetQuestionId == ids.DevQuestionId);

        var before = automatic.Edges.Single(edge => edge.TransitionId == incoming.Id);
        var after = pinned.Edges.Single(edge => edge.TransitionId == incoming.Id);

        Assert.NotEqual(before.Path, after.Path);
        Assert.Equal(before.Shape, after.Shape);

        // The path ends at the top edge of the moved node: x = 900 + half the node width.
        Assert.EndsWith(
            $"{SvgFormat.N(900 + (GraphMetrics.NodeWidth / 2))} {SvgFormat.N(600)}",
            after.Path,
            StringComparison.Ordinal);
    }

    /// <summary>Without a row everything stays with the auto-layout – the way back is "no row".</summary>
    [Fact]
    public void Without_a_layout_row_the_auto_layout_applies()
    {
        var detail = AdminProjection.ToDetail(TestDialogFactory.BuildBranchingDialog(Guid.NewGuid(), out var ids));

        var pinned = GraphLayout.Compute(WithLayout(detail, (ids.RoleQuestionId, 700, 500)));
        var reset = GraphLayout.Compute(detail with { Layout = [] });
        var automatic = GraphLayout.Compute(detail);

        Assert.NotEqual(Node(pinned, ids.RoleQuestionId).X, Node(reset, ids.RoleQuestionId).X);
        Assert.Equal(automatic.Nodes, reset.Nodes);
        Assert.Equal(automatic.Edges, reset.Edges);
        Assert.DoesNotContain(reset.Nodes, node => node.IsPinned);
    }

    /// <summary>
    /// The drawing surface grows to include a node dragged far out. Otherwise it would lie outside
    /// the <c>viewBox</c> and be reachable only via the keyboard.
    /// </summary>
    [Fact]
    public void The_drawing_surface_grows_around_a_node_dragged_far_out()
    {
        var detail = AdminProjection.ToDetail(TestDialogFactory.BuildBranchingDialog(Guid.NewGuid(), out var ids));
        var automatic = GraphLayout.Compute(detail);

        var pinned = GraphLayout.Compute(WithLayout(detail, (ids.DevQuestionId, 2000, 1500)));

        Assert.True(pinned.Width > automatic.Width);
        Assert.True(pinned.Height > automatic.Height);
        Assert.True(pinned.Width >= 2000 + GraphMetrics.NodeWidth);
        Assert.True(pinned.Height >= 1500 + GraphMetrics.NodeHeight);
    }

    /// <summary>
    /// A row for a question that (no longer) exists is passed over. It must neither throw nor inflate
    /// the drawing surface – the cleanup branch in <c>DeleteQuestionCommand</c> is the rule, this
    /// check is the belt.
    /// </summary>
    [Fact]
    public void A_position_without_a_question_is_passed_over()
    {
        var detail = AdminProjection.ToDetail(TestDialogFactory.BuildBranchingDialog(Guid.NewGuid(), out _));
        var automatic = GraphLayout.Compute(detail);

        var withOrphan = GraphLayout.Compute(WithLayout(detail, (Guid.NewGuid(), 5000, 5000)));

        Assert.Equal(automatic.Nodes, withOrphan.Nodes);
        Assert.Equal(automatic.Width, withOrphan.Width);
        Assert.Equal(automatic.Height, withOrphan.Height);
    }

    /// <summary>Sets stored positions on a dialog.</summary>
    private static DialogDetail WithLayout(DialogDetail detail, params (Guid ElementId, int X, int Y)[] entries)
        => detail with
        {
            Layout =
            [
                .. entries.Select(entry => new DialogLayoutDetail(
                    Guid.NewGuid(), detail.Dialog.Id, LayoutElementKind.Question,
                    entry.ElementId, entry.X, entry.Y)),
            ],
        };

    private static GraphNodePosition Node(GraphLayoutResult result, Guid questionId)
        => result.Nodes.Single(node => node.QuestionId == questionId);

    private static DialogDetail Branching()
        => AdminProjection.ToDetail(TestDialogFactory.BuildBranchingDialog(Guid.NewGuid(), out _));

    /// <summary>
    /// A graph whose author order inevitably produces a crossing: <c>root</c> branches to <c>a</c>
    /// and <c>b</c>, <c>a</c> leads to the <b>second</b> leaf and <c>b</c> to the first.
    /// </summary>
    private static DialogDetail CrossingGraph(out (Guid A, Guid B, Guid C, Guid D) ids)
    {
        var dialogId = Guid.NewGuid();
        var root = Guid.NewGuid();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var d = Guid.NewGuid();
        ids = (a, b, c, d);

        QuestionDetail Question(Guid id, string key, int order)
            => new(id, dialogId, key, $"Frage {key}", QuestionType.FreeText, order, false, null, []);

        TransitionDetail Edge(Guid from, Guid target)
            => new(Guid.NewGuid(), dialogId, from, target, null, 0, true);

        return new DialogDetail(
            new DialogSummary(
                dialogId, "crossing", "Kreuzung", null, 1, false, root,
                TestDialogFactory.SampleTime, TestDialogFactory.SampleTime),
            [
                Question(root, "root", 0),
                Question(a, "a", 1),
                Question(b, "b", 2),
                Question(c, "c", 3),
                Question(d, "d", 4),
            ],
            [Edge(root, a), Edge(root, b), Edge(a, d), Edge(b, c)],
            [],
            [],
            []);
    }
}
