using Flirty.Designer.Models;
using Flirty.Domain;
using Flirty.Runtime;
using Flirty.Runtime.Admin;

namespace Flirty.Designer.Services;

/// <summary>
/// Leitet aus dem Zustand einer laufenden Test-Session (#43) den <b>Laufzustand über dem Graphen</b> ab
/// (#104): besuchte Knoten, gegriffene Kanten, Iterationszahl je Schleife und die publizierten Trigger.
/// </summary>
/// <remarks>
/// <para>
/// <b>Die Engine protokolliert keinen Pfad.</b> Ein <c>SessionAnswer</c> trägt keine <c>TransitionId</c>,
/// und auch <c>QuestionAnsweredNotification</c> nennt nur die nächste <i>Frage</i>. Abgeleitet wird der
/// Weg deshalb aus der Antwortfolge: Zwei aufeinanderfolgende Antworten bilden das Paar
/// <i>(von, nach)</i>, und die zuletzt gegebene Antwort bildet zusammen mit der offenen Frage das letzte
/// Paar. Liegen zwischen denselben zwei Fragen <b>mehrere</b> Übergänge, ist nicht entscheidbar, welcher
/// gegriffen hat – dann werden alle markiert und als mehrdeutig ausgewiesen
/// (<see cref="GraphRunEdgeUse.IsAmbiguous"/>). Die Auswertung nachzustellen wäre eine weitere Spiegelung
/// des Core-<c>TransitionResolver</c>, und zwar eine unmögliche: Sie bräuchte die Ausdruckswerte von
/// <i>damals</i>, nicht die von jetzt.
/// </para>
/// <para>
/// Was es schon gibt, wird nicht nachgebaut: Der Schleifen-Bereich kommt aus
/// <see cref="LoopAnalyzer.ComputeBody"/>, die Auswahl der jüngsten Schleifen-Instanz folgt derselben
/// Regel wie der Core-<c>LoopResolver</c> (und wie <see cref="RunExpressionContext"/>), die lesbaren
/// Antwortwerte liefert <see cref="AnswerValueCodec.Describe"/>.
/// </para>
/// <para>
/// Nach außen gehen ausschließlich <b>Listen</b> – wie beim Zeichenmodell (#101): Die
/// Iterationsreihenfolge einer Menge oder eines Wörterbuchs ist nicht zugesichert, und ein Lauf wird
/// gerendert.
/// </para>
/// </remarks>
internal static class GraphRunAnalyzer
{
    /// <summary>
    /// Der Laufzustand vor dem ersten Lauf: nichts besucht, nichts gegriffen. Damit zeigt die Ansicht
    /// den Graphen schon vor dem Start, ohne dass Canvas und Inspector einen <see langword="null"/>-Fall
    /// kennen müssen.
    /// </summary>
    /// <returns>Der leere Laufzustand.</returns>
    public static GraphRunOverlay NotStarted()
        => new(SessionStatus.InProgress, null, [], [], [], [], "Testlauf: noch nicht gestartet.");

    /// <summary>Baut den Laufzustand.</summary>
    /// <param name="detail">Der Dialog samt Graph (aus <c>GetDialogQuery</c>).</param>
    /// <param name="state">Der Session-Zustand (aus <c>ResumeDialogQuery</c>).</param>
    /// <param name="events">Die im Lauf beobachteten Trigger-Ereignisse (<see cref="DesignerTriggerLog"/>).</param>
    /// <param name="freshFrom">
    /// Der Index, ab dem die Ereignisse aus dem <b>letzten</b> Schritt stammen – alles davor ist älter.
    /// Der Aufrufer merkt sich dafür den Stand des Protokolls, bevor er die Engine ruft.
    /// </param>
    /// <returns>Der Laufzustand.</returns>
    public static GraphRunOverlay Build(
        DialogDetail detail,
        ResumeDialogResult state,
        IReadOnlyList<DesignerTriggerEntry> events,
        int freshFrom = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(detail);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(events);

        var questions = detail.Questions.ToDictionary(question => question.Id);
        var answers = state.Answers.OrderBy(answer => answer.Sequence).ToList();

        var visits = BuildVisits(questions, answers, state.CurrentQuestion?.Id);
        var edges = BuildEdges(detail, answers, state.CurrentQuestion?.Id);
        var loops = BuildLoops(detail, answers, state.CurrentQuestion?.Id);
        var triggers = BuildTriggers(questions, events, freshFrom);

        return new GraphRunOverlay(
            state.Status,
            state.CurrentQuestion?.Id,
            visits,
            edges,
            loops,
            triggers,
            Summarize(state, answers.Count, loops));
    }

