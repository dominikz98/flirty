namespace Flirty.Designer.Models;

/// <summary>
/// The kind of a graph element – target of a <see cref="GraphWarning"/> and at the same time kind of the selection in
/// <see cref="GraphSelection"/>.
/// </summary>
/// <remarks>
/// The two roles do not fully coincide: <see cref="Trigger"/> is exclusively selectable
/// (there is no warning on a trigger), and <see cref="Dialog"/> carries, as a selection, the
/// scope markers – the triggers without a question reference. A second enum for that would be a duplicate with four
/// identical values.
/// </remarks>
public enum GraphElementKind
{
    /// <summary>
    /// The dialog as a whole: a warning without a single element as the cause – or, as a selection, the
    /// scope markers with the triggers without a question reference (#103).
    /// </summary>
    Dialog = 0,

    /// <summary>A question (node on the canvas).</summary>
    Question = 1,

    /// <summary>A transition (edge on the canvas).</summary>
    Transition = 2,

    /// <summary>A loop marker (range frame on the canvas).</summary>
    Loop = 3,

    /// <summary>A single trigger (chip at the node or at the scope marker) – only as a selection (#103).</summary>
    Trigger = 4,
}

/// <summary>
/// A configuration warning <b>with a location</b>: the same finding that the <c>DialogEditor</c> shows as
/// running text, additionally assigned to the element that causes it.
/// </summary>
/// <remarks>
/// <para>
/// The graph view (#101) must show warnings <b>at the affected node or at the affected edge</b>,
/// the list view still needs running text per source question. Feeding both from the same
/// source is the whole purpose of this type – a second warning logic beside
/// <see cref="Flirty.Designer.Services.TransitionWarningAnalyzer"/> and
/// <see cref="Flirty.Designer.Services.LoopAnalyzer"/> would inevitably drift apart.
/// </para>
/// <para>
/// <see cref="Text"/> is deliberately the <b>unchanged</b> wording that the designer has always
/// shown: the texts are a contract towards the tests and the E2E suite.
/// </para>
/// <para>
/// The type is <see langword="public"/> because Razor components are generated <see langword="public"/>
/// and an <see langword="internal"/> type on a <c>[Parameter]</c> leads to CS0053 – under
/// <c>TreatWarningsAsErrors</c> therefore to a build error. The same rationale as with
/// <see cref="AnswerChoice"/>.
/// </para>
/// </remarks>
/// <param name="Kind">The kind of the affected element.</param>
/// <param name="ElementId">
/// The primary key of the affected element; <see langword="null"/> for
/// <see cref="GraphElementKind.Dialog"/>.
/// </param>
/// <param name="QuestionId">
/// The reference question for grouping and prefix in the list view: for a question identical to
/// <paramref name="ElementId"/>, for a transition its source question, otherwise <see langword="null"/>.
/// </param>
/// <param name="Text">The warning text – word for word the same as what the <c>DialogEditor</c> displays.</param>
public sealed record GraphWarning(
    GraphElementKind Kind,
    Guid? ElementId,
    Guid? QuestionId,
    string Text)
{
    /// <summary>A warning that hangs on a question (node).</summary>
    /// <param name="questionId">The affected question.</param>
    /// <param name="text">The warning text.</param>
    /// <returns>The located warning.</returns>
    public static GraphWarning ForQuestion(Guid questionId, string text)
        => new(GraphElementKind.Question, questionId, questionId, text);

    /// <summary>A warning that hangs on a transition (edge).</summary>
    /// <param name="transitionId">The affected transition.</param>
    /// <param name="fromQuestionId">Its source question – the reference question of the list view.</param>
    /// <param name="text">The warning text.</param>
    /// <returns>The located warning.</returns>
    public static GraphWarning ForTransition(Guid transitionId, Guid fromQuestionId, string text)
        => new(GraphElementKind.Transition, transitionId, fromQuestionId, text);

    /// <summary>A warning that hangs on a loop marker (range frame).</summary>
    /// <param name="loopId">The affected marker.</param>
    /// <param name="text">The warning text.</param>
    /// <returns>The located warning.</returns>
    public static GraphWarning ForLoop(Guid loopId, string text)
        => new(GraphElementKind.Loop, loopId, null, text);

    /// <summary>A warning that concerns the dialog as a whole (no single element).</summary>
    /// <param name="text">The warning text.</param>
    /// <returns>The warning without an element reference.</returns>
    public static GraphWarning ForDialog(string text)
        => new(GraphElementKind.Dialog, null, null, text);
}
