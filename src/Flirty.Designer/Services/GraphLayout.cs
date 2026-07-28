using Flirty.Designer.Models;
using Flirty.Domain;
using Flirty.Runtime.Admin;

namespace Flirty.Designer.Services;

/// <summary>
/// Ordnet den Dialog-Graphen automatisch an – ein „Sugiyama-Light“: Schichtung per Breitensuche ab der
/// Einstiegsfrage, Rückwärtskanten aufgebrochen, Kreuzungen per Baryzentrum reduziert.
/// </summary>
/// <remarks>
/// <para>
/// <b>Warum selbst gebaut:</b> dagre und ELK sind JS-Bibliotheken und haben in .NET kein verbreitetes
/// Äquivalent; eine Node-Toolchain will das Repo bewusst nicht (ADR 0006). Die Alternative wäre gewesen,
/// den Graphen unangeordnet zu zeigen – ohne gespeicherte Positionen (die bringt erst #102) ist das
/// keine.
/// </para>
/// <para>
/// <b>Determinismus ist hier kein Komfort, sondern Anforderung.</b> Derselbe Graph muss dieselben
/// Koordinaten ergeben, sonst wackeln später E2E-Selektoren und Screenshots. Abgesichert wird das an
/// drei Stellen: Es werden ausschließlich <b>Listen in fester Reihenfolge</b> zurückgegeben (nie eine
/// Menge oder ein Wörterbuch, deren Iterationsreihenfolge nicht zugesichert ist); die Sortierschlüssel
/// enden mit einem <b>eindeutigen</b> Ordinal, sind also eine Totalordnung und nicht auf die Stabilität
/// von <c>OrderBy</c> angewiesen; und Koordinaten entstehen nur aus ganzzahligen Schicht-/Spaltenwerten,
/// nie aus einem Baryzentrum – Gleitkomma-Mittelwerte bestimmen die Reihenfolge, nicht die Position.
/// </para>
/// <para>
/// Das Ordinal einer Frage kommt aus <c>(Order, Id)</c> und <b>nicht</b> aus der Guid allein:
/// <c>CreateDialogVersionCommand</c> vergibt beim Klonen jeder Frage eine neue Guid, ein Guid-basiertes
/// Layout würfelte also bei jeder neuen Dialogversion neu durch. <c>Order</c> überlebt das Klonen und
/// ist obendrein die vom Autor gewählte Reihenfolge.
/// </para>
/// <para>
/// <b>Ohne Dummy-Knoten.</b> Ein vollständiger Sugiyama zieht Ketten von Platzhaltern durch
/// übersprungene Schichten. Nötig wären sie hier nur für Rücksprünge – und die gehören optisch ohnehin
/// nicht zwischen die Knoten, sondern in einen Kanal am Rand. Für die Zielgröße von rund 30 Knoten
/// sparen Dummy-Ketten kein einziges Kreuz, kosten aber eine zweite Knotenart im Modell, im Rendering
/// und in der Auswahl.
/// </para>
/// </remarks>
internal static class GraphLayout
{
    /// <summary>Wie oft die Schichten zur Kreuzungsreduktion durchsortiert werden.</summary>
    /// <remarks>
    /// Feste Zahl statt Abbruch bei Konvergenz: Ein Konvergenzkriterium auf Gleitkommawerten wäre die
    /// Sorte Bedingung, die auf einer anderen Maschine anders ausgeht.
    /// </remarks>
    private const int SweepCount = 4;

    /// <summary>Eckenradius der Rücksprung-Kanäle in px.</summary>
    private const double CornerRadius = 12;

    /// <summary>Ordnet den Graphen eines Dialogs an.</summary>
    /// <param name="detail">Der Dialog samt Graph (aus <c>GetDialogQuery</c>).</param>
    /// <returns>Die Geometrie aller Knoten und Kanten.</returns>
    public static GraphLayoutResult Compute(DialogDetail detail)
    {
        ArgumentNullException.ThrowIfNull(detail);

        if (detail.Questions.Count == 0)
        {
            return new GraphLayoutResult([], [], 0, GraphMetrics.MarginX * 2, GraphMetrics.MarginY * 2);
        }

        var order = Normalize(detail);
        var layers = AssignLayers(detail, order);
        var shapes = ClassifyEdges(order, layers);
        var slots = Arrange(detail, order, layers, shapes, out var crossings);
        var pinned = Pinned(detail, order);

        return Render(order, layers, slots, shapes, crossings, pinned);
    }

