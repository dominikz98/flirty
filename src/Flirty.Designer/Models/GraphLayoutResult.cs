namespace Flirty.Designer.Models;

/// <summary>
/// Die Form, in der eine Kante gezeichnet wird. Sie ergibt sich aus der Schichtung, nicht aus der
/// Konfiguration – dieselbe Kante kann in einem anderen Graphen eine andere Form haben.
/// </summary>
public enum GraphEdgeShape
{
    /// <summary>Von einer Schicht in die nächste – der Regelfall, ein Bogen nach unten.</summary>
    Forward = 0,

    /// <summary>Innerhalb derselben Schicht – ein flacher Bogen zur Seite.</summary>
    Flat = 1,

    /// <summary>Von einer Frage auf sich selbst – eine kleine Schleife am Knoten.</summary>
    SelfLoop = 2,

    /// <summary>Zurück in eine frühere Schicht – der Zyklus, der eine Schleife trägt.</summary>
    BackJump = 3,
}

/// <summary>Die berechnete Position eines Knotens.</summary>
/// <param name="QuestionId">Die Frage, die der Knoten zeigt.</param>
/// <param name="Layer">Die Schicht (0 = Einstiegsfrage).</param>
/// <param name="Slot">Die Spalte innerhalb der Schicht, von links nach rechts.</param>
/// <param name="X">Die linke obere Ecke in px.</param>
/// <param name="Y">Die linke obere Ecke in px.</param>
/// <param name="IsReachable">
/// Ob die Frage von der Einstiegsfrage aus über Übergänge erreichbar ist. Ohne gesetzte Einstiegsfrage
/// gelten alle Fragen als erreichbar – sonst wäre der ganze Graph als defekt markiert, obwohl nur eine
/// Angabe fehlt.
/// </param>
public sealed record GraphNodePosition(
    Guid QuestionId,
    int Layer,
    int Slot,
    double X,
    double Y,
    bool IsReachable);

/// <summary>Der berechnete Verlauf einer Kante.</summary>
/// <param name="TransitionId">Der Übergang, den die Kante zeigt.</param>
/// <param name="Shape">Die Zeichenform.</param>
/// <param name="Path">Der fertige SVG-Pfad (<c>d</c>-Attribut), kulturunabhängig formatiert.</param>
/// <param name="LabelX">Ankerpunkt der Beschriftung in px.</param>
/// <param name="LabelY">Ankerpunkt der Beschriftung in px.</param>
/// <param name="FanIndex">
/// Der Platz dieser Kante im Fächer paralleler Kanten zwischen denselben zwei Knoten (0-basiert).
/// </param>
/// <param name="FanCount">Wie viele Kanten dasselbe Knotenpaar verbinden.</param>
public sealed record GraphEdgeRoute(
    Guid TransitionId,
    GraphEdgeShape Shape,
    string Path,
    double LabelX,
    double LabelY,
    int FanIndex,
    int FanCount);

/// <summary>
/// Das Ergebnis des Auto-Layouts: reine Geometrie, frei von Fachdaten – und damit für sich testbar.
/// </summary>
/// <remarks>
/// Beide Sammlungen sind <b>Listen in fester Reihenfolge</b>, nie Wörterbücher oder Mengen: Deren
/// Iterationsreihenfolge ist nicht zugesichert, und ein Layout, das sich zwischen zwei Aufrufen
/// umsortiert, würde später E2E-Selektoren zum Wackeln bringen.
/// </remarks>
/// <param name="Nodes">Die Knoten, sortiert nach Schicht und Spalte – zugleich die Renderreihenfolge.</param>
/// <param name="Edges">Die Kanten in stabiler Reihenfolge.</param>
/// <param name="Crossings">
/// Die Zahl der Kantenkreuzungen zwischen benachbarten Schichten nach der Sortierung. Macht die Güte
/// der Anordnung messbar, statt sie behaupten zu müssen.
/// </param>
/// <param name="Width">Gesamtbreite der Zeichenfläche in px.</param>
/// <param name="Height">Gesamthöhe der Zeichenfläche in px.</param>
public sealed record GraphLayoutResult(
    IReadOnlyList<GraphNodePosition> Nodes,
    IReadOnlyList<GraphEdgeRoute> Edges,
    int Crossings,
    double Width,
    double Height);
