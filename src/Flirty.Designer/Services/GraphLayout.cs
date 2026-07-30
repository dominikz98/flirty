using Flirty.Designer.Models;
using Flirty.Domain;
using Flirty.Runtime.Admin;

namespace Flirty.Designer.Services;

/// <summary>
/// Lays out the dialog graph automatically – a "Sugiyama-light": layering via breadth-first search from
/// the entry question, back edges broken up, crossings reduced via barycenter.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why built in-house:</b> dagre and ELK are JS libraries and have no widespread equivalent in .NET;
/// the repo deliberately does not want a Node toolchain (ADR 0006). The alternative would have been to
/// show the graph unarranged – without stored positions (which #102 brings first) that is no
/// alternative.
/// </para>
/// <para>
/// <b>Determinism here is not a comfort but a requirement.</b> The same graph must yield the same
/// coordinates, otherwise E2E selectors and screenshots wobble later. This is secured in
/// three places: only <b>lists in fixed order</b> are returned (never a set or a dictionary whose
/// iteration order is not guaranteed); the sort keys end with a <b>unique</b> ordinal, so they are a total
/// order and do not rely on the stability of <c>OrderBy</c>; and coordinates arise only from integer
/// layer/column values, never from a barycenter – floating-point averages determine the order, not the
/// position.
/// </para>
/// <para>
/// The ordinal of a question comes from <c>(Order, Id)</c> and <b>not</b> from the Guid alone:
/// <c>CreateDialogVersionCommand</c> assigns each question a new Guid on cloning, so a Guid-based
/// layout would reshuffle on every new dialog version. <c>Order</c> survives cloning and
/// is moreover the order chosen by the author.
/// </para>
/// <para>
/// <b>Without dummy nodes.</b> A full Sugiyama draws chains of placeholders through
/// skipped layers. They would only be needed here for back-jumps – and those visually do not belong
/// between the nodes anyway, but in a lane at the edge. For the target size of around 30 nodes
/// dummy chains save not a single crossing, but cost a second node kind in the model, in the rendering
/// and in the selection.
/// </para>
/// </remarks>
internal static class GraphLayout
{
    /// <summary>How often the layers are sorted through to reduce crossings.</summary>
    /// <remarks>
    /// A fixed number instead of stopping on convergence: a convergence criterion on floating-point values
    /// would be the kind of condition that comes out differently on another machine.
    /// </remarks>
    private const int SweepCount = 4;

    /// <summary>Corner radius of the back-jump lanes in px.</summary>
    private const double CornerRadius = 12;

    /// <summary>Lays out the graph of a dialog.</summary>
    /// <param name="detail">The dialog including graph (from <c>GetDialogQuery</c>).</param>
    /// <returns>The geometry of all nodes and edges.</returns>
    public static GraphLayoutResult Compute(DialogDetail detail)
    {
        ArgumentNullException.ThrowIfNull(detail);

        if (detail.Questions.Count == 0)
        {
            // The minimum area is not cosmetics: onto an empty dialog the first question is dragged from
            // the palette (#103) – an 80 × 80 px area would be no target.
            return new GraphLayoutResult([], [], 0, GraphMetrics.MinCanvasWidth, GraphMetrics.MinCanvasHeight);
        }

        var order = Normalize(detail);
        var layers = AssignLayers(detail, order);
        var shapes = ClassifyEdges(order, layers);
        var slots = Arrange(detail, order, layers, shapes, out var crossings);
        var pinned = Pinned(detail, order);

        return Render(order, layers, slots, shapes, crossings, pinned);
    }

    /// <summary>
    /// Reads the positions stored by the author (<c>DialogLayout</c>) from the dialog.
    /// </summary>
    /// <remarks>
    /// <para>
    /// They take effect only at the very end, in <see cref="Render"/>: layering, edge shape, barycenter and
    /// lane assignment stay with the auto-layout. A moved node thus changes <b>only its
    /// position</b> – never the drawing shape of an edge and never the arrangement of the other nodes.
    /// Otherwise a single drag could resort the whole graph.
    /// </para>
    /// <para>
    /// The determinism guarantee holds: the same input – graph <i>and</i> layout rows – yields
    /// the same coordinates.
    /// </para>
    /// </remarks>
    private static Dictionary<Guid, (double X, double Y)> Pinned(DialogDetail detail, GraphOrder order)
    {
        var pinned = new Dictionary<Guid, (double X, double Y)>();

        foreach (var entry in detail.Layout)
        {
            // Only questions are nodes today; a row on a deleted question has no target.
            if (entry.ElementKind != LayoutElementKind.Question || !order.Ordinal.ContainsKey(entry.ElementId))
            {
                continue;
            }

            // Negative values are rejected by the command; a hand-written record would otherwise come to
            // lie outside the drawing surface and would be unreachable.
            pinned[entry.ElementId] = (Math.Max(0, entry.X), Math.Max(0, entry.Y));
        }

        return pinned;
    }