    /// <summary>
    /// Liest die vom Autor gespeicherten Positionen (<c>DialogLayout</c>) aus dem Dialog.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Sie greifen erst ganz am Ende, in <see cref="Render"/>: Schichtung, Kantenform, Baryzentrum und
    /// Kanalvergabe bleiben am Auto-Layout hängen. Ein verschobener Knoten ändert damit <b>nur seine
    /// Position</b> – nie die Zeichenform einer Kante und nie die Anordnung der übrigen Knoten. Sonst
    /// könnte ein einziger Zug den ganzen Graphen umsortieren.
    /// </para>
    /// <para>
    /// Die Determinismus-Zusage bleibt: Gleiche Eingabe – Graph <i>und</i> Layout-Zeilen – ergibt
    /// dieselben Koordinaten.
    /// </para>
    /// </remarks>
    private static Dictionary<Guid, (double X, double Y)> Pinned(DialogDetail detail, GraphOrder order)
    {
        var pinned = new Dictionary<Guid, (double X, double Y)>();

        foreach (var entry in detail.Layout)
        {
            // Nur Fragen sind heute Knoten; eine Zeile auf eine gelöschte Frage hat kein Ziel.
            if (entry.ElementKind != LayoutElementKind.Question || !order.Ordinal.ContainsKey(entry.ElementId))
            {
                continue;
            }

            // Negative Werte lehnt der Command ab; ein von Hand geschriebener Datensatz käme sonst
            // ausserhalb der Zeichenfläche zu liegen und wäre unerreichbar.
            pinned[entry.ElementId] = (Math.Max(0, entry.X), Math.Max(0, entry.Y));
        }

        return pinned;
    }

    // ---- Schritt 0: Normalisierung ------------------------------------------------------------------

