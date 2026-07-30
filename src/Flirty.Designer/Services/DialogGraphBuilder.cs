using Flirty.Designer.Models;
using Flirty.Domain;
using Flirty.Runtime.Admin;

namespace Flirty.Designer.Services;

/// <summary>
/// Builds the finished drawing model of the graph view (#101) from the loaded dialog: nodes with their
/// markers, edges with their labels, loops as range frames, triggers as chips – and every warning at the
/// element that causes it.
/// </summary>
/// <remarks>
/// <para>
/// The builder invents no rule: the transition findings come from the
/// <see cref="TransitionWarningAnalyzer"/> (the same one the list view uses), the loop findings and the
/// body from the <see cref="LoopAnalyzer"/>, the geometry from <see cref="GraphLayout"/>. Its own findings
/// are only the two that first arise in the graph: the missing entry question and the questions not
/// reachable from it.
/// </para>
/// <para>
/// <b>There are no implicit edges.</b> If a question has no outgoing transition, the dialog ends there –
/// <c>TransitionResolver.ResolveTransitionTarget</c> returns <see langword="null"/>, there is no "continue
/// with the next question by <c>Order</c>". What is visible on the canvas is therefore the complete flow;
/// <c>Order</c> remains pure sorting of the list view.
/// </para>
/// </remarks>
internal static class DialogGraphBuilder
{
    /// <summary>Maximum length of the question text on a node card.</summary>
    private const int NodeTextLength = 64;

    /// <summary>Maximum length of an edge label.</summary>
    private const int EdgeLabelLength = 30;

    /// <summary>Maximum length of the target of a trigger chip.</summary>
    private const int ChipTargetLength = 28;

    /// <summary>Height of a scope marker including its gap to the graph, in px.</summary>
    private const double MarkerBand = 64;

    /// <summary>Builds the drawing model.</summary>
    /// <param name="detail">The dialog including its graph (from <c>GetDialogQuery</c>).</param>
    /// <returns>The finished model.</returns>
    public static DialogGraphModel Build(DialogDetail detail)
    {
        ArgumentNullException.ThrowIfNull(detail);

        var layout = GraphLayout.Compute(detail);
        var insights = LoopAnalyzer.Analyze(detail);
        var questions = detail.Questions.ToDictionary(question => question.Id);

        var warnings = new List<GraphWarning>(TransitionWarningAnalyzer.Analyze(detail));
        foreach (var insight in insights)
        {
            warnings.AddRange(insight.TargetedWarnings);
        }

        var dialogWarnings = new List<GraphWarning>();
        if (detail.Dialog.StartQuestionId is null || !questions.ContainsKey(detail.Dialog.StartQuestionId.Value))
        {
            dialogWarnings.Add(GraphWarning.ForDialog(
                "No entry question set – without it the dialog cannot be started, and the reachability of "
                + "the questions cannot be determined."));
        }

        var nodes = BuildNodes(detail, layout, insights, warnings, dialogWarnings);
        var edges = BuildEdges(detail, layout, warnings, questions);
        var loops = BuildLoops(insights, layout);

        var orphanTransitions = detail.Transitions
            .Where(transition => !questions.ContainsKey(transition.FromQuestionId)
                || !questions.ContainsKey(transition.TargetQuestionId))
            .ToArray();

        var orphanTriggers = detail.Triggers
            .Where(trigger => trigger.QuestionId is { } id && !questions.ContainsKey(id))
            .ToArray();

        var (start, end, minY, height) = BuildMarkers(detail, layout, nodes);

        return new DialogGraphModel(
            detail.Dialog,
            nodes,
            edges,
            loops,
            start,
            end,
            dialogWarnings,
            orphanTransitions,
            orphanTriggers,
            Summarize(detail, nodes, edges, loops, dialogWarnings),
            minY,
            layout.Width,
            height);
    }

    // ---- Nodes --------------------------------------------------------------------------------------

