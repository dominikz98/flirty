using Flirty.Designer.Models;
using Flirty.Runtime.Admin;

namespace Flirty.Designer.Services;

/// <summary>
/// Wertet die Schleifen-Marker eines Dialogs für den Loop-Editor (#41) aus: Schleifen-Bereich (Body),
/// Rücksprung-/Exit-Übergänge und die Warnungen zu Konfigurationen, die zur Laufzeit anders wirken als
/// gedacht – allen voran der <b>Zyklus ohne erreichbaren Exit</b> (Endlosschleife).
/// </summary>
/// <remarks>
/// <para>
/// Die Body-Ermittlung spiegelt bewusst den Core-internen <c>LoopResolver</c>
/// (<c>src/Flirty/Runtime/LoopResolver.cs</c>): <c>(vorwärts ab Entry, Stopp an Breaking) ∩ (rückwärts zu
/// Breaking) ∪ {Entry, Breaking}</c>. Der Resolver selbst ist nicht wiederverwendbar – er ist
/// <c>internal</c> im Core und arbeitet auf einer <c>Dialog</c>-Entity mit geladenen Navigationen,
/// während der Designer nur die navigationsfreie Sicht <see cref="DialogDetail"/> hat. Dieselbe
/// Abgrenzung wie bei <see cref="DesignerExpressionContext"/> ↔ <c>SessionExpressionContextBuilder</c>;
/// gegen ein Auseinanderlaufen sichert ein Test in <c>tests/Flirty.Tests/Designer/LoopAnalyzerTests</c>,
/// der beide Implementierungen auf demselben Graphen vergleicht.
/// </para>
/// <para>
/// Die Erreichbarkeit des Exits spiegelt den <c>TransitionResolver</c>: Es gewinnt der erste
/// <b>nicht</b>-Default, dessen Bedingung zutrifft (ein leerer Ausdruck trifft immer zu), sonst der erste
/// Default. Ein bedingungsloser Rücksprung vor jedem Exit macht den Ausstieg daher unerreichbar.
/// </para>
/// </remarks>
internal static class LoopAnalyzer
{
    /// <summary>Analysiert alle Schleifen-Marker des Dialogs.</summary>
    /// <param name="detail">Der Dialog samt Graph (aus <c>GetDialogQuery</c>).</param>
    /// <returns>Je Marker ein Analyseergebnis, in der Reihenfolge von <see cref="DialogDetail.Loops"/>.</returns>
    public static IReadOnlyList<LoopInsight> Analyze(DialogDetail detail)
    {
        ArgumentNullException.ThrowIfNull(detail);

        var bodies = detail.Loops.ToDictionary(loop => loop.Id, loop => ComputeBody(detail, loop));

        return [.. detail.Loops.Select(loop => Describe(detail, loop, bodies))];
    }

    /// <summary>Analysiert einen einzelnen Schleifen-Marker des Dialogs.</summary>
    /// <param name="detail">Der Dialog samt Graph.</param>
    /// <param name="loopId">Der Primärschlüssel des Markers.</param>
    /// <returns>Das Analyseergebnis oder <see langword="null"/>, wenn der Dialog den Marker nicht enthält.</returns>
    public static LoopInsight? Analyze(DialogDetail detail, Guid loopId)
    {
        ArgumentNullException.ThrowIfNull(detail);

        return Analyze(detail).FirstOrDefault(insight => insight.Loop.Id == loopId);
    }

