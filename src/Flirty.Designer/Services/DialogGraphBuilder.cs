using Flirty.Designer.Models;
using Flirty.Domain;
using Flirty.Runtime.Admin;

namespace Flirty.Designer.Services;

/// <summary>
/// Baut aus dem geladenen Dialog das fertige Zeichenmodell der Graph-Ansicht (#101): Knoten mit ihren
/// Markern, Kanten mit ihren Beschriftungen, Schleifen als Bereichsrahmen, Trigger als Anhängsel – und
/// jede Warnung an dem Element, das sie verursacht.
/// </summary>
/// <remarks>
/// <para>
/// Der Builder erfindet keine Regel: Die Übergangs-Befunde kommen aus dem
/// <see cref="TransitionWarningAnalyzer"/> (derselbe, den die Listenansicht nutzt), die Schleifen-Befunde
/// und der Body aus dem <see cref="LoopAnalyzer"/>, die Geometrie aus <see cref="GraphLayout"/>. Eigene
/// Befunde sind nur die beiden, die es erst im Graphen gibt: die fehlende Einstiegsfrage und die von
/// dort nicht erreichbaren Fragen.
/// </para>
/// <para>
/// <b>Es gibt keine impliziten Kanten.</b> Hat eine Frage keinen ausgehenden Übergang, endet der Dialog
/// dort – <c>TransitionResolver.ResolveTransitionTarget</c> liefert <see langword="null"/>, es gibt kein
/// „weiter mit der nächsten Frage nach <c>Order</c>“. Was auf dem Canvas zu sehen ist, ist damit der
/// vollständige Ablauf; <c>Order</c> bleibt reine Sortierung der Listenansicht.
/// </para>
/// </remarks>
internal static class DialogGraphBuilder
{
    /// <summary>Höchstlänge des Fragetexts auf einer Knotenkarte.</summary>
    private const int NodeTextLength = 64;

    /// <summary>Höchstlänge einer Kantenbeschriftung.</summary>
    private const int EdgeLabelLength = 30;

    /// <summary>Höchstlänge der Zielangabe eines Trigger-Chips.</summary>
    private const int ChipTargetLength = 28;

    /// <summary>Höhe eines Scope-Markers samt Abstand zum Graphen in px.</summary>
    private const double MarkerBand = 64;

