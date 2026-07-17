using Flirty.Domain;
using Flirty.Expressions;

namespace Flirty.Runtime;

/// <summary>
/// Kapselt die gesamte Loop-Laufzeitlogik einer gepinnten Dialogversion (Issue #29): die Ermittlung
/// des Schleifen-Bereichs (Body) je <see cref="LoopDefinition"/>, die Zuordnung von
/// <see cref="SessionAnswer.LoopInstanceId"/>/<see cref="SessionAnswer.IterationIndex"/> beim
/// Persistieren einer Antwort sowie den Aufbau der je Iteration gesammelten Collections und des
/// aktuellen Iterationsindex für den <see cref="ExpressionContext"/>.
/// </summary>
/// <remarks>
/// Loops entstehen ausschließlich über das vorhandene Branching (eine <see cref="Transition"/> zeigt auf
/// eine frühere Frage = Zyklus); die <see cref="LoopDefinition"/> ist nur die Marker-Ebene darüber. Es gibt
/// bewusst keinen separaten Runtime-Sonderpfad (vgl. <c>docs/ARCHITECTURE.md</c> §10/§11.5). Der Body wird
/// einmalig im Konstruktor aus dem Übergangs-Graphen vorberechnet; die restlichen Operationen leiten ihren
/// Zustand aus den vorhandenen <see cref="SessionAnswer"/>-Zeilen ab (kein zusätzliches Session-Feld).
/// </remarks>
internal sealed class LoopResolver
{
    private readonly List<LoopScope> _loops;
    private readonly Dictionary<Guid, LoopDefinition> _loopByQuestion;

    /// <summary>
    /// Erstellt den Resolver für die angegebene gepinnte Dialogversion und berechnet den Body jeder
    /// Schleife aus deren Übergängen vor.
    /// </summary>
    /// <param name="dialog">Die gepinnte Dialogversion samt <see cref="Dialog.Loops"/> und <see cref="Dialog.Transitions"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="dialog"/> ist <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// Zwei Schleifen-Bereiche überlappen sich (verschachtelte/überlappende Loops werden im MVP nicht
    /// unterstützt).
    /// </exception>
    public LoopResolver(Dialog dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);

        _loops = [.. dialog.Loops.Select(loop => new LoopScope(loop, ComputeBody(dialog, loop)))];
        _loopByQuestion = new Dictionary<Guid, LoopDefinition>();