    private static IReadOnlyList<GraphNode> BuildNodes(
        DialogDetail detail,
        GraphLayoutResult layout,
        IReadOnlyList<LoopInsight> insights,
        List<GraphWarning> warnings,
        List<GraphWarning> dialogWarnings)
    {
        var questions = detail.Questions.ToDictionary(question => question.Id);
        var outgoingCount = detail.Transitions
            .GroupBy(transition => transition.FromQuestionId)
            .ToDictionary(group => group.Key, group => group.Count());

        var entries = insights.Select(insight => insight.Loop.EntryQuestionId).ToHashSet();
        var breakings = insights.Select(insight => insight.Loop.BreakingQuestionId).ToHashSet();
        var inLoop = insights.SelectMany(insight => insight.Body).Select(question => question.Id).ToHashSet();

        var nodes = new List<GraphNode>(layout.Nodes.Count);

        // layout.Nodes is sorted by layer and column – the order is carried over unchanged, because it is
        // at the same time the tab order in the browser: tabbing walks the dialog from top to bottom.
        foreach (var position in layout.Nodes)
        {
            var question = questions[position.QuestionId];
            var isStart = detail.Dialog.StartQuestionId == question.Id;
            var isTerminal = !outgoingCount.ContainsKey(question.Id);

            var nodeWarnings = warnings
                .Where(warning => warning.Kind == GraphElementKind.Question
                    && warning.ElementId == question.Id)
                .ToList();

            // Unreachability arises only from the arrangement starting at the entry question, so here and
            // not in the TransitionWarningAnalyzer. Since #118 the list view draws from this too: its
            // publish confirmation reads the whole graph (GraphWarningList), so this finding is not
            // missing of all times right before publishing.
            if (position is { IsReachable: false })
            {
                nodeWarnings.Add(GraphWarning.ForQuestion(
                    question.Id,
                    "Not reachable from the entry question – no path via transitions leads here. The "
                    + "question is never asked at runtime."));
            }

            var triggers = detail.Triggers
                .Where(trigger => trigger.Scope == TriggerScope.AfterQuestion
                    && trigger.QuestionId == question.Id)
                .Select(Chip)
                .ToArray();

            nodes.Add(new GraphNode(
                question.Id,
                question.Key,
                Shorten(question.Text, NodeTextLength),
                question.Text,
                QuestionTypeLabels.Describe(question.Type),
                question.IsRequired,
                question.Options.Count,
                QuestionTypeLabels.UsesOptions(question.Type),
                position.X,
                position.Y,
                isStart,
                isTerminal,
                !position.IsReachable,
                entries.Contains(question.Id),
                breakings.Contains(question.Id),
                inLoop.Contains(question.Id),
                position.IsPinned,
                triggers,
                nodeWarnings,
                DescribeNode(
                    question,
                    isStart,
                    isTerminal,
                    !position.IsReachable,
                    position.IsPinned,
                    outgoingCount.GetValueOrDefault(question.Id),
                    triggers.Length,
                    nodeWarnings.Count)));
        }

        // Special case: no questions, but an entry-question warning would be misleadingly detailed.
        if (detail.Questions.Count == 0)
        {
            dialogWarnings.Clear();
        }

        return nodes;
    }

    /// <summary>
    /// Describes a node fully in words. Not decorative: for screen readers this is the only rendering of
    /// the node, and everything that exists only as color or position would otherwise be missing.
    /// </summary>
    private static string DescribeNode(
        QuestionDetail question,
        bool isStart,
        bool isTerminal,
        bool isUnreachable,
        bool isPinned,
        int outgoing,
        int triggers,
        int warnings)
    {
        var parts = new List<string>
        {
            $"Question {question.Key}",
            QuestionTypeLabels.Describe(question.Type),
            question.IsRequired ? "required" : "optional",
        };

        if (QuestionTypeLabels.UsesOptions(question.Type))
        {
            parts.Add(Count(question.Options.Count, "answer option", "answer options"));
        }

        if (isStart)
        {
            parts.Add("entry question");
        }

        if (isUnreachable)
        {
            parts.Add("not reachable");
        }

        parts.Add(isTerminal
            ? "terminal, no outgoing transition"
            : Count(outgoing, "outgoing transition", "outgoing transitions"));

        if (triggers > 0)
        {
            parts.Add(Count(triggers, "trigger", "triggers"));
        }

        if (isPinned)
        {
            parts.Add("own position");
        }

        if (warnings > 0)
        {
            parts.Add(Count(warnings, "warning", "warnings"));
        }

        return string.Join(", ", parts) + ".";
    }

    // ---- Edges --------------------------------------------------------------------------------------