    /// <summary>Baut das Zeichenmodell.</summary>
    /// <param name="detail">Der Dialog samt Graph (aus <c>GetDialogQuery</c>).</param>
    /// <returns>Das fertige Modell.</returns>
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
                "Keine Einstiegsfrage gesetzt – ohne sie lässt sich der Dialog nicht starten, und die "
                + "Erreichbarkeit der Fragen ist nicht bestimmbar."));
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

    // ---- Knoten -------------------------------------------------------------------------------------

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

        // layout.Nodes ist nach Schicht und Spalte sortiert – die Reihenfolge wird unverändert
        // übernommen, weil sie zugleich die Tab-Reihenfolge im Browser ist: Tabben läuft den Dialog von
        // oben nach unten durch.
        foreach (var position in layout.Nodes)
        {
            var question = questions[position.QuestionId];
            var isStart = detail.Dialog.StartQuestionId == question.Id;
            var isTerminal = !outgoingCount.ContainsKey(question.Id);

            var nodeWarnings = warnings
                .Where(warning => warning.Kind == GraphElementKind.Question
                    && warning.ElementId == question.Id)
                .ToList();

            // Unerreichbarkeit ist erst im Graphen sichtbar – die Listenansicht kennt den Befund nicht.
            if (position is { IsReachable: false })
            {
                nodeWarnings.Add(GraphWarning.ForQuestion(
                    question.Id,
                    "Von der Einstiegsfrage aus nicht erreichbar – kein Pfad über Übergänge führt hierher. "
                    + "Die Frage wird zur Laufzeit nie gestellt."));
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

        // Sonderfall: keine Fragen, aber eine Einstiegsfrage-Warnung wäre irreführend detailliert.
        if (detail.Questions.Count == 0)
        {
            dialogWarnings.Clear();
        }

        return nodes;
    }

    /// <summary>
    /// Beschreibt einen Knoten vollständig in Worten. Nicht dekorativ: Für Screenreader ist das die
    /// einzige Fassung des Knotens, und alles, was nur als Farbe oder Position vorliegt, fehlte sonst.
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
            $"Frage {question.Key}",
            QuestionTypeLabels.Describe(question.Type),
            question.IsRequired ? "Pflichtfrage" : "optional",
        };

        if (QuestionTypeLabels.UsesOptions(question.Type))
        {
            parts.Add(Count(question.Options.Count, "Antwortoption", "Antwortoptionen"));
        }

        if (isStart)
        {
            parts.Add("Einstiegsfrage");
        }

        if (isUnreachable)
        {
            parts.Add("nicht erreichbar");
        }

        parts.Add(isTerminal
            ? "Abschluss, kein ausgehender Übergang"
            : Count(outgoing, "ausgehender Übergang", "ausgehende Übergänge"));

        if (triggers > 0)
        {
            parts.Add(Count(triggers, "Trigger", "Trigger"));
        }

        if (isPinned)
        {
            parts.Add("eigene Position");
        }

        if (warnings > 0)
        {
            parts.Add(Count(warnings, "Warnung", "Warnungen"));
        }

        return string.Join(", ", parts) + ".";
    }

    // ---- Kanten -------------------------------------------------------------------------------------

    private static IReadOnlyList<GraphEdge> BuildEdges(
        DialogDetail detail,
        GraphLayoutResult layout,
        IReadOnlyList<GraphWarning> warnings,
        IReadOnlyDictionary<Guid, QuestionDetail> questions)
    {
        var transitions = detail.Transitions.ToDictionary(transition => transition.Id);

        // Die Auswertungsposition ist 1-basiert je Ausgangsfrage – dieselbe Zählung, die die
        // Listenansicht in ihrer Spalte „#“ zeigt und auf die sich die Warntexte beziehen.
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
                ? transition.IsDefault ? "Default" : "bedingungslos"
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
            ? transition.IsDefault ? "Default-Übergang" : "bedingungslos"
            : $"Bedingung {transition.Expression}";

        return $"Übergang {position} von {from.Key} nach {target.Key}, {condition}.";
    }

    // ---- Schleifen ----------------------------------------------------------------------------------

    private static IReadOnlyList<GraphLoopFrame> BuildLoops(
        IReadOnlyList<LoopInsight> insights, GraphLayoutResult layout)
    {
        var positions = layout.Nodes.ToDictionary(node => node.QuestionId);
        var frames = new List<GraphLoopFrame>(insights.Count);

        for (var index = 0; index < insights.Count; index++)
        {
            var insight = insights[index];

            // insight.Body ist bereits in Dialog-Reihenfolge – bewusst nicht über die Menge aus
            // LoopAnalyzer.ComputeBody iterieren, deren Reihenfolge nicht zugesichert ist.
            var boxes = insight.Body
                .Where(question => positions.ContainsKey(question.Id))
                .Select(question => positions[question.Id])
                .ToArray();

            if (boxes.Length == 0)
            {
                // Kaputter Marker: kein Rahmen, nur die Warnung. Ein Rahmen um nichts wäre schlimmer
                // als keiner.
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

    // ---- Scope-Marker -------------------------------------------------------------------------------

    /// <summary>
    /// Legt die Marker für die Trigger an, die an keiner einzelnen Frage hängen.
    /// </summary>
    /// <remarks>
    /// <c>AfterAnswer</c> hat keinen natürlichen Ort im Graphen – der Trigger feuert nach <b>jeder</b>
    /// Antwort, gehört also an keinen Knoten. Er landet zusammen mit <c>OnDialogStarted</c> am
    /// Start-Marker, unterscheidbar über die Beschriftung des Chips. Die Alternative, ihn an jeden
    /// einzelnen Knoten zu hängen, würde den Canvas zumüllen und dieselbe Konfiguration vielfach zeigen.
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

        // Die Marker liegen außerhalb der vom Layout berechneten Fläche – oben darüber, unten darunter.
        // Statt die Knoten zu verschieben, wächst die Zeichenfläche: Das hält die Koordinaten des
        // Layouts unangetastet und damit die Determinismus-Zusage einfach nachvollziehbar.
        GraphScopeMarker? start = startTriggers.Length == 0
            ? null
            : new GraphScopeMarker(
                "Dialogstart",
                (nodes.FirstOrDefault(node => node.IsStart) ?? nodes.FirstOrDefault())?.X ?? GraphMetrics.MarginX,
                GraphMetrics.MarginY - MarkerBand,
                startTriggers);

        GraphScopeMarker? end = endTriggers.Length == 0
            ? null
            : new GraphScopeMarker(
                "Dialogabschluss",
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
            ? "bedingungslos"
            : $"Bedingung {trigger.Expression}";

        return new GraphTriggerChip(
            trigger.Id,
            $"{channel}: {Shorten(target, ChipTargetLength)}",
            $"{scope} · {TriggerLabels.Describe(trigger.Kind)} · {target} · {condition}",
            trigger.Kind);
    }

    // ---- Zusammenfassung ----------------------------------------------------------------------------

    /// <summary>
    /// Fasst den Graphen in einem Satz zusammen. Steht als versteckter Text vor dem Canvas, damit ein
    /// Screenreader nicht erst 30 Knoten durchlaufen muss, um zu wissen, worum es geht.
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
            Count(nodes.Count, "Frage", "Fragen"),
            Count(edges.Count, "Übergang", "Übergänge"),
        };

        if (loops.Count > 0)
        {
            parts.Add(Count(loops.Count, "Schleife", "Schleifen"));
        }

        if (detail.Triggers.Count > 0)
        {
            parts.Add(Count(detail.Triggers.Count, "Trigger", "Trigger"));
        }

        parts.Add(total == 0 ? "keine Warnungen" : Count(total, "Warnung", "Warnungen"));

        return $"Graph des Dialogs {detail.Dialog.Key}: {string.Join(", ", parts)}.";
    }

    private static string Count(int value, string singular, string plural)
        => $"{value} {(value == 1 ? singular : plural)}";

    /// <summary>Kürzt einen Text auf die Anzeigelänge; der vollständige Text bleibt im Tooltip.</summary>
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
