using Flirty.Domain;
using Flirty.Runtime.Admin;

namespace Flirty.Designer.Models;

/// <summary>
/// What is selected on the canvas. <see langword="null"/> means: nothing selected.
/// </summary>
/// <param name="Kind">The kind of the selected element.</param>
/// <param name="Id">Its primary key.</param>
public sealed record GraphSelection(GraphElementKind Kind, Guid Id);

/// <summary>A trigger attached to a node or to a scope marker.</summary>
/// <param name="TriggerId">The trigger.</param>
/// <param name="Label">The short label of the chip (channel and target).</param>
/// <param name="Title">The full description for tooltip and screen reader.</param>
/// <param name="Kind">The channel – determines the coloring.</param>
public sealed record GraphTriggerChip(Guid TriggerId, string Label, string Title, TriggerKind Kind);

/// <summary>A question as a node on the canvas.</summary>
/// <param name="QuestionId">The question.</param>
/// <param name="Key">Its key – the header of the card.</param>
/// <param name="Text">The shortened question text.</param>
/// <param name="FullText">The full question text (tooltip).</param>
/// <param name="TypeLabel">The display text of the question type.</param>
/// <param name="IsRequired">Whether the question is configured as required.</param>
/// <param name="OptionCount">The number of answer options (only relevant for choice types).</param>
/// <param name="UsesOptions">Whether the question type knows answer options at all.</param>
/// <param name="X">Top-left corner in px.</param>
/// <param name="Y">Top-left corner in px.</param>
/// <param name="IsStart">Whether the question is the entry question of the dialog.</param>
/// <param name="IsTerminal">
/// Whether the question has no outgoing transition. This is <b>not an error</b>, but the regular
/// end of the dialog: <c>TransitionResolver.ResolveTransitionTarget</c> returns <see langword="null"/> there.
/// </param>
/// <param name="IsUnreachable">Whether no path from the entry question leads here.</param>
/// <param name="IsLoopEntry">Whether the question is the entry question of a loop.</param>
/// <param name="IsLoopBreaking">Whether the question is the breaking question of a loop.</param>
/// <param name="InLoop">Whether the question lies within the body of a loop.</param>
/// <param name="IsPinned">
/// Whether the node lies at a position saved by the author (<c>DialogLayout</c>) instead of at the one
/// computed by the auto-layout.
/// </param>
/// <param name="Triggers">The triggers with <see cref="TriggerScope.AfterQuestion"/> on this question.</param>
/// <param name="Warnings">The warnings attached to this question.</param>
/// <param name="AriaLabel">The full description for screen readers.</param>
public sealed record GraphNode(
    Guid QuestionId,
    string Key,
    string Text,
    string FullText,
    string TypeLabel,
    bool IsRequired,
    int OptionCount,
    bool UsesOptions,
    double X,
    double Y,
    bool IsStart,
    bool IsTerminal,
    bool IsUnreachable,
    bool IsLoopEntry,
    bool IsLoopBreaking,
    bool InLoop,
    bool IsPinned,
    IReadOnlyList<GraphTriggerChip> Triggers,
    IReadOnlyList<GraphWarning> Warnings,
    string AriaLabel);

/// <summary>A transition as an edge on the canvas.</summary>
/// <param name="TransitionId">The transition.</param>
/// <param name="FromQuestionId">The source question.</param>
/// <param name="TargetQuestionId">The target question.</param>
/// <param name="Path">The SVG path.</param>
/// <param name="Shape">The drawing shape.</param>
/// <param name="LabelX">Anchor point of the label.</param>
/// <param name="LabelY">Anchor point of the label.</param>
/// <param name="Label">The shortened label (condition or "Default").</param>
/// <param name="Position">The 1-based evaluation position within the source question.</param>
/// <param name="IsDefault">Whether it is the default transition.</param>
/// <param name="IsBackJump">
/// Whether the transition points to an earlier question <b>in list order</b> – the same statement that
/// the <c>DialogEditor</c> shows as the "Back-jump" badge. Deliberately not identical to
/// <see cref="GraphEdgeShape.BackJump"/>: that is a statement about the <i>layering</i> of the layout.
/// Both are correct, but they answer different questions.
/// </param>
/// <param name="Warnings">The warnings attached to this edge.</param>
/// <param name="AriaLabel">The full description for screen readers.</param>
public sealed record GraphEdge(
    Guid TransitionId,
    Guid FromQuestionId,
    Guid TargetQuestionId,
    string Path,
    GraphEdgeShape Shape,
    double LabelX,
    double LabelY,
    string Label,
    int Position,
    bool IsDefault,
    bool IsBackJump,
    IReadOnlyList<GraphWarning> Warnings,
    string AriaLabel);