    private static IReadOnlyList<GraphEdge> BuildEdges(
        DialogDetail detail,
        GraphLayoutResult layout,
        IReadOnlyList<GraphWarning> warnings,
        IReadOnlyDictionary<Guid, QuestionDetail> questions)
    {
        var transitions = detail.Transitions.ToDictionary(transition => transition.Id);

        // The evaluation position is 1-based per source question – the same count the list view shows in
        // its "#" column and that the warning texts refer to.
        var positions = new Dictionary<Guid, int>();
        foreach (var group in detail.Transitions.GroupBy(transition => transition.FromQuestionId))
        {
            var ordered = group.OrderBy(transition => transition.Priority).ToArray();
            for (var index = 0; index < ordered.Length; index++)
            {
                positions[ordered[index].Id] = index + 1;
            }
        }

        var order = detail.Questions.Select(question => question.Id).ToList();

        var edges = new List<GraphEdge>(layout.Edges.Count);
        foreach (var route in layout.Edges)
        {
            var transition = transitions[route.TransitionId];
            var target = questions[transition.TargetQuestionId];

            var edgeWarnings = warnings
                .Where(warning => warning.Kind == GraphElementKind.Transition
                    && warning.ElementId == transition.Id)
                .ToArray();

            var condition = string.IsNullOrWhiteSpace(transition.Expression)
                ? transition.IsDefault ? "Default" : "unconditional"
                : Shorten(transition.Expression, EdgeLabelLength);

            edges.Add(new GraphEdge(
                transition.Id,
                transition.FromQuestionId,
                transition.TargetQuestionId,
                route.Path,
                route.Shape,
                route.LabelX,
                route.LabelY,
                condition,
                positions[transition.Id],
                transition.IsDefault,
                LoopAnalyzer.IsBackJump(order, transition),
                edgeWarnings,
                DescribeEdge(transition, questions[transition.FromQuestionId], target, positions[transition.Id])));
        }

        return edges;
    }

    private static string DescribeEdge(
        TransitionDetail transition, QuestionDetail from, QuestionDetail target, int position)
    {
        var condition = string.IsNullOrWhiteSpace(transition.Expression)
            ? transition.IsDefault ? "default transition" : "unconditional"
            : $"condition {transition.Expression}";

        return $"Transition {position} from {from.Key} to {target.Key}, {condition}.";
    }

    // ---- Loops --------------------------------------------------------------------------------------

    private static IReadOnlyList<GraphLoopFrame> BuildLoops(
        IReadOnlyList<LoopInsight> insights, GraphLayoutResult layout)
    {
        var positions = layout.Nodes.ToDictionary(node => node.QuestionId);
        var frames = new List<GraphLoopFrame>(insights.Count);

        for (var index = 0; index < insights.Count; index++)
        {
            var insight = insights[index];

            // insight.Body is already in dialog order – deliberately not iterating over the set from
            // LoopAnalyzer.ComputeBody, whose order is not guaranteed.
            var boxes = insight.Body
                .Where(question => positions.ContainsKey(question.Id))
                .Select(question => positions[question.Id])
                .ToArray();

            if (boxes.Length == 0)
            {
                // Broken marker: no frame, only the warning. A frame around nothing would be worse than
                // none.
                frames.Add(new GraphLoopFrame(
                    insight.Loop.Id, insight.Loop.CollectionKey, 0, 0, 0, 0,
                    insight.EntryQuestion?.Key ?? string.Empty,
                    insight.BreakingQuestion?.Key ?? string.Empty,
                    insight.TargetedWarnings.Where(warning => warning.Kind == GraphElementKind.Loop).ToArray()));
                continue;
            }

            var padding = GraphMetrics.LoopFramePadding + (index * GraphMetrics.LoopFramePaddingStep);
            var x0 = boxes.Min(box => box.X) - padding;
            var y0 = boxes.Min(box => box.Y) - padding;
            var x1 = boxes.Max(box => box.X) + GraphMetrics.NodeWidth + padding;
            var y1 = boxes.Max(box => box.Y) + GraphMetrics.NodeHeight + padding;

            frames.Add(new GraphLoopFrame(
                insight.Loop.Id,
                insight.Loop.CollectionKey,
                x0,
                y0,
                x1 - x0,
                y1 - y0,
                insight.EntryQuestion?.Key ?? string.Empty,
                insight.BreakingQuestion?.Key ?? string.Empty,
                insight.TargetedWarnings.Where(warning => warning.Kind == GraphElementKind.Loop).ToArray()));
        }

        return frames;
    }

    // ---- Scope markers ------------------------------------------------------------------------------

