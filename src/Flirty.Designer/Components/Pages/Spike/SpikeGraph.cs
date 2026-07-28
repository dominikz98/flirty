namespace Flirty.Designer.Components.Pages.Spike;

/// <summary>Ein Knoten des Spike-Testgraphen. <paramref name="X"/>/<paramref name="Y"/> ist die linke obere Ecke.</summary>
internal sealed record SpikeNode(int Index, string Key, string Label, double X, double Y);

/// <summary>Eine gerichtete Kante des Spike-Testgraphen (Indizes in <see cref="SpikeGraph.Nodes"/>).</summary>
internal sealed record SpikeEdge(int Id, int From, int To);

/// <summary>
/// Der Testgraph des Canvas-Spikes (#100): <b>30 Knoten, 45 Kanten</b>, darunter ein Zyklus und eine
/// Frage mit vier ausgehenden Übergängen – exakt die Messlatte aus dem Issue.
/// </summary>
/// <remarks>
/// <para>
/// Diese Klasse ist die <b>einzige</b> Quelle beider Prototypen. Nur so ist „beide zeigen denselben
/// Graphen" belegbar statt behauptet; der Messlauf prüft es zusätzlich über die Elementanzahl im DOM.
/// </para>
/// <para>
/// Bewusst <b>synthetisch</b> statt über <c>FlirtyAdminGateway</c>: Gemessen werden Rendering und
/// Interaktion, nicht Persistenz. Der Gateway zöge EF, SQLite und rund 75 Commands je Seitenaufbau in
/// die Messung – Rauschen ohne Gegenwert.
/// </para>
/// <para>
/// Der Aufbau ist <b>deterministisch</b> (kein <c>Random</c>), damit beide Prototypen und alle
/// Wiederholungen denselben Graphen sehen.
/// </para>
/// </remarks>
internal static class SpikeGraph
{
    /// <summary>Breite eines Knotens in px. Identisch in beiden Prototypen (Fairness).</summary>
    public const double NodeWidth = 160;

    /// <summary>Höhe eines Knotens in px. Identisch in beiden Prototypen (Fairness).</summary>
    public const double NodeHeight = 48;

    /// <summary>
    /// Der Knoten, den der Messlauf zieht. Index 8 hat <b>vier</b> inzidente Kanten (7→8, 5→8, 8→9,
    /// 8→29) – ein Knoten ohne Kanten würde den teuersten Teil des Neuzeichnens verschweigen. Seine
    /// Rasterposition (Spalte 2, Zeile 1) lässt 300 px Ziehweg innerhalb eines 1280×720-Viewports.
    /// </summary>
    public const int DragTargetIndex = 8;

    private const int Columns = 6;
    private const double MarginX = 40;
    private const double MarginY = 40;
    private const double PitchX = 200;
    private const double PitchY = 130;

    /// <summary>Die 30 Knoten in Rasteranordnung (6 Spalten × 5 Zeilen).</summary>
    public static IReadOnlyList<SpikeNode> Nodes { get; } = BuildNodes();

    /// <summary>Die 45 gerichteten Kanten.</summary>
    public static IReadOnlyList<SpikeEdge> Edges { get; } = BuildEdges();

    private static SpikeNode[] BuildNodes()
    {
        var nodes = new SpikeNode[30];
        for (var i = 0; i < nodes.Length; i++)
        {
            nodes[i] = new SpikeNode(
                i,
                $"frage_{i:00}",
                $"Frage {i:00}",
                MarginX + (i % Columns) * PitchX,
                MarginY + (i / Columns) * PitchY);
        }

        return nodes;
    }

    private static SpikeEdge[] BuildEdges()
    {
        var edges = new List<(int From, int To)>(45);

        // 1) Kette 0→1 … 28→29 (29 Kanten) – der lineare Grundverlauf.
        for (var i = 0; i < 29; i++)
        {
            edges.Add((i, i + 1));
        }

        // 2) Der geforderte Zyklus: Rücksprung 17→9 (wie ein Loop-Rücksprung im echten Designer).
        edges.Add((17, 9));

        // 3) Die geforderte Frage mit vier ausgehenden Übergängen: 7→8 kommt aus der Kette, dazu drei.
        edges.Add((7, 20));
        edges.Add((7, 25));
        edges.Add((7, 29));

        // 4) Auffüllen auf 45 über eine feste Streuformel. Quelle 7 bleibt ausgespart, damit dieser
        //    Knoten bei genau vier ausgehenden Kanten bleibt. Die Formel erzeugt weder Selbstschleifen
        //    (6i ≡ 27 mod 30 ist unlösbar) noch Dubletten zur Kette (6i ≡ 28 mod 30 ebenso).
        for (var i = 0; edges.Count < 45; i++)
        {
            if (i == 7)
            {
                continue;
            }

            edges.Add((i, (i * 7 + 3) % 30));
        }

        return [.. edges.Select((e, index) => new SpikeEdge(index, e.From, e.To))];
    }

    /// <summary>Mittelpunkt eines Knotens – Ankerpunkt aller Kanten in beiden Prototypen.</summary>
    /// <param name="node">Der Knoten.</param>
    /// <returns>Der Mittelpunkt in Canvas-Koordinaten.</returns>
    public static (double X, double Y) CenterOf(SpikeNode node)
        => (node.X + (NodeWidth / 2), node.Y + (NodeHeight / 2));
}
