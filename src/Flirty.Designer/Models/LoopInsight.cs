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
/// <param name="Warnings">Die gefundenen Warnungen (leer, wenn die Schleife stimmig konfiguriert ist).</param>
internal sealed record LoopInsight(
    LoopDetail Loop,
    IReadOnlyList<QuestionDetail> Body,
    QuestionDetail? EntryQuestion,
    QuestionDetail? BreakingQuestion,
    IReadOnlyList<TransitionDetail> LoopBackTransitions,
    IReadOnlyList<TransitionDetail> ExitTransitions,
    IReadOnlyList<string> Warnings);
