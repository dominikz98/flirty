using Flirty.Designer.Models;
using Flirty.Domain;
using Flirty.Runtime;
using Flirty.Runtime.Admin;

namespace Flirty.Designer.Services;

/// <summary>
/// Derives from the state of a running test session (#43) the <b>run state over the graph</b>
/// (#104): visited nodes, taken edges, iteration count per loop and the published triggers.
/// </summary>
/// <remarks>
/// <para>
/// <b>The engine logs no path.</b> A <c>SessionAnswer</c> carries no <c>TransitionId</c>,
/// and <c>QuestionAnsweredNotification</c> too names only the next <i>question</i>. The
/// path is therefore derived from the answer sequence: two consecutive answers form the pair
/// <i>(from, to)</i>, and the last given answer forms together with the open question the last
/// pair. If <b>several</b> transitions lie between the same two questions, it is not decidable which one
/// took effect – then all are marked and reported as ambiguous
/// (<see cref="GraphRunEdgeUse.IsAmbiguous"/>). Recomputing the evaluation would be another mirror
/// of the core <c>TransitionResolver</c>, and an impossible one at that: it would need the expression values from
/// <i>back then</i>, not the ones from now.
/// </para>
/// <para>
/// What already exists is not rebuilt: the loop range comes from
/// <see cref="LoopAnalyzer.ComputeBody"/>, the selection of the most recent loop instance follows the same
/// rule as the core <c>LoopResolver</c> (and as <see cref="RunExpressionContext"/>), the readable
/// answer values are delivered by <see cref="AnswerValueCodec.Describe"/>.
/// </para>
/// <para>
/// To the outside go exclusively <b>lists</b> – as with the drawing model (#101): the
/// iteration order of a set or a dictionary is not guaranteed, and a run is
/// rendered.
/// </para>
/// </remarks>
internal static class GraphRunAnalyzer
{
    /// <summary>
    /// The run state before the first run: nothing visited, nothing taken. With that the view shows
    /// the graph already before the start, without canvas and inspector having to know a <see langword="null"/>
    /// case.
    /// </summary>
    /// <returns>The empty run state.</returns>
    public static GraphRunOverlay NotStarted()
        => new(SessionStatus.InProgress, null, [], [], [], [], "Testlauf: noch nicht gestartet.");

    /// <summary>Builds the run state.</summary>
    /// <param name="detail">The dialog together with the graph (from <c>GetDialogQuery</c>).</param>
    /// <param name="state">The session state (from <c>ResumeDialogQuery</c>).</param>
    /// <param name="events">The trigger events observed during the run (<see cref="DesignerTriggerLog"/>).</param>
    /// <param name="freshFrom">
    /// The index from which the events stem from the <b>last</b> step – everything before is older.
    /// The caller for this remembers the state of the log before it calls the engine.
    /// </param>
    /// <returns>The run state.</returns>
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

    // ---- Visited nodes ----------------------------------------------------------------------------

    /// <summary>
    /// Sums up the answers per question – in loops therefore several per node, one per iteration.
    /// </summary>
    /// <remarks>
    /// The open question also gets an entry when it has not yet been answered: it is
    /// the point at which the run stands, and without an entry the entry question directly after the start
    /// could not be marked as open. Answers to questions that no longer belong to the dialog fall
    /// out – only what exists in the graph can be drawn.
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

    // ---- Taken edges --------------------------------------------------------------------------------

    /// <summary>
    /// Maps the step sequence onto the edges of the graph. See class comment on the ambiguity
    /// of parallel transitions.
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

    // ---- Loops ----------------------------------------------------------------------------------

    /// <summary>
    /// Counts the iterations per marker: the answers of the <b>most recent</b> loop instance, their
    /// highest iteration index plus one. The same selection is made by the core <c>LoopResolver</c> when it
    /// fills the collection for the expression context.
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

                // The set comes out sorted by dialog order: its own
                // iteration order is not guaranteed, and the inspector lists it.
                [.. detail.Questions.Where(question => body.Contains(question.Id)).Select(question => question.Id)]));
        }

        return states;
    }

    // ---- Triggers ------------------------------------------------------------------------------------

    /// <summary>
    /// Translates the log entries into chips. An entry for a question that no longer exists in the dialog
    /// loses its location reference and is shown dialog-wide – concealing would be wrong,
    /// it did fire after all.
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
    /// The short label of an event chip. Deliberately not <see cref="TriggerLabels.Describe(TriggerScope)"/>:
    /// its text additionally names the technical name and bursts a chip on the node card. The
    /// full text stands in the <c>title</c>.
    /// </summary>
    /// <param name="scope">The triggering point in time.</param>
    /// <returns>The label.</returns>
    private static string ShortLabel(TriggerScope scope) => scope switch
    {
        TriggerScope.OnDialogStarted => "Start",
        TriggerScope.AfterAnswer => "Antwort",
        TriggerScope.AfterQuestion => "Nach Frage",
        TriggerScope.OnDialogCompleted => "Abschluss",
        _ => scope.ToString(),
    };

    // ---- Summary ----------------------------------------------------------------------------

    /// <summary>
    /// Sums up the run in one sentence – for screen readers the alternative to the highlighting, which
    /// otherwise is present only as color and stroke width.
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
