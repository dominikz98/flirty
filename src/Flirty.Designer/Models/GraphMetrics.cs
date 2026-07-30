namespace Flirty.Designer.Models;

/// <summary>
/// The dimensions of the graph canvas (#101) – one source for the layout in C#, the markup in
/// <c>DialogGraph.razor</c> and the limits passed to the JS module.
/// </summary>
/// <remarks>
/// The spacings are deliberately <b>integer and even</b>: the coordinates arise exclusively as
/// integer multiples of layer and column, never from an average. Otherwise the
/// last decimal places would hang on the floating-point order, and the promise "same graph ⇒ same
/// coordinates" (acceptance criterion) would only be mostly true.
/// </remarks>
public static class GraphMetrics
{
    /// <summary>Width of a node card in px.</summary>
    public const double NodeWidth = 240;

    /// <summary>
    /// Height of a node card in px. Sized for the full content – badge row, two lines of
    /// question text, meta row <b>and</b> a row of trigger chips. The card clips overflow; were
    /// it tighter, the chips would disappear invisibly.
    /// </summary>
    public const double NodeHeight = 112;

    /// <summary>Horizontal gap between two nodes of the same layer in px.</summary>
    public const double GapX = 60;

    /// <summary>Vertical gap between two layers in px.</summary>
    public const double GapY = 80;

    /// <summary>Margin left and right of the graph in px.</summary>
    public const double MarginX = 40;

    /// <summary>Margin top and bottom in px.</summary>
    public const double MarginY = 40;

    /// <summary>Horizontal grid pitch: node width plus gap.</summary>
    public const double PitchX = NodeWidth + GapX;

    /// <summary>Vertical grid pitch: node height plus gap.</summary>
    public const double PitchY = NodeHeight + GapY;

    /// <summary>
    /// Lateral offset per additional edge between the same node pair in px. Without it several
    /// transitions would lie congruently on top of each other and would not be distinguishable.
    /// </summary>
    public const double FanStep = 18;

    /// <summary>Deflection of the Bézier control points of a forward edge in px.</summary>
    public const double EdgeBend = 70;

    /// <summary>Distance of two back-jump channels to the right of the graph in px.</summary>
    public const double GutterStep = 34;

    /// <summary>Inner padding of a loop frame to the nodes of its body in px.</summary>
    public const double LoopFramePadding = 20;

    /// <summary>
    /// Additional inner padding per further loop frame in px – so that two frames do not
    /// coincide exactly.
    /// </summary>
    public const double LoopFramePaddingStep = 10;

    /// <summary>
    /// Edge length of the source port at the node in px (#103).
    /// </summary>
    /// <remarks>
    /// The port sits at the <b>bottom-edge center</b> – exactly where <c>GraphLayout.Route</c> starts
    /// a forward edge. The affordance thereby asserts the same starting point that the created
    /// edge has later; a port at a different place would be a visible lie about the geometry.
    /// </remarks>
    public const double PortSize = 26;

    /// <summary>
    /// Smallest width of the drawing surface in px – regardless of how little lies on it.
    /// </summary>
    /// <remarks>
    /// Since #103 the canvas is a <b>drop surface</b>: a question type is dragged onto it from the
    /// palette. Without a lower bound the surface of an empty dialog would be
    /// <c>MarginX * 2</c> × <c>MarginY * 2</c> = 80 × 80 px – too small to aim at, and it is exactly
    /// in the empty dialog that one begins.
    /// </remarks>
    public const double MinCanvasWidth = 960;

    /// <summary>Smallest height of the drawing surface in px. Rationale as for <see cref="MinCanvasWidth"/>.</summary>
    public const double MinCanvasHeight = 540;

    /// <summary>Smallest zoom factor of the canvas.</summary>
    public const double MinZoom = 0.25;

    /// <summary>Largest zoom factor of the canvas.</summary>
    public const double MaxZoom = 2.5;

    /// <summary>Zoom step per notch of the mouse wheel or click on the toolbar.</summary>
    public const double ZoomStep = 1.15;
}