    /// <summary>
    /// Bringt Fragen und Übergänge in eine eindeutige, wiederholbare Reihenfolge – die Grundlage jeder
    /// späteren Determinismus-Zusage.
    /// </summary>
    private static GraphOrder Normalize(DialogDetail detail)
    {
        // (Order, Id): Order ist die Autorenreihenfolge und überlebt das Klonen einer Dialogversion,
        // die Id löst den Gleichstand, den der Designer selbst nie erzeugt.
        var questions = detail.Questions
            .OrderBy(question => question.Order)
            .ThenBy(question => question.Id)
            .ToArray();

        var ordinal = new Dictionary<Guid, int>(questions.Length);
        for (var index = 0; index < questions.Length; index++)
        {
            ordinal[questions[index].Id] = index;
        }

        // Nur zeichenbare Kanten. Die globale Priority-Sortierung aus DialogDetail reicht als
        // Reihenfolge nicht: Bei gleicher Priority in verschiedenen Ausgangsfragen ist sie beliebig.
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

    // ---- Schritt 1: Schichtung ----------------------------------------------------------------------

    /// <summary>
    /// Schichtet den Graphen per Breitensuche ab der Einstiegsfrage. Weil die Breitensuche kürzeste Wege
    /// findet, gilt für jede Kante innerhalb einer Komponente <c>layer[ziel] ≤ layer[quelle] + 1</c> –
    /// eine Vorwärtskante überspringt also nie eine Schicht.
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
            // Ohne Einstiegsfrage gibt es keinen Bezugspunkt für „erreichbar“. Dann wären alle Fragen
            // rot markiert, obwohl nur eine Angabe fehlt – der Befund gehört an den Dialog, nicht an
            // jeden Knoten. Wurzeln sind hier die Fragen, auf die nichts zeigt.
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

        // Was jetzt noch fehlt, hängt an keinem Pfad von der Einstiegsfrage. Es kommt hinter den Graphen,
        // getrennt durch eine leere Schicht – das Band ist die optische Aussage „gehört nicht dazu“.
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

    // ---- Schritt 2: Rückwärtskanten aufbrechen ------------------------------------------------------

    /// <summary>
    /// Teilt die Kanten nach ihrer Zeichenform ein. Nur <see cref="GraphEdgeShape.Forward"/>-Kanten
    /// zwischen benachbarten Schichten ordnen mit – Rücksprünge sind aus dem azyklischen Kantensatz
    /// entfernt, sonst zöge der Zyklus die Anordnung in sich zusammen.
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

    // ---- Schritt 3: Baryzentrum ---------------------------------------------------------------------

    /// <summary>
    /// Sortiert die Knoten innerhalb ihrer Schichten so um, dass sich möglichst wenige Kanten kreuzen:
    /// Jeder Knoten rückt an das Mittel („Baryzentrum“) seiner Nachbarn in der jeweils festgehaltenen
    /// Nachbarschicht.
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

        // Startreihenfolge ist die Entdeckungsreihenfolge der Breitensuche, ausgedrückt über das
        // Ordinal – reproduzierbar ohne die Suche noch einmal laufen zu lassen.
        foreach (var question in order.Questions)
        {
            slots[layers.Layer[question.Id]].Add(question.Id);
        }

        // Nur Kanten zwischen unmittelbar benachbarten Schichten ordnen mit; alles andere hat in der
        // dummy-freien Variante keinen definierten Platz in der Zwischenschicht.
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
    /// Ordnet eine Schicht nach dem Baryzentrum ihrer Knoten. Der Sortierschlüssel endet mit dem
    /// eindeutigen Ordinal und ist damit eine Totalordnung – zwei Aufrufe können nicht unterschiedlich
    /// ausgehen.
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
            // Fragen derselben Schleife bleiben beieinander – sonst spannt der Bereichsrahmen später
            // über fremde Knoten hinweg.
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
    /// Zählt die Kreuzungen zwischen benachbarten Schichten – die Inversionen der Zielpositionen, wenn
    /// die Kanten nach ihrer Quellposition gelesen werden. Bei der Zielgröße von rund 30 Knoten ist der
    /// quadratische Aufwand belanglos; der Wert macht die Anordnung messbar statt behauptbar.
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

    /// <summary>Ordnet jeder Frage die Schleife zu, in deren Bereich sie liegt.</summary>
    private static Dictionary<Guid, int> LoopIndexes(DialogDetail detail, GraphOrder order)
    {
        var loopIndex = new Dictionary<Guid, int>();

        for (var index = 0; index < detail.Loops.Count; index++)
        {
            var body = LoopAnalyzer.ComputeBody(detail, detail.Loops[index]);

            // Über die geordnete Fragenliste projizieren, nicht über die Menge iterieren: Die
            // Iterationsreihenfolge eines HashSet ist nicht zugesichert.
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

    // ---- Schritt 4 und 5: Koordinaten und Kantenverlauf ---------------------------------------------

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

                // Die Schicht wird mittig ausgerichtet. Alle Faktoren sind ganzzahlig, PitchX ist gerade –
                // es entstehen keine Nachkommastellen aus einer Division.
                var x = GraphMetrics.MarginX
                    + ((widest - layer.Count) * GraphMetrics.PitchX / 2)
                    + (slot * GraphMetrics.PitchX);
                var y = GraphMetrics.MarginY + (layerIndex * GraphMetrics.PitchY);

                // Die gespeicherte Position schlägt die berechnete – und zwar erst hier, damit sie das
                // Kantenrouting unten mitnimmt, aber die Anordnung darüber nicht beeinflusst hat.
                var isPinned = pinned.TryGetValue(id, out var saved);
                boxes[id] = isPinned ? saved : (x, y);

                nodes.Add(new GraphNodePosition(
                    id, layerIndex, slot, boxes[id].X, boxes[id].Y, layers.Reachable.Contains(id), isPinned));
            }
        }

        // Die Zeichenfläche muss auch verschobene Knoten fassen: Der Kanal der Rücksprünge liegt rechts
        // von allem, was gezeichnet wird – sonst liefe er quer durch einen weit gezogenen Knoten.
        var contentWidth = Math.Max(
            GraphMetrics.MarginX + (widest * GraphMetrics.PitchX) - GraphMetrics.GapX,
            boxes.Values.Max(box => box.X) + GraphMetrics.NodeWidth);

        var lanes = AssignLanes(order, layers, shapes);
        var width = contentWidth + GraphMetrics.MarginX + (lanes.Count * GraphMetrics.GutterStep);
        var height = Math.Max(
            GraphMetrics.MarginY + (slots.Count * GraphMetrics.PitchY) - GraphMetrics.GapY,
            boxes.Values.Max(box => box.Y) + GraphMetrics.NodeHeight) + GraphMetrics.MarginY;

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
    /// Vergibt die Kanäle rechts des Graphen an die Rücksprünge – der längste außen, damit sich die
    /// Kanäle nicht schneiden.
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

                // Der Biegeradius darf die halbe Spannweite nicht überschreiten: Bei zwei benachbarten
                // Schichten liegen nur GapY zwischen den Knoten, ein fester Wert schöbe die
                // Kontrollpunkte aneinander vorbei und die Kante liefe als S-Schlaufe.
                var bend = Math.Min(GraphMetrics.EdgeBend, Math.Abs(ty - sy) / 2);

                var path = $"M {N(sx)} {N(sy)} C {N(sx)} {N(sy + bend)} "
                    + $"{N(tx)} {N(ty - bend)} {N(tx)} {N(ty)}";

                // Der Mittelpunkt einer kubischen Bézier mit senkrechten Kontrollpunkten liegt exakt
                // zwischen Start und Ziel – kein Näherungswert nötig.
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

                // Mittelpunkt der quadratischen Bézier: (P0 + 2·P1 + P2) / 4.
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
                // Rücksprünge laufen im Kanal rechts am Graphen vorbei nach oben. Zwischen die Knoten
                // gezeichnet würden sie den Fluss verdecken, den der Canvas gerade zeigen soll.
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

    /// <summary>Fragen und Kanten in eindeutiger Reihenfolge, mit dem Ordinal je Frage.</summary>
    private sealed record GraphOrder(
        QuestionDetail[] Questions,
        Dictionary<Guid, int> Ordinal,
        TransitionDetail[] Edges);

    /// <summary>Die Schicht je Frage und die Menge der von der Einstiegsfrage aus erreichbaren Fragen.</summary>
    private sealed record LayerAssignment(Dictionary<Guid, int> Layer, HashSet<Guid> Reachable);
}
