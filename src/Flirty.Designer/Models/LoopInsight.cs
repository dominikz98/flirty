using Flirty.Runtime.Admin;

namespace Flirty.Designer.Models;

/// <summary>
/// Das Analyseergebnis zu einem Schleifen-Marker (#41): der aus dem Übergangs-Graphen abgeleitete
/// Schleifen-Bereich, seine Rücksprung- und Exit-Übergänge sowie die Warnungen, die der Loop-Editor
/// anzeigt. Erzeugt von <see cref="Flirty.Designer.Services.LoopAnalyzer"/>.
/// </summary>
/// <param name="Loop">Der analysierte Schleifen-Marker.</param>
/// <param name="Body">
/// Die Fragen des Schleifenbereichs in Dialog-Reihenfolge (leer, wenn Einstiegs- oder Breaking Question
/// nicht mehr zum Dialog gehören).
/// </param>
/// <param name="EntryQuestion">Die Einstiegsfrage oder <see langword="null"/>, wenn sie nicht (mehr) existiert.</param>
/// <param name="BreakingQuestion">Die Breaking Question oder <see langword="null"/>, wenn sie nicht (mehr) existiert.</param>
/// <param name="LoopBackTransitions">
/// Die Übergänge der Breaking Question, deren Ziel <b>innerhalb</b> des Bereichs liegt (Rücksprünge),
/// in Auswertungsreihenfolge.
/// </param>
/// <param name="ExitTransitions">
/// Die Übergänge der Breaking Question, deren Ziel <b>außerhalb</b> des Bereichs liegt (Ausstiege),
/// in Auswertungsreihenfolge.
/// </param>
/// <param name="TargetedWarnings">
/// Die gefundenen Warnungen samt Ortsangabe (leer, wenn die Schleife stimmig konfiguriert ist). Seit
/// #101 tragen sie einen Elementbezug, damit die Graph-Ansicht sie am Rahmen, am Knoten oder an der
/// Kante zeigen kann; der Loop-Editor liest weiterhin nur <see cref="Warnings"/>.
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
    /// Die Warntexte in unveränderter Reihenfolge – die Sicht, die Loop- und Dialog-Editor seit #41
    /// anzeigen.
    /// </summary>
    public IReadOnlyList<string> Warnings => [.. TargetedWarnings.Select(warning => warning.Text)];
}
