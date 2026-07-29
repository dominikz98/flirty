using Flirty.Designer.Models;
using Flirty.Runtime.Admin;

namespace Flirty.Designer.Services;

/// <summary>
/// Sums up the warnings of a graph as a text list – each with its <b>cause</b> in front.
/// This is the version that the <c>DialogEditor</c> shows in the publish section and on which the
/// confirmation before publishing hangs.
/// </summary>
/// <remarks>
/// <para>
/// The source is <see cref="DialogGraphModel.AllWarnings"/> and thus the <b>whole</b> graph: dialog,
/// nodes (including reachability), edges and loops. Up to #118 the confirmation was fed
/// only from the <see cref="TransitionWarningAnalyzer"/>; an unreachable question – clearly indicated by
/// the graph – could therefore be published without a confirmation. The defect was not the
/// one missing warning, but the <b>hand-picked selection</b>: every future warning kind would have
/// fallen out again. Via <see cref="DialogGraphModel.AllWarnings"/> the list is structurally
/// closed.
/// </para>
/// <para>
/// Own service and not in the <c>@code</c> block, because <c>tests/Flirty.Tests/Designer</c> renders no
/// components (no bUnit): what lies in the Razor is not checkable. The same delimitation as with
/// <see cref="GraphEditing"/>.
/// </para>
/// <para>
/// The <b>wordings</b> do not arise here – they come unchanged from
/// <see cref="TransitionWarningAnalyzer"/>, <see cref="LoopAnalyzer"/> and
/// <see cref="DialogGraphBuilder"/> and are a contract towards tests and the E2E suite. This class sets
/// only the prefix in front.
/// </para>
/// </remarks>
internal static class GraphWarningList
{
    /// <summary>
    /// Describes all warnings of the graph as a list, each with the key of its cause in front.
    /// </summary>
    /// <param name="detail">The dialog together with the graph – source of the question and loop keys.</param>
    /// <param name="model">The drawing model from which the warnings stem.</param>
    /// <returns>
    /// The warnings in the order of <see cref="DialogGraphModel.AllWarnings"/>; empty if the
    /// graph is coherent.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="detail"/> or <paramref name="model"/> is <see langword="null"/>.
    /// </exception>
    public static IReadOnlyList<string> Describe(DialogDetail detail, DialogGraphModel model)
    {
        ArgumentNullException.ThrowIfNull(detail);
        ArgumentNullException.ThrowIfNull(model);

        return [.. model.AllWarnings.Select(warning => Line(detail, warning))];
    }

    /// <summary>Prepends a warning with its cause – provided it has one.</summary>
    private static string Line(DialogDetail detail, GraphWarning warning)
    {
        var origin = Origin(detail, warning);
        return origin is null ? warning.Text : $"{origin}: {warning.Text}";
    }

    /// <summary>
    /// The key of the causing element, or <see langword="null"/> for a warning on the dialog
    /// as a whole.
    /// </summary>
    /// <remarks>
    /// <see cref="GraphWarning.QuestionId"/> already carries the reference question – for a question it itself,
    /// for a transition its source question. Only the loop marker has none (it hangs on the frame,
    /// not on a question) and is named via its <c>CollectionKey</c>; a warning on the dialog
    /// stays without a prefix, because its cause is the dialog itself.
    /// </remarks>
    private static string? Origin(DialogDetail detail, GraphWarning warning)
        => warning switch
        {
            { QuestionId: { } questionId } => QuestionKey(detail, questionId),
            { Kind: GraphElementKind.Loop, ElementId: { } loopId } => LoopKey(detail, loopId),
            _ => null,
        };

    /// <summary>
    /// The domain key of a question. The fallback is deliberate: a reference to a question that
    /// no longer exists is itself a finding and must not disappear as an empty prefix.
    /// </summary>
    private static string QuestionKey(DialogDetail detail, Guid questionId)
        => detail.Questions.FirstOrDefault(question => question.Id == questionId)?.Key
            ?? $"unbekannt ({questionId})";

    /// <summary>The <c>CollectionKey</c> of a loop marker, with the same fallback.</summary>
    private static string LoopKey(DialogDetail detail, Guid loopId)
        => detail.Loops.FirstOrDefault(loop => loop.Id == loopId)?.CollectionKey
            ?? $"unbekannt ({loopId})";
}