    /// <summary>
    /// Bestimmt den Schleifen-Bereich eines Markers – identisch zur Vorberechnung im
    /// Core-<c>LoopResolver</c>. Fehlt die Einstiegs- oder Breaking Question im Dialog, ist der Bereich
    /// leer (der Marker zeigt ins Leere und wird als Warnung ausgewiesen).
    /// </summary>
    /// <param name="detail">Der Dialog samt Graph.</param>
    /// <param name="loop">Der zu vermessende Marker.</param>
    /// <returns>Die Frage-Ids des Schleifenbereichs.</returns>
    public static HashSet<Guid> ComputeBody(DialogDetail detail, LoopDetail loop)
    {
        ArgumentNullException.ThrowIfNull(detail);
        ArgumentNullException.ThrowIfNull(loop);

        var known = detail.Questions.Select(question => question.Id).ToHashSet();
        if (!known.Contains(loop.EntryQuestionId) || !known.Contains(loop.BreakingQuestionId))
        {
            return [];
        }

        var forward = ReachableForward(detail, loop.EntryQuestionId, stopAt: loop.BreakingQuestionId);
        var backward = ReachableBackward(detail, loop.BreakingQuestionId);

        var body = new HashSet<Guid>();
        foreach (var questionId in forward)
        {
            if (backward.Contains(questionId))
            {
                body.Add(questionId);
            }
        }

        body.Add(loop.EntryQuestionId);
        body.Add(loop.BreakingQuestionId);
        return body;
    }

    private static LoopInsight Describe(
        DialogDetail detail, LoopDetail loop, IReadOnlyDictionary<Guid, HashSet<Guid>> bodies)
    {
        var body = bodies[loop.Id];
        var entry = Question(detail, loop.EntryQuestionId);
        var breaking = Question(detail, loop.BreakingQuestionId);

        var outgoing = breaking is null
            ? []
            : detail.Transitions
                .Where(transition => transition.FromQuestionId == breaking.Id)
                .OrderBy(transition => transition.Priority)
                .ToList();

        var loopBacks = outgoing.Where(transition => body.Contains(transition.TargetQuestionId)).ToList();
        var exits = outgoing.Where(transition => !body.Contains(transition.TargetQuestionId)).ToList();

        return new LoopInsight(
            loop,
            [.. detail.Questions.Where(question => body.Contains(question.Id))],
            entry,
            breaking,
            loopBacks,
            exits,
            Warnings(detail, loop, bodies, entry, breaking, outgoing, body, exits));
    }

    private static IReadOnlyList<string> Warnings(
        DialogDetail detail,
        LoopDetail loop,
        IReadOnlyDictionary<Guid, HashSet<Guid>> bodies,
        QuestionDetail? entry,
        QuestionDetail? breaking,
        IReadOnlyList<TransitionDetail> outgoing,
        IReadOnlySet<Guid> body,
        IReadOnlyList<TransitionDetail> exits)
    {
        var warnings = new List<string>();

        if (entry is null)
        {
            warnings.Add(
                "Die Einstiegsfrage gehört nicht (mehr) zu diesem Dialog – der Marker zeigt ins Leere und "
                + "sammelt nichts. Bitte eine vorhandene Frage wählen oder die Schleife löschen.");
        }

        if (breaking is null)
        {
            warnings.Add(
                "Die Breaking Question gehört nicht (mehr) zu diesem Dialog – ohne sie gibt es keinen "
                + "Ausstieg aus dem Zyklus. Bitte eine vorhandene Frage wählen oder die Schleife löschen.");
        }

        if (entry is not null && breaking is not null)
        {
            if (!outgoing.Any(transition => transition.TargetQuestionId == entry.Id))
            {
                warnings.Add(
                    $"Es gibt keinen Übergang von „{breaking.Key}“ zurück auf „{entry.Key}“ – ohne diesen "
                    + "Rücksprung entsteht gar kein Zyklus, und die Antworten werden nicht je Iteration "
                    + "gesammelt.");
            }

            if (exits.Count == 0)
            {
                warnings.Add(
                    $"Die Breaking Question „{breaking.Key}“ hat keinen Übergang aus dem Schleifenbereich "
                    + "heraus – die Schleife lässt sich nie verlassen (Endlosschleife).");
            }
            else if (!ExitIsReachable(outgoing, body))
            {
                warnings.Add(
                    $"Der Ausstieg aus „{breaking.Key}“ wird nie geprüft: Zur Laufzeit greift immer ein "
                    + "Rücksprung davor (der erste zutreffende Nicht-Default gewinnt, ein leerer Ausdruck "
                    + "trifft immer zu; sonst der oberste Default). Ergebnis ist eine Endlosschleife – "
                    + "dem Rücksprung eine Bedingung geben oder ihn hinter den Ausstieg sortieren.");
            }
        }

        foreach (var other in detail.Loops.Where(candidate => candidate.Id != loop.Id))
        {
            if (bodies[other.Id].Overlaps(bodies[loop.Id]))
            {
                warnings.Add(
                    $"Der Schleifenbereich überschneidet sich mit der Schleife „{other.CollectionKey}“. "
                    + "Verschachtelte oder überlappende Schleifen werden nicht unterstützt: Jede Session "
                    + "gegen diesen Dialog bricht schon beim Start mit einem Fehler ab.");
            }
        }

        if (!DesignerExpressionContext.IsBindable(loop.CollectionKey))
        {
            warnings.Add(
                $"Der Collection-Schlüssel „{loop.CollectionKey}“ ist im Ausdruck nicht referenzierbar: "
                + DesignerExpressionContext.IdentifierNote(loop.CollectionKey));
        }
        else if (detail.Questions.Any(question => string.Equals(question.Key, loop.CollectionKey, StringComparison.Ordinal)))
        {
            warnings.Add(
                $"Der Collection-Schlüssel „{loop.CollectionKey}“ verdeckt die gleichnamige Frage im "
                + "Ausdruckskontext – deren Antwort ist in Bedingungen dann nicht mehr erreichbar. Einen "
                + "der beiden Schlüssel umbenennen.");
        }

        return warnings;
    }