        foreach (var scope in _loops)
        {
            foreach (var questionId in scope.Body)
            {
                if (!_loopByQuestion.TryAdd(questionId, scope.Loop))
                {
                    throw new InvalidOperationException(
                        $"Die Frage '{questionId}' im Dialog '{dialog.Key}' gehört zu mehreren "
                        + "Schleifen-Bereichen; verschachtelte oder überlappende Loops werden nicht unterstützt.");
                }
            }
        }
    }

    /// <summary>
    /// Bestimmt die Loop-Zuordnung für eine <b>neu zu persistierende</b> Antwort auf
    /// <paramref name="questionId"/>. Muss vor dem Anhängen der neuen Antwort aufgerufen werden (rechnet
    /// auf dem Vor-Zustand der bereits gespeicherten Antworten).
    /// </summary>
    /// <param name="session">Die getrackte Session inkl. ihrer bisherigen Antworten.</param>
    /// <param name="questionId">Die Id der Frage, deren Antwort gleich persistiert wird.</param>
    /// <returns>
    /// Die zu setzende <see cref="LoopAssignment"/>. Außerhalb jeder Schleife sind beide Werte
    /// <see langword="null"/> (unverändertes Nicht-Loop-Verhalten).
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> ist <see langword="null"/>.</exception>
    public LoopAssignment ResolveAssignment(DialogSession session, Guid questionId)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (!_loopByQuestion.TryGetValue(questionId, out var loop))
        {
            return default;
        }

        var body = BodyOf(loop);
        var priorBodyAnswers = session.Answers
            .Where(answer => answer.LoopInstanceId is not null && body.Contains(answer.QuestionId))
            .OrderBy(answer => answer.Sequence)
            .ToList();

        // Erster Eintritt in die Schleife: frische Instanz, Iteration 0.
        if (priorBodyAnswers.Count == 0)
        {
            return new LoopAssignment(Guid.NewGuid(), 0);
        }

        var instanceId = priorBodyAnswers[^1].LoopInstanceId!.Value;
        var instanceAnswers = priorBodyAnswers.Where(answer => answer.LoopInstanceId == instanceId).ToList();
        var maxIteration = instanceAnswers.Max(answer => answer.IterationIndex ?? 0);

        // Loop-Back: Wird die Einstiegsfrage in der laufenden Iteration erneut beantwortet, beginnt die
        // nächste Iteration. Alle übrigen (Folge-)Fragen bleiben in der aktuellen Iteration.
        var startsNextIteration = questionId == loop.EntryQuestionId
            && instanceAnswers.Any(answer =>
                answer.QuestionId == loop.EntryQuestionId && answer.IterationIndex == maxIteration);

        return new LoopAssignment(instanceId, startsNextIteration ? maxIteration + 1 : maxIteration);
    }

    /// <summary>
    /// Baut die je Iteration gesammelten Loop-Collections für den <see cref="ExpressionContext"/>: je
    /// <see cref="LoopDefinition.CollectionKey"/> die <see cref="SessionAnswer.Value"/> der Einstiegsfrage
    /// je Iteration der jüngsten Loop-Instanz, geordnet nach <see cref="SessionAnswer.IterationIndex"/>.
    /// Jeder <see cref="LoopDefinition.CollectionKey"/> wird stets gebunden (leere Liste, solange die
    /// Schleife noch nicht betreten wurde), damit Ausdrücke wie <c>positions.Count &gt; 0</c> auch vor der
    /// ersten Iteration auswertbar sind.
    /// </summary>
    /// <param name="session">Die Session inkl. ihrer bisherigen Antworten.</param>
    /// <returns>Die Collections, indiziert nach <see cref="LoopDefinition.CollectionKey"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> ist <see langword="null"/>.</exception>
    public IReadOnlyDictionary<string, IReadOnlyList<string?>> BuildCollections(DialogSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var result = new Dictionary<string, IReadOnlyList<string?>>(StringComparer.Ordinal);

        foreach (var scope in _loops)
        {
            var bodyAnswers = session.Answers
                .Where(answer => answer.LoopInstanceId is not null && scope.Body.Contains(answer.QuestionId))
                .ToList();

            IReadOnlyList<string?> entries = [];
            if (bodyAnswers.Count > 0)
            {
                var instanceId = bodyAnswers.OrderByDescending(answer => answer.Sequence).First().LoopInstanceId!.Value;
                entries = bodyAnswers
                    .Where(answer => answer.LoopInstanceId == instanceId
                        && answer.QuestionId == scope.Loop.EntryQuestionId)
                    .OrderBy(answer => answer.IterationIndex ?? 0)
                    .Select(answer => (string?)answer.Value)
                    .ToList();
            }

            result[scope.Loop.CollectionKey] = entries;
        }

        return result;
    }

    /// <summary>
    /// Ermittelt den Iterationsindex der zuletzt gegebenen Antwort auf <paramref name="questionId"/>,
    /// sofern die Frage in einem Schleifen-Bereich liegt; sonst <see langword="null"/>.
    /// </summary>
    /// <param name="session">Die Session inkl. ihrer bisherigen Antworten.</param>
    /// <param name="questionId">Die Id der gerade beantworteten Frage.</param>
    /// <returns>Der aktuelle nullbasierte Iterationsindex oder <see langword="null"/> außerhalb einer Schleife.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> ist <see langword="null"/>.</exception>
    public int? ResolveIterationIndex(DialogSession session, Guid questionId)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (!_loopByQuestion.ContainsKey(questionId))
        {
            return null;
        }

        return session.Answers
            .Where(answer => answer.QuestionId == questionId && answer.IterationIndex is not null)
            .OrderByDescending(answer => answer.Sequence)
            .FirstOrDefault()?.IterationIndex;
    }

    private IReadOnlySet<Guid> BodyOf(LoopDefinition loop)
        => _loops.First(scope => ReferenceEquals(scope.Loop, loop)).Body;

    /// <summary>
    /// Berechnet den Schleifen-Bereich als <c>(vorwärts erreichbar ab Entry) ∩ (rückwärts erreichbar zu
    /// Breaking) ∪ {Entry, Breaking}</c>. Die Vorwärts-Suche stoppt an der Breaking Question (deren
    /// Loop-Back-/Exit-Kanten werden nicht verfolgt); dadurch bleiben früh aus dem Zyklus austretende Zweige
    /// (in F, nicht in B) und dem Zyklus vorgelagerte Fragen (in B, nicht in F) außerhalb des Body.
    /// </summary>
    private static HashSet<Guid> ComputeBody(Dialog dialog, LoopDefinition loop)
    {
        var forward = ReachableForward(dialog, loop.EntryQuestionId, stopAt: loop.BreakingQuestionId);
        var backward = ReachableBackward(dialog, loop.BreakingQuestionId);

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

    /// <summary>Vorwärts über ausgehende Übergänge erreichbare Fragen ab <paramref name="start"/>; expandiert <paramref name="stopAt"/> nicht.</summary>
    private static HashSet<Guid> ReachableForward(Dialog dialog, Guid start, Guid stopAt)
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

            foreach (var transition in dialog.Transitions.Where(t => t.FromQuestionId == current))
            {
                stack.Push(transition.TargetQuestionId);
            }
        }

        return visited;
    }

    /// <summary>Fragen, von denen aus <paramref name="target"/> über Übergänge rückwärts erreichbar ist (inkl. <paramref name="target"/>).</summary>
    private static HashSet<Guid> ReachableBackward(Dialog dialog, Guid target)
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

            foreach (var transition in dialog.Transitions.Where(t => t.TargetQuestionId == current))
            {
                stack.Push(transition.FromQuestionId);
            }
        }

        return visited;
    }

    /// <summary>Verknüpft eine <see cref="LoopDefinition"/> mit ihrem vorberechneten Schleifen-Bereich (Frage-Ids).</summary>
    private sealed record LoopScope(LoopDefinition Loop, HashSet<Guid> Body);
}

/// <summary>
/// Die beim Persistieren einer Antwort zu setzende Loop-Zuordnung: die Instanz-Id der Schleife und der
/// nullbasierte Iterationsindex. Beide sind <see langword="null"/>, wenn die Antwort außerhalb jeder
/// Schleife gegeben wird.
/// </summary>
/// <param name="LoopInstanceId">Die Instanz-Id der laufenden Schleife oder <see langword="null"/> außerhalb.</param>
/// <param name="IterationIndex">Der nullbasierte Iterationsindex oder <see langword="null"/> außerhalb.</param>
internal readonly record struct LoopAssignment(Guid? LoopInstanceId, int? IterationIndex);