    // ---- Step 0: normalization ----------------------------------------------------------------------

    /// <summary>
    /// Brings questions and transitions into a unique, repeatable order – the basis of every
    /// later determinism guarantee.
    /// </summary>
    private static GraphOrder Normalize(DialogDetail detail)
    {
        // (Order, Id): Order is the author order and survives cloning a dialog version,
        // the Id breaks the tie that the designer itself never produces.
        var questions = detail.Questions
            .OrderBy(question => question.Order)
            .ThenBy(question => question.Id)
            .ToArray();

        var ordinal = new Dictionary<Guid, int>(questions.Length);
        for (var index = 0; index < questions.Length; index++)
        {
            ordinal[questions[index].Id] = index;
        }

        // Only drawable edges. The global Priority sort from DialogDetail is not enough as an
        // order: with equal Priority across different source questions it is arbitrary.
        var edges = detail.Transitions
            .Where(transition =>
                ordinal.ContainsKey(transition.FromQuestionId)
                && ordinal.ContainsKey(transition.TargetQuestionId))
            .OrderBy(transition => ordinal[transition.FromQuestionId])
            .ThenBy(transition => transition.Priority)
            .ThenBy(transition => ordinal[transition.TargetQuestionId])
            .ThenBy(transition => transition.Id)
            .ToArray();

        return new GraphOrder(questions, ordinal, edges);
    }

    // ---- Step 1: layering ---------------------------------------------------------------------------