    // ---- Besuchte Knoten ----------------------------------------------------------------------------

    /// <summary>
    /// Fasst die Antworten je Frage zusammen – in Schleifen also mehrere je Knoten, eine je Iteration.
    /// </summary>
    /// <remarks>
    /// Die offene Frage bekommt auch dann einen Eintrag, wenn sie noch nicht beantwortet wurde: Sie ist
    /// der Punkt, an dem der Lauf steht, und ohne Eintrag wäre die Einstiegsfrage direkt nach dem Start
    /// nicht als offen markierbar. Antworten auf Fragen, die nicht (mehr) zum Dialog gehören, fallen
    /// heraus – gezeichnet werden kann nur, was es im Graphen gibt.
    /// </remarks>
    private static IReadOnlyList<GraphRunVisit> BuildVisits(
        IReadOnlyDictionary<Guid, QuestionDetail> questions,
        IReadOnlyList<SessionAnswerView> answers,
        Guid? currentQuestionId)
    {
        var order = new List<Guid>();
        var grouped = new Dictionary<Guid, List<GraphRunAnswer>>();

        foreach (var answer in answers)
        {
            if (!questions.TryGetValue(answer.QuestionId, out var question))
            {
                continue;
            }

            if (!grouped.TryGetValue(answer.QuestionId, out var list))
            {
                list = [];
                grouped[answer.QuestionId] = list;
                order.Add(answer.QuestionId);
            }

            list.Add(new GraphRunAnswer(
                answer.Sequence,
                answer.IterationIndex,
                answer.Value,
                AnswerValueCodec.Describe(question, answer.Value),
                answer.AnsweredAt));
        }

        if (currentQuestionId is { } current && questions.ContainsKey(current) && !grouped.ContainsKey(current))
        {
            grouped[current] = [];
            order.Add(current);
        }

        return [.. order.Select(id => new GraphRunVisit(id, grouped[id], id == currentQuestionId))];
    }

    // ---- Gegriffene Kanten --------------------------------------------------------------------------

    /// <summary>
    /// Bildet die Schrittfolge auf die Kanten des Graphen ab. Siehe Klassenkommentar zur Mehrdeutigkeit
    /// paralleler Übergänge.
    /// </summary>
    private static IReadOnlyList<GraphRunEdgeUse> BuildEdges(
        DialogDetail detail, IReadOnlyList<SessionAnswerView> answers, Guid? currentQuestionId)
    {
        var steps = new List<(Guid From, Guid To)>();
        for (var index = 1; index < answers.Count; index++)
        {
            steps.Add((answers[index - 1].QuestionId, answers[index].QuestionId));
        }

        if (answers.Count > 0 && currentQuestionId is { } current)
        {
            steps.Add((answers[^1].QuestionId, current));
        }

        var uses = new List<GraphRunEdgeUse>();
        foreach (var pair in steps.Distinct())
        {
            var candidates = detail.Transitions
                .Where(transition => transition.FromQuestionId == pair.From
                    && transition.TargetQuestionId == pair.To)
                .OrderBy(transition => transition.Priority)
                .ThenBy(transition => transition.Id)
                .ToArray();

            var count = steps.Count(step => step == pair);

            uses.AddRange(candidates.Select(
                transition => new GraphRunEdgeUse(transition.Id, count, candidates.Length > 1)));
        }

        return uses;
    }

    // ---- Schleifen ----------------------------------------------------------------------------------