    /// <summary>
    /// Bildet die Auswahl des <c>TransitionResolver</c> nach und beantwortet, ob überhaupt ein Ausstieg
    /// greifen kann: Der erste bedingungslose Nicht-Default gewinnt immer; wird keiner erreicht, greift
    /// der oberste Default.
    /// </summary>
    private static bool ExitIsReachable(IReadOnlyList<TransitionDetail> outgoing, IReadOnlySet<Guid> body)
    {
        foreach (var transition in outgoing.Where(transition => !transition.IsDefault))
        {
            if (!body.Contains(transition.TargetQuestionId))
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(transition.Expression))
            {
                return false;
            }
        }

        var fallback = outgoing.FirstOrDefault(transition => transition.IsDefault);
        return fallback is not null && !body.Contains(fallback.TargetQuestionId);
    }

    private static QuestionDetail? Question(DialogDetail detail, Guid questionId)
        => detail.Questions.FirstOrDefault(question => question.Id == questionId);

    /// <summary>Vorwärts über ausgehende Übergänge erreichbare Fragen ab <paramref name="start"/>; expandiert <paramref name="stopAt"/> nicht.</summary>
    private static HashSet<Guid> ReachableForward(DialogDetail detail, Guid start, Guid stopAt)
    {
        var visited = new HashSet<Guid>();
        var stack = new Stack<Guid>();
        stack.Push(start);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!visited.Add(current) || current == stopAt)
            {
                continue;
            }

            foreach (var transition in detail.Transitions.Where(transition => transition.FromQuestionId == current))
            {
                stack.Push(transition.TargetQuestionId);
            }
        }

        return visited;
    }

    /// <summary>Fragen, von denen aus <paramref name="target"/> über Übergänge rückwärts erreichbar ist (inkl. <paramref name="target"/>).</summary>
    private static HashSet<Guid> ReachableBackward(DialogDetail detail, Guid target)
    {
        var visited = new HashSet<Guid>();
        var stack = new Stack<Guid>();
        stack.Push(target);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!visited.Add(current))
            {
                continue;
            }

            foreach (var transition in detail.Transitions.Where(transition => transition.TargetQuestionId == current))
            {
                stack.Push(transition.FromQuestionId);
            }
        }

        return visited;
    }
}
