using Flirty.Runtime.Admin;

namespace Flirty.Designer.Models;

/// <summary>
/// The analysis result for a loop marker (#41): the loop range derived from the transition graph,
/// its back-jump and exit transitions as well as the warnings that the loop editor
/// shows. Produced by <see cref="Flirty.Designer.Services.LoopAnalyzer"/>.
/// </summary>
/// <param name="Loop">The analyzed loop marker.</param>
/// <param name="Body">
/// The questions of the loop range in dialog order (empty if the entry or breaking question
/// no longer belong to the dialog).
/// </param>
/// <param name="EntryQuestion">The entry question or <see langword="null"/> if it no longer exists.</param>
/// <param name="BreakingQuestion">The breaking question or <see langword="null"/> if it no longer exists.</param>
/// <param name="LoopBackTransitions">
/// The transitions of the breaking question whose target lies <b>within</b> the range (back-jumps),
/// in evaluation order.
/// </param>
/// <param name="ExitTransitions">
/// The transitions of the breaking question whose target lies <b>outside</b> the range (exits),
/// in evaluation order.
/// </param>
/// <param name="TargetedWarnings">
/// The found warnings together with a location (empty if the loop is coherently configured). Since
/// #101 they carry an element reference, so that the graph view can show them at the frame, at the node or at the
/// edge; the loop editor still reads only <see cref="Warnings"/>.
/// </param>
internal sealed record LoopInsight(
    LoopDetail Loop,
    IReadOnlyList<QuestionDetail> Body,
    QuestionDetail? EntryQuestion,
    QuestionDetail? BreakingQuestion,
    IReadOnlyList<TransitionDetail> LoopBackTransitions,
    IReadOnlyList<TransitionDetail> ExitTransitions,
    IReadOnlyList<GraphWarning> TargetedWarnings)
{
    /// <summary>
    /// The warning texts in unchanged order – the view that loop and dialog editor have shown since
    /// #41.
    /// </summary>
    public IReadOnlyList<string> Warnings => [.. TargetedWarnings.Select(warning => warning.Text)];
}