    /// <summary>
    /// Zählt die Iterationen je Marker: die Antworten der <b>jüngsten</b> Schleifen-Instanz, deren
    /// höchster Iterationsindex plus eins. Dieselbe Auswahl trifft der Core-<c>LoopResolver</c>, wenn er
    /// die Collection für den Ausdruckskontext füllt.
    /// </summary>
    private static IReadOnlyList<GraphRunLoopState> BuildLoops(
        DialogDetail detail, IReadOnlyList<SessionAnswerView> answers, Guid? currentQuestionId)
    {
        var states = new List<GraphRunLoopState>(detail.Loops.Count);

        foreach (var loop in detail.Loops)
        {
            var body = LoopAnalyzer.ComputeBody(detail, loop);

            var bodyAnswers = answers
                .Where(answer => answer.LoopInstanceId is not null && body.Contains(answer.QuestionId))
                .ToList();

            var iterations = 0;
            if (bodyAnswers.Count > 0)
            {
                var instanceId = bodyAnswers[^1].LoopInstanceId!.Value;
                iterations = bodyAnswers
                    .Where(answer => answer.LoopInstanceId == instanceId)
                    .Max(answer => answer.IterationIndex ?? 0) + 1;
            }

            states.Add(new GraphRunLoopState(
                loop.Id,
                loop.CollectionKey,
                iterations,
                currentQuestionId is { } current && body.Contains(current),

                // Die Menge kommt sortiert nach der Dialog-Reihenfolge nach außen: Ihre eigene
                // Iterationsreihenfolge ist nicht zugesichert, und der Inspector listet sie.
                [.. detail.Questions.Where(question => body.Contains(question.Id)).Select(question => question.Id)]));
        }

        return states;
    }

    // ---- Trigger ------------------------------------------------------------------------------------

    /// <summary>
    /// Übersetzt die Protokolleinträge in Chips. Ein Eintrag zu einer Frage, die es im Dialog nicht
    /// (mehr) gibt, verliert seinen Ortsbezug und wird dialogweit gezeigt – verschweigen wäre falsch,
    /// gefeuert hat er ja.
    /// </summary>
    private static IReadOnlyList<GraphRunTrigger> BuildTriggers(
        IReadOnlyDictionary<Guid, QuestionDetail> questions,
        IReadOnlyList<DesignerTriggerEntry> events,
        int freshFrom)
    {
        var triggers = new List<GraphRunTrigger>(events.Count);

        for (var index = 0; index < events.Count; index++)
        {
            var entry = events[index];
            var questionId = entry.QuestionId is { } id && questions.ContainsKey(id) ? id : (Guid?)null;

            triggers.Add(new GraphRunTrigger(
                entry.OccurredAt,
                entry.Scope,
                questionId,
                ShortLabel(entry.Scope),
                $"{TriggerLabels.Describe(entry.Scope)} · {entry.Detail}",
                entry.Detail,
                index >= freshFrom));
        }

        return triggers;
    }

    /// <summary>
    /// Die kurze Beschriftung eines Ereignis-Chips. Bewusst nicht <see cref="TriggerLabels.Describe(TriggerScope)"/>:
    /// dessen Text nennt zusätzlich den technischen Namen und sprengt einen Chip auf der Knotenkarte. Der
    /// vollständige Text steht im <c>title</c>.
    /// </summary>
    /// <param name="scope">Der Auslöse-Zeitpunkt.</param>
    /// <returns>Die Beschriftung.</returns>
    private static string ShortLabel(TriggerScope scope) => scope switch
    {
        TriggerScope.OnDialogStarted => "Start",
        TriggerScope.AfterAnswer => "Antwort",
        TriggerScope.AfterQuestion => "Nach Frage",
        TriggerScope.OnDialogCompleted => "Abschluss",
        _ => scope.ToString(),
    };

    // ---- Zusammenfassung ----------------------------------------------------------------------------

    /// <summary>
    /// Fasst den Lauf in einem Satz zusammen – für Screenreader die Alternative zur Hervorhebung, die
    /// sonst nur als Farbe und Strichstärke vorliegt.
    /// </summary>
    private static string Summarize(
        ResumeDialogResult state, int answers, IReadOnlyList<GraphRunLoopState> loops)
    {
        var parts = new List<string> { Count(answers, "Antwort", "Antworten") };

        parts.Add(state.CurrentQuestion is { } current
            ? $"offene Frage {current.Key}"
            : state.Status switch
            {
                SessionStatus.Completed => "abgeschlossen",
                SessionStatus.Abandoned => "abgebrochen",
                _ => "keine offene Frage",
            });

        parts.AddRange(loops
            .Where(loop => loop.Iterations > 0)
            .Select(loop => $"{loop.CollectionKey}: {Count(loop.Iterations, "Iteration", "Iterationen")}"));

        return $"Testlauf: {string.Join(", ", parts)}.";
    }

    private static string Count(int value, string singular, string plural)
        => $"{value} {(value == 1 ? singular : plural)}";
}