    /// <summary>
    /// Creates the markers for the triggers that hang on no single question.
    /// </summary>
    /// <remarks>
    /// <c>AfterAnswer</c> has no natural place in the graph – the trigger fires after <b>every</b> answer,
    /// so it belongs to no node. It lands together with <c>OnDialogStarted</c> at the start marker,
    /// distinguishable via the chip's label. The alternative, hanging it on every single node, would
    /// clutter the canvas and show the same configuration many times over.
    /// </remarks>
    private static (GraphScopeMarker? Start, GraphScopeMarker? End, double MinY, double Height) BuildMarkers(
        DialogDetail detail, GraphLayoutResult layout, IReadOnlyList<GraphNode> nodes)
    {
        var startTriggers = detail.Triggers
            .Where(trigger => trigger.Scope is TriggerScope.OnDialogStarted or TriggerScope.AfterAnswer)
            .Select(Chip)
            .ToArray();

        var endTriggers = detail.Triggers
            .Where(trigger => trigger.Scope == TriggerScope.OnDialogCompleted)
            .Select(Chip)
            .ToArray();

        // The markers lie outside the area computed by the layout – above it at the top, below it at the
        // bottom. Instead of moving the nodes, the drawing surface grows: that keeps the layout's
        // coordinates untouched and thus makes the determinism promise easy to follow.
        GraphScopeMarker? start = startTriggers.Length == 0
            ? null
            : new GraphScopeMarker(
                "Dialog start",
                (nodes.FirstOrDefault(node => node.IsStart) ?? nodes.FirstOrDefault())?.X ?? GraphMetrics.MarginX,
                GraphMetrics.MarginY - MarkerBand,
                startTriggers);

        GraphScopeMarker? end = endTriggers.Length == 0
            ? null
            : new GraphScopeMarker(
                "Dialog completed",
                nodes.LastOrDefault()?.X ?? GraphMetrics.MarginX,
                layout.Height - GraphMetrics.MarginY,
                endTriggers);

        var minY = start is null ? 0 : -MarkerBand;
        var height = layout.Height - minY + (end is null ? 0 : MarkerBand);

        return (start, end, minY, height);
    }

    private static GraphTriggerChip Chip(TriggerDetail trigger)
    {
        var target = TriggerConfig.TryParse(trigger.Config, out var config, out _)
            ? config.Url ?? config.Name ?? "—"
            : trigger.Config;

        var channel = trigger.Kind == TriggerKind.Webhook ? "Webhook" : "In-Process";
        var scope = TriggerLabels.Describe(trigger.Scope);
        var condition = string.IsNullOrWhiteSpace(trigger.Expression)
            ? "unconditional"
            : $"condition {trigger.Expression}";

        return new GraphTriggerChip(
            trigger.Id,
            $"{channel}: {Shorten(target, ChipTargetLength)}",
            $"{scope} · {TriggerLabels.Describe(trigger.Kind)} · {target} · {condition}",
            trigger.Kind);
    }

    // ---- Summary ------------------------------------------------------------------------------------

    /// <summary>
    /// Summarizes the graph in a single sentence. Stands as hidden text before the canvas, so a screen
    /// reader does not have to walk 30 nodes first to know what it is about.
    /// </summary>
    private static string Summarize(
        DialogDetail detail,
        IReadOnlyList<GraphNode> nodes,
        IReadOnlyList<GraphEdge> edges,
        IReadOnlyList<GraphLoopFrame> loops,
        IReadOnlyList<GraphWarning> dialogWarnings)
    {
        var total = dialogWarnings.Count
            + nodes.Sum(node => node.Warnings.Count)
            + edges.Sum(edge => edge.Warnings.Count)
            + loops.Sum(loop => loop.Warnings.Count);

        var parts = new List<string>
        {
            Count(nodes.Count, "question", "questions"),
            Count(edges.Count, "transition", "transitions"),
        };

        if (loops.Count > 0)
        {
            parts.Add(Count(loops.Count, "loop", "loops"));
        }

        if (detail.Triggers.Count > 0)
        {
            parts.Add(Count(detail.Triggers.Count, "trigger", "triggers"));
        }

        parts.Add(total == 0 ? "no warnings" : Count(total, "warning", "warnings"));

        return $"Graph of dialog {detail.Dialog.Key}: {string.Join(", ", parts)}.";
    }

    private static string Count(int value, string singular, string plural)
        => $"{value} {(value == 1 ? singular : plural)}";

    /// <summary>Shortens a text to the display length; the full text stays in the tooltip.</summary>
    private static string Shorten(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var trimmed = text.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..(max - 1)].TrimEnd() + "…";
    }
}
