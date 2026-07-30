namespace Flirty.Designer.Models;

/// <summary>
/// The shape in which an edge is drawn. It arises from the layering, not from the
/// configuration – the same edge can have a different shape in a different graph.
/// </summary>
public enum GraphEdgeShape
{
    /// <summary>From one layer into the next – the normal case, an arc downward.</summary>
    Forward = 0,

    /// <summary>Within the same layer – a flat arc to the side.</summary>
    Flat = 1,

    /// <summary>From a question onto itself – a small loop at the node.</summary>
    SelfLoop = 2,

    /// <summary>Back into an earlier layer – the cycle that carries a loop.</summary>
    BackJump = 3,
}

/// <summary>The computed position of a node.</summary>
/// <param name="QuestionId">The question that the node shows.</param>
/// <param name="Layer">The layer (0 = entry question).</param>
/// <param name="Slot">The column within the layer, from left to right.</param>
/// <param name="X">The top-left corner in px.</param>
/// <param name="Y">The top-left corner in px.</param>
/// <param name="IsReachable">
/// Whether the question is reachable from the entry question via transitions. Without a set entry question
/// all questions are considered reachable – otherwise the whole graph would be marked as broken, even though
/// only one entry is missing.
/// </param>
/// <param name="IsPinned">
/// Whether <see cref="X"/>/<see cref="Y"/> stem from a saved position (<c>DialogLayout</c>) and
/// not from the auto-layout. <see cref="Layer"/> and <see cref="Slot"/> still come from
/// the arrangement even then – a drag changes the position, not the structure.
/// </param>
public sealed record GraphNodePosition(
    Guid QuestionId,
    int Layer,
    int Slot,
    double X,
    double Y,
    bool IsReachable,
    bool IsPinned);

/// <summary>The computed course of an edge.</summary>
/// <param name="TransitionId">The transition that the edge shows.</param>
/// <param name="Shape">The drawing shape.</param>
/// <param name="Path">The finished SVG path (<c>d</c> attribute), formatted culture-independently.</param>
/// <param name="LabelX">Anchor point of the label in px.</param>
/// <param name="LabelY">Anchor point of the label in px.</param>
/// <param name="FanIndex">
/// The place of this edge in the fan of parallel edges between the same two nodes (0-based).
/// </param>
/// <param name="FanCount">How many edges connect the same node pair.</param>
public sealed record GraphEdgeRoute(
    Guid TransitionId,
    GraphEdgeShape Shape,
    string Path,
    double LabelX,
    double LabelY,
    int FanIndex,
    int FanCount);

/// <summary>
/// The result of the auto-layout: pure geometry, free of domain data – and thus testable on its own.
/// </summary>
/// <remarks>
/// Both collections are <b>lists in fixed order</b>, never dictionaries or sets: their
/// iteration order is not guaranteed, and a layout that reorders itself between two calls
/// would later make E2E selectors flaky.
/// </remarks>
/// <param name="Nodes">The nodes, sorted by layer and column – at the same time the render order.</param>
/// <param name="Edges">The edges in stable order.</param>
/// <param name="Crossings">
/// The number of edge crossings between adjacent layers after sorting. Makes the quality
/// of the arrangement measurable, instead of having to assert it.
/// </param>
/// <param name="Width">Total width of the drawing surface in px.</param>
/// <param name="Height">Total height of the drawing surface in px.</param>
public sealed record GraphLayoutResult(
    IReadOnlyList<GraphNodePosition> Nodes,
    IReadOnlyList<GraphEdgeRoute> Edges,
    int Crossings,
    double Width,
    double Height);