    /// <summary>
    /// Layers the graph via breadth-first search from the entry question. Because the breadth-first search
    /// finds shortest paths, for every edge within a component <c>layer[target] ≤ layer[source] + 1</c>
    /// holds – so a forward edge never skips a layer.
    /// </summary>
    private static LayerAssignment AssignLayers(DialogDetail detail, GraphOrder order)
    {
        var layer = new Dictionary<Guid, int>(order.Questions.Length);
        var reachable = new HashSet<Guid>();
        var hasStart = detail.Dialog.StartQuestionId is { } start && order.Ordinal.ContainsKey(start);

        var outgoing = order.Edges
            .GroupBy(edge => edge.FromQuestionId)
            .ToDictionary(group => group.Key, group => group.Select(edge => edge.TargetQuestionId).ToArray());

        if (hasStart)
        {
            Explore(detail.Dialog.StartQuestionId!.Value, 0);
            foreach (var visited in layer.Keys)
            {
                reachable.Add(visited);
            }
        }
        else
        {
            // Without an entry question there is no reference point for "reachable". Then all questions
            // would be marked red, even though only one piece of information is missing – the finding
            // belongs on the dialog, not on every node. Roots here are the questions that nothing points to.
            var targets = order.Edges.Select(edge => edge.TargetQuestionId).ToHashSet();
            var roots = order.Questions.Where(question => !targets.Contains(question.Id)).ToArray();

            foreach (var root in roots.Length > 0 ? roots : [order.Questions[0]])
            {
                Explore(root.Id, 0);
            }

            foreach (var question in order.Questions)
            {
                reachable.Add(question.Id);
            }
        }

        // What is still missing now hangs on no path from the entry question. It goes behind the graph,
        // separated by an empty layer – the band is the visual statement "does not belong".
        var offset = (layer.Count == 0 ? -2 : layer.Values.Max()) + 2;
        foreach (var question in order.Questions)
        {
            if (!layer.ContainsKey(question.Id))
            {
                Explore(question.Id, offset);
            }
        }

        return new LayerAssignment(layer, reachable);

        void Explore(Guid root, int baseLayer)
        {
            if (!layer.TryAdd(root, baseLayer))
            {
                return;
            }

            var queue = new Queue<Guid>();
            queue.Enqueue(root);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (!outgoing.TryGetValue(current, out var neighbours))
                {
                    continue;
                }

                foreach (var neighbour in neighbours)
                {
                    if (layer.TryAdd(neighbour, layer[current] + 1))
                    {
                        queue.Enqueue(neighbour);
                    }
                }
            }
        }
    }

    // ---- Step 2: break up back edges ----------------------------------------------------------------

    /// <summary>
    /// Classifies the edges by their drawing shape. Only <see cref="GraphEdgeShape.Forward"/> edges
    /// between adjacent layers take part in the arrangement – back-jumps are removed from the acyclic
    /// edge set, otherwise the cycle would pull the arrangement in on itself.
    /// </summary>
    private static Dictionary<Guid, GraphEdgeShape> ClassifyEdges(GraphOrder order, LayerAssignment layers)
    {
        var shapes = new Dictionary<Guid, GraphEdgeShape>(order.Edges.Length);

        foreach (var edge in order.Edges)
        {
            var from = layers.Layer[edge.FromQuestionId];
            var to = layers.Layer[edge.TargetQuestionId];

            shapes[edge.Id] = edge.FromQuestionId == edge.TargetQuestionId
                ? GraphEdgeShape.SelfLoop
                : to < from
                    ? GraphEdgeShape.BackJump
                    : to == from
                        ? GraphEdgeShape.Flat
                        : GraphEdgeShape.Forward;
        }

        return shapes;
    }

    // ---- Step 3: barycenter -------------------------------------------------------------------------

    /// <summary>
    /// Reorders the nodes within their layers so that as few edges as possible cross:
    /// each node moves to the mean ("barycenter") of its neighbours in the respectively fixed
    /// adjacent layer.
    /// </summary>
    private static List<Guid>[] Arrange(
        DialogDetail detail,
        GraphOrder order,
        LayerAssignment layers,
        IReadOnlyDictionary<Guid, GraphEdgeShape> shapes,
        out int crossings)
    {
        var layerCount = layers.Layer.Values.Max() + 1;
        var slots = new List<Guid>[layerCount];
        for (var index = 0; index < layerCount; index++)
        {
            slots[index] = [];
        }

        // Start order is the discovery order of the breadth-first search, expressed via the
        // ordinal – reproducible without running the search again.
        foreach (var question in order.Questions)
        {
            slots[layers.Layer[question.Id]].Add(question.Id);
        }

        // Only edges between immediately adjacent layers take part in the arrangement; everything else has
        // no defined place in the intermediate layer in the dummy-free variant.
        var adjacent = order.Edges
            .Where(edge => shapes[edge.Id] == GraphEdgeShape.Forward
                && layers.Layer[edge.TargetQuestionId] == layers.Layer[edge.FromQuestionId] + 1)
            .ToArray();

        var above = adjacent
            .GroupBy(edge => edge.TargetQuestionId)
            .ToDictionary(group => group.Key, group => group.Select(edge => edge.FromQuestionId).ToArray());
        var below = adjacent
            .GroupBy(edge => edge.FromQuestionId)
            .ToDictionary(group => group.Key, group => group.Select(edge => edge.TargetQuestionId).ToArray());

        var loopIndex = LoopIndexes(detail, order);

        var best = Snapshot(slots);
        crossings = CountCrossings(slots, adjacent, layers);

        for (var sweep = 0; sweep < SweepCount; sweep++)
        {
            var downwards = sweep % 2 == 0;

            for (var step = 1; step < layerCount; step++)
            {
                var index = downwards ? step : layerCount - 1 - step;
                var neighbours = downwards ? above : below;
                var fixedSlots = Positions(slots[downwards ? index - 1 : index + 1]);

                Reorder(slots[index], neighbours, fixedSlots, loopIndex, order.Ordinal);
            }

            var current = CountCrossings(slots, adjacent, layers);
            if (current < crossings)
            {
                crossings = current;
                best = Snapshot(slots);
            }
        }

        return best;
    }

    /// <summary>
    /// Orders a layer by the barycenter of its nodes. The sort key ends with the
    /// unique ordinal and is thus a total order – two calls cannot come out
    /// differently.
    /// </summary>
    private static void Reorder(
        List<Guid> layer,
        IReadOnlyDictionary<Guid, Guid[]> neighbours,
        IReadOnlyDictionary<Guid, int> fixedSlots,
        IReadOnlyDictionary<Guid, int> loopIndex,
        IReadOnlyDictionary<Guid, int> ordinal)
    {
        var previous = Positions(layer);

        var sorted = layer
            .OrderBy(id => Barycenter(id, neighbours, fixedSlots, previous))
            // Questions of the same loop stay together – otherwise the range frame would later span
            // across foreign nodes.
            .ThenBy(id => loopIndex.TryGetValue(id, out var index) ? index : int.MaxValue)
            .ThenBy(id => previous[id])
            .ThenBy(id => ordinal[id])
            .ToArray();

        layer.Clear();
        layer.AddRange(sorted);
    }

    private static double Barycenter(
        Guid id,
        IReadOnlyDictionary<Guid, Guid[]> neighbours,
        IReadOnlyDictionary<Guid, int> fixedSlots,
        IReadOnlyDictionary<Guid, int> previous)
    {
        if (!neighbours.TryGetValue(id, out var linked))
        {
            return previous[id];
        }

        var sum = 0d;
        var count = 0;
        foreach (var neighbour in linked)
        {
            if (fixedSlots.TryGetValue(neighbour, out var slot))
            {
                sum += slot;
                count++;
            }
        }

        return count == 0 ? previous[id] : sum / count;
    }

    /// <summary>
    /// Counts the crossings between adjacent layers – the inversions of the target positions when
    /// the edges are read by their source position. At the target size of around 30 nodes the
    /// quadratic effort is negligible; the value makes the arrangement measurable instead of merely
    /// claimed.
    /// </summary>
    private static int CountCrossings(
        IReadOnlyList<List<Guid>> slots, IReadOnlyList<TransitionDetail> adjacent, LayerAssignment layers)
    {
        var positions = new Dictionary<Guid, int>();
        foreach (var layer in slots)
        {
            for (var index = 0; index < layer.Count; index++)
            {
                positions[layer[index]] = index;
            }
        }

        var crossings = 0;

        foreach (var group in adjacent.GroupBy(edge => layers.Layer[edge.FromQuestionId]))
        {
            var pairs = group
                .Select(edge => (From: positions[edge.FromQuestionId], To: positions[edge.TargetQuestionId]))
                .ToArray();

            for (var i = 0; i < pairs.Length; i++)
            {
                for (var k = i + 1; k < pairs.Length; k++)
                {
                    var left = pairs[i];
                    var right = pairs[k];
                    if ((left.From < right.From && left.To > right.To)
                        || (left.From > right.From && left.To < right.To))
                    {
                        crossings++;
                    }
                }
            }
        }

        return crossings;
    }

    /// <summary>Assigns each question the loop in whose range it lies.</summary>
    private static Dictionary<Guid, int> LoopIndexes(DialogDetail detail, GraphOrder order)
    {
        var loopIndex = new Dictionary<Guid, int>();

        for (var index = 0; index < detail.Loops.Count; index++)
        {
            var body = LoopAnalyzer.ComputeBody(detail, detail.Loops[index]);

            // Project over the ordered question list, do not iterate over the set: the
            // iteration order of a HashSet is not guaranteed.
            foreach (var question in order.Questions)
            {
                if (body.Contains(question.Id))
                {
                    loopIndex.TryAdd(question.Id, index);
                }
            }
        }

        return loopIndex;
    }

    private static Dictionary<Guid, int> Positions(IReadOnlyList<Guid> layer)
    {
        var positions = new Dictionary<Guid, int>(layer.Count);
        for (var index = 0; index < layer.Count; index++)
        {
            positions[layer[index]] = index;
        }

        return positions;
    }

    private static List<Guid>[] Snapshot(IReadOnlyList<List<Guid>> slots)
        => [.. slots.Select(layer => new List<Guid>(layer))];

    // ---- Steps 4 and 5: coordinates and edge routing ------------------------------------------------

    private static GraphLayoutResult Render(
        GraphOrder order,
        LayerAssignment layers,
        IReadOnlyList<List<Guid>> slots,
        IReadOnlyDictionary<Guid, GraphEdgeShape> shapes,
        int crossings,
        IReadOnlyDictionary<Guid, (double X, double Y)> pinned)
    {
        var widest = slots.Max(layer => layer.Count);
        var nodes = new List<GraphNodePosition>(order.Questions.Length);
        var boxes = new Dictionary<Guid, (double X, double Y)>(order.Questions.Length);

        for (var layerIndex = 0; layerIndex < slots.Count; layerIndex++)
        {
            var layer = slots[layerIndex];
            for (var slot = 0; slot < layer.Count; slot++)
            {
                var id = layer[slot];

                // The layer is centered. All factors are integers, PitchX is even –
                // no decimal places arise from a division.
                var x = GraphMetrics.MarginX
                    + ((widest - layer.Count) * GraphMetrics.PitchX / 2)
                    + (slot * GraphMetrics.PitchX);
                var y = GraphMetrics.MarginY + (layerIndex * GraphMetrics.PitchY);

                // The stored position beats the computed one – and only here, so that it is picked up by
                // the edge routing below, but did not influence the arrangement above.
                var isPinned = pinned.TryGetValue(id, out var saved);
                boxes[id] = isPinned ? saved : (x, y);

                nodes.Add(new GraphNodePosition(
                    id, layerIndex, slot, boxes[id].X, boxes[id].Y, layers.Reachable.Contains(id), isPinned));
            }
        }

        // The drawing surface must also fit moved nodes: the lane of the back-jumps lies to the right
        // of everything that is drawn – otherwise it would run across a widely stretched node.
        var contentWidth = Math.Max(
            GraphMetrics.MarginX + (widest * GraphMetrics.PitchX) - GraphMetrics.GapX,
            boxes.Values.Max(box => box.X) + GraphMetrics.NodeWidth);

        var lanes = AssignLanes(order, layers, shapes);

        // The lower bound also applies to a small graph: a dialog with two questions should still have room
        // to drop the third (#103). Both constants are integers – the determinism guarantee
        // stays unaffected.
        var width = Math.Max(
            GraphMetrics.MinCanvasWidth,
            contentWidth + GraphMetrics.MarginX + (lanes.Count * GraphMetrics.GutterStep));
        var height = Math.Max(
            GraphMetrics.MinCanvasHeight,
            Math.Max(
                GraphMetrics.MarginY + (slots.Count * GraphMetrics.PitchY) - GraphMetrics.GapY,
                boxes.Values.Max(box => box.Y) + GraphMetrics.NodeHeight) + GraphMetrics.MarginY);

        var fans = order.Edges
            .GroupBy(edge => (edge.FromQuestionId, edge.TargetQuestionId))
            .ToDictionary(group => group.Key, group => group.Select(edge => edge.Id).ToArray());

        var edges = new List<GraphEdgeRoute>(order.Edges.Length);
        foreach (var edge in order.Edges)
        {
            var fan = fans[(edge.FromQuestionId, edge.TargetQuestionId)];
            var fanIndex = Array.IndexOf(fan, edge.Id);
            var offset = (fanIndex - ((fan.Length - 1) / 2.0)) * GraphMetrics.FanStep;

            edges.Add(Route(
                edge,
                shapes[edge.Id],
                boxes[edge.FromQuestionId],
                boxes[edge.TargetQuestionId],
                offset,
                fanIndex,
                fan.Length,
                lanes.TryGetValue(edge.Id, out var lane) ? contentWidth + (lane + 1) * GraphMetrics.GutterStep : 0));
        }

        return new GraphLayoutResult(nodes, edges, crossings, width, height);
    }

    /// <summary>
    /// Assigns the lanes to the right of the graph to the back-jumps – the longest one outermost, so that
    /// the lanes do not intersect.
    /// </summary>
    private static Dictionary<Guid, int> AssignLanes(
        GraphOrder order, LayerAssignment layers, IReadOnlyDictionary<Guid, GraphEdgeShape> shapes)
    {
        var backJumps = order.Edges
            .Where(edge => shapes[edge.Id] == GraphEdgeShape.BackJump)
            .OrderByDescending(edge =>
                layers.Layer[edge.FromQuestionId] - layers.Layer[edge.TargetQuestionId])
            .ThenBy(edge => order.Ordinal[edge.FromQuestionId])
            .ThenBy(edge => edge.Priority)
            .ThenBy(edge => edge.Id)
            .ToArray();

        var lanes = new Dictionary<Guid, int>(backJumps.Length);
        for (var index = 0; index < backJumps.Length; index++)
        {
            lanes[backJumps[index].Id] = index;
        }

        return lanes;
    }

    private static GraphEdgeRoute Route(
        TransitionDetail edge,
        GraphEdgeShape shape,
        (double X, double Y) from,
        (double X, double Y) to,
        double offset,
        int fanIndex,
        int fanCount,
        double laneX)
    {
        const double half = GraphMetrics.NodeWidth / 2;
        const double middle = GraphMetrics.NodeHeight / 2;

        switch (shape)
        {
            case GraphEdgeShape.Forward:
            {
                var sx = from.X + half + offset;
                var sy = from.Y + GraphMetrics.NodeHeight;
                var tx = to.X + half + offset;
                var ty = to.Y;

                // The bend radius must not exceed half the span: with two adjacent
                // layers there is only GapY between the nodes, a fixed value would push the
                // control points past each other and the edge would run as an S-loop.
                var bend = Math.Min(GraphMetrics.EdgeBend, Math.Abs(ty - sy) / 2);

                var path = $"M {N(sx)} {N(sy)} C {N(sx)} {N(sy + bend)} "
                    + $"{N(tx)} {N(ty - bend)} {N(tx)} {N(ty)}";

                // The midpoint of a cubic Bézier with vertical control points lies exactly
                // between start and target – no approximation needed.
                return new GraphEdgeRoute(edge.Id, shape, path, (sx + tx) / 2, (sy + ty) / 2, fanIndex, fanCount);
            }

            case GraphEdgeShape.Flat:
            {
                var leftToRight = from.X <= to.X;
                var sx = leftToRight ? from.X + GraphMetrics.NodeWidth : from.X;
                var tx = leftToRight ? to.X : to.X + GraphMetrics.NodeWidth;
                var sy = from.Y + middle + offset;
                var ty = to.Y + middle + offset;
                var bend = Math.Max(GraphMetrics.NodeHeight, Math.Abs(tx - sx) / 3);
                var cx = (sx + tx) / 2;
                var cy = ((sy + ty) / 2) + bend;

                var path = $"M {N(sx)} {N(sy)} Q {N(cx)} {N(cy)} {N(tx)} {N(ty)}";

                // Midpoint of the quadratic Bézier: (P0 + 2·P1 + P2) / 4.
                return new GraphEdgeRoute(
                    edge.Id, shape, path, (sx + (2 * cx) + tx) / 4, (sy + (2 * cy) + ty) / 4, fanIndex, fanCount);
            }

            case GraphEdgeShape.SelfLoop:
            {
                var sx = from.X + GraphMetrics.NodeWidth;
                var sy = from.Y + (GraphMetrics.NodeHeight * 0.3);
                var ty = from.Y + (GraphMetrics.NodeHeight * 0.7);
                var reach = sx + 46 + Math.Abs(offset);

                var path = $"M {N(sx)} {N(sy)} C {N(reach)} {N(sy - 18)} {N(reach)} {N(ty + 18)} {N(sx)} {N(ty)}";

                return new GraphEdgeRoute(edge.Id, shape, path, reach - 6, (sy + ty) / 2, fanIndex, fanCount);
            }

            default:
            {
                // Back-jumps run up past the graph in the lane to the right. Drawn between the nodes
                // they would obscure the flow that the canvas is meant to show.
                var sx = from.X + GraphMetrics.NodeWidth;
                var sy = from.Y + middle + offset;
                var tx = to.X + GraphMetrics.NodeWidth;
                var ty = to.Y + middle + offset;
                var lane = laneX + Math.Abs(offset);
                var radius = Math.Min(CornerRadius, Math.Abs(sy - ty) / 2);

                var path = $"M {N(sx)} {N(sy)} H {N(lane - radius)} Q {N(lane)} {N(sy)} {N(lane)} {N(sy - radius)} "
                    + $"V {N(ty + radius)} Q {N(lane)} {N(ty)} {N(lane - radius)} {N(ty)} H {N(tx)}";

                return new GraphEdgeRoute(edge.Id, shape, path, lane, (sy + ty) / 2, fanIndex, fanCount);
            }
        }
    }

    private static string N(double value) => SvgFormat.N(value);

    /// <summary>Questions and edges in unique order, with the ordinal per question.</summary>
    private sealed record GraphOrder(
        QuestionDetail[] Questions,
        Dictionary<Guid, int> Ordinal,
        TransitionDetail[] Edges);

    /// <summary>The layer per question and the set of questions reachable from the entry question.</summary>
    private sealed record LayerAssignment(Dictionary<Guid, int> Layer, HashSet<Guid> Reachable);
}