/// <summary>
/// A loop as a range frame around the nodes of its body – not a node of its own, because it is none in
/// the domain model: <c>LoopDefinition</c> is a marker over the branching.
/// </summary>
/// <param name="LoopId">The loop marker.</param>
/// <param name="CollectionKey">The collection key – the label of the frame.</param>
/// <param name="X">Top-left corner in px.</param>
/// <param name="Y">Top-left corner in px.</param>
/// <param name="Width">Width in px.</param>
/// <param name="Height">Height in px.</param>
/// <param name="EntryKey">Key of the entry question (empty if it is missing).</param>
/// <param name="BreakingKey">Key of the breaking question (empty if it is missing).</param>
/// <param name="Warnings">The warnings attached to this marker.</param>
public sealed record GraphLoopFrame(
    Guid LoopId,
    string CollectionKey,
    double X,
    double Y,
    double Width,
    double Height,
    string EntryKey,
    string BreakingKey,
    IReadOnlyList<GraphWarning> Warnings);

/// <summary>
/// A marker for the triggers that are not attached to any single question – start and completion of the dialog.
/// </summary>
/// <param name="Title">The label.</param>
/// <param name="X">Top-left corner in px.</param>
/// <param name="Y">Top-left corner in px.</param>
/// <param name="Triggers">The triggers of this point in time.</param>
public sealed record GraphScopeMarker(
    string Title,
    double X,
    double Y,
    IReadOnlyList<GraphTriggerChip> Triggers);

/// <summary>
/// The entire dialog graph, ready to draw: nodes, edges, loop frames, scope markers and
/// all warnings – each at the element that causes it.
/// </summary>
/// <remarks>
/// Built by <see cref="Flirty.Designer.Services.DialogGraphBuilder"/> <b>once after loading</b>
/// and held in a field. Called from the markup, the whole arrangement would run on every render,
/// that is on every click.
/// </remarks>
/// <param name="Dialog">The header data of the dialog.</param>
/// <param name="Nodes">The nodes, sorted by layer and column – at the same time the tab order.</param>
/// <param name="Edges">The edges.</param>
/// <param name="Loops">The loop frames.</param>
/// <param name="StartMarker">The start marker, if it carries triggers.</param>
/// <param name="EndMarker">The completion marker, if it carries triggers.</param>
/// <param name="DialogWarnings">Warnings without an element reference (such as the missing entry question).</param>
/// <param name="OrphanTransitions">
/// Transitions whose source or target question no longer belongs to the dialog. They are not drawable
/// and are reported separately, instead of silently disappearing.
/// </param>
/// <param name="OrphanTriggers">Triggers that point to a question that is no longer present.</param>
/// <param name="Summary">A brief version of the graph in words – the alternative to the picture.</param>
/// <param name="MinY">Top edge of the drawing surface (negative if a start marker lies above it).</param>
/// <param name="Width">Width of the drawing surface in px.</param>
/// <param name="Height">Height of the drawing surface in px.</param>
public sealed record DialogGraphModel(
    DialogSummary Dialog,
    IReadOnlyList<GraphNode> Nodes,
    IReadOnlyList<GraphEdge> Edges,
    IReadOnlyList<GraphLoopFrame> Loops,
    GraphScopeMarker? StartMarker,
    GraphScopeMarker? EndMarker,
    IReadOnlyList<GraphWarning> DialogWarnings,
    IReadOnlyList<TransitionDetail> OrphanTransitions,
    IReadOnlyList<TriggerDetail> OrphanTriggers,
    string Summary,
    double MinY,
    double Width,
    double Height)
{
    /// <summary>All warnings of the graph, regardless of what they are attached to.</summary>
    public IReadOnlyList<GraphWarning> AllWarnings =>
    [
        .. DialogWarnings,
        .. Nodes.SelectMany(node => node.Warnings),
        .. Edges.SelectMany(edge => edge.Warnings),
        .. Loops.SelectMany(loop => loop.Warnings),
    ];

    /// <summary>Finds a node by its question.</summary>
    /// <param name="questionId">The question sought.</param>
    /// <returns>The node or <see langword="null"/>.</returns>
    public GraphNode? Node(Guid questionId)
        => Nodes.FirstOrDefault(node => node.QuestionId == questionId);

    /// <summary>Finds an edge by its transition.</summary>
    /// <param name="transitionId">The transition sought.</param>
    /// <returns>The edge or <see langword="null"/>.</returns>
    public GraphEdge? Edge(Guid transitionId)
        => Edges.FirstOrDefault(edge => edge.TransitionId == transitionId);
}
