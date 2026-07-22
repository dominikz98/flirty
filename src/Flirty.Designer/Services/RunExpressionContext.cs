using Flirty.Runtime;
using Flirty.Runtime.Admin;

namespace Flirty.Designer.Services;

/// <summary>
/// Momentaufnahme der Ausdrucks-Bindungen eines laufenden Testlaufs – dieselben drei Bausteine, die der
/// Core zur Laufzeit in den <c>ExpressionContext</c> stellt.
/// </summary>
/// <param name="Answers">
/// Die zuletzt gegebene Antwort je Frage, indiziert nach dem fachlichen Frage-Schlüssel; Werte sind
/// roher JSON-Text.
/// </param>
/// <param name="Collections">
/// Die je Iteration gesammelten Antworten der Schleifen, indiziert nach dem <c>CollectionKey</c>.
/// </param>
/// <param name="IterationIndex">
/// Der nullbasierte Iterationsindex der aktuell offenen Frage oder <see langword="null"/>, wenn sie
/// außerhalb einer Schleife liegt bzw. keine Frage offen ist.
/// </param>
internal sealed record RunExpressionSnapshot(
    IReadOnlyDictionary<string, string?> Answers,
    IReadOnlyDictionary<string, IReadOnlyList<string?>> Collections,
    int? IterationIndex);

/// <summary>
/// Baut die Ausdrucks-Bindungen eines laufenden Testlaufs (#43) auf, damit der Test-Runner zeigen kann,
/// <b>womit</b> die Übergangs- und Trigger-Bedingungen gerade rechnen.
/// </summary>
/// <remarks>
/// <para>
/// Spiegelt bewusst den Core-internen <c>SessionExpressionContextBuilder</c>
/// (<c>src/Flirty/Runtime/SessionExpressionContextBuilder.cs</c>) samt der von ihm genutzten
/// <c>LoopResolver</c>-Regeln: der Resolver ist <c>internal</c> und arbeitet auf einer <c>Dialog</c>-Entity
/// mit geladenen Navigationen, während der Designer nur die navigationsfreien Sichten
/// <see cref="DialogDetail"/> und <see cref="ResumeDialogResult"/> hat. Dieselbe Abgrenzung wie bei
/// <see cref="DesignerExpressionContext"/> und <see cref="LoopAnalyzer"/>; gegen ein Auseinanderlaufen
/// sichert ein Test in <c>tests/Flirty.Tests/Designer/RunExpressionContextTests</c>, der beide
/// Implementierungen auf demselben Graphen und derselben Session vergleicht.
/// </para>
/// <para>
/// Die Bereichsermittlung der Schleifen kommt aus <see cref="LoopAnalyzer.ComputeBody"/> – nicht noch
/// einmal nachgebaut.
/// </para>
/// </remarks>
internal static class RunExpressionContext
{
    /// <summary>
    /// Baut die Momentaufnahme aus dem Dialog-Graphen und dem gelesenen Session-Zustand.
    /// </summary>
    /// <param name="detail">Der Dialog samt Graph (aus <c>GetDialogQuery</c>).</param>
    /// <param name="state">Der Session-Zustand (aus <c>ResumeDialogQuery</c>).</param>
    /// <returns>Die Bindungen zum aktuellen Zeitpunkt des Laufs.</returns>
    public static RunExpressionSnapshot Build(DialogDetail detail, ResumeDialogResult state)
    {
        ArgumentNullException.ThrowIfNull(detail);
        ArgumentNullException.ThrowIfNull(state);

        var known = detail.Questions.Select(question => question.Id).ToHashSet();

        // Je Frage die Antwort mit der höchsten Sequence – innerhalb einer Schleife also die der
        // aktuellen Iteration (identisch zum SessionExpressionContextBuilder).
        var answers = state.Answers
            .Where(answer => known.Contains(answer.QuestionId))
            .GroupBy(answer => answer.QuestionId)
            .ToDictionary(
                group => group.OrderByDescending(answer => answer.Sequence).First().QuestionKey,
                group => (string?)group.OrderByDescending(answer => answer.Sequence).First().Value,
                StringComparer.Ordinal);

        var collections = new Dictionary<string, IReadOnlyList<string?>>(StringComparer.Ordinal);
        foreach (var loop in detail.Loops)
        {
            collections[loop.CollectionKey] = CollectEntries(detail, state, loop);
        }

        return new RunExpressionSnapshot(answers, collections, ResolveIterationIndex(state));
    }

    /// <summary>
    /// Sammelt die Werte einer Schleifen-Collection: die Antworten der <b>Einstiegsfrage</b> in der
    /// jüngsten Loop-Instanz, nach Iterationsindex geordnet. Solange die Schleife nicht betreten wurde,
    /// bleibt die Liste leer – der Schlüssel wird trotzdem gebunden, sonst wäre
    /// <c>skills.Count &gt; 0</c> vor der ersten Iteration nicht auswertbar.
    /// </summary>
    /// <param name="detail">Der Dialog samt Graph.</param>
    /// <param name="state">Der Session-Zustand.</param>
    /// <param name="loop">Der Schleifen-Marker.</param>
    /// <returns>Die gesammelten Rohwerte je Iteration.</returns>
    private static IReadOnlyList<string?> CollectEntries(
        DialogDetail detail, ResumeDialogResult state, LoopDetail loop)
    {
        var body = LoopAnalyzer.ComputeBody(detail, loop);

        var bodyAnswers = state.Answers
            .Where(answer => answer.LoopInstanceId is not null && body.Contains(answer.QuestionId))
            .ToList();

        if (bodyAnswers.Count == 0)
        {
            return [];
        }

        var instanceId = bodyAnswers.OrderByDescending(answer => answer.Sequence).First().LoopInstanceId!.Value;

        return
        [
            .. bodyAnswers
                .Where(answer => answer.LoopInstanceId == instanceId
                    && answer.QuestionId == loop.EntryQuestionId)
                .OrderBy(answer => answer.IterationIndex ?? 0)
                .Select(answer => (string?)answer.Value),
        ];
    }

    /// <summary>
    /// Der Iterationsindex, mit dem die Bedingungen der aktuell offenen Frage rechnen: der zuletzt
    /// vergebene Index dieser Frage. Ohne offene Frage (abgeschlossene Session) oder außerhalb einer
    /// Schleife ist er <see langword="null"/>.
    /// </summary>
    /// <param name="state">Der Session-Zustand.</param>
    /// <returns>Der Iterationsindex oder <see langword="null"/>.</returns>
    private static int? ResolveIterationIndex(ResumeDialogResult state)
    {
        if (state.CurrentQuestion is not { } current)
        {
            return null;
        }

        return state.Answers
            .Where(answer => answer.QuestionId == current.Id && answer.IterationIndex is not null)
            .OrderByDescending(answer => answer.Sequence)
            .FirstOrDefault()?.IterationIndex;
    }
}
