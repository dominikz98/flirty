using Flirty.Domain;

namespace Flirty.Designer.Models;

/// <summary>An answer given during the test run – the content of a visited node.</summary>
/// <param name="Sequence">The consecutive position within the session (identity for editing).</param>
/// <param name="IterationIndex">
/// The zero-based iteration index within a loop or <see langword="null"/> outside.
/// </param>
/// <param name="Value">The stored raw JSON answer value – the thing the conditions compute with.</param>
/// <param name="Display">The readable value (option label instead of raw value, <c>true</c> → "Ja").</param>
/// <param name="AnsweredAt">The point in time of capture.</param>
public sealed record GraphRunAnswer(
    int Sequence,
    int? IterationIndex,
    string Value,
    string Display,
    DateTimeOffset AnsweredAt);

/// <summary>
/// A node visited during the run: the question together with <b>all</b> answers that were given to it
/// in this run – within a loop therefore one per iteration.
/// </summary>
/// <param name="QuestionId">The visited question.</param>
/// <param name="Answers">The answers in the order of their <see cref="GraphRunAnswer.Sequence"/>.</param>
/// <param name="IsCurrent">
/// Whether the question is currently open. This is independent of <see cref="Answers"/>: the entry question is
/// open before it was answered, and a loop question is open again in the next iteration.
/// </param>
public sealed record GraphRunVisit(
    Guid QuestionId,
    IReadOnlyList<GraphRunAnswer> Answers,
    bool IsCurrent);

/// <summary>An edge taken during the run.</summary>
/// <remarks>
/// <see cref="IsAmbiguous"/> is the honest part: the engine does not record <b>which</b> transition
/// took effect (<c>SessionAnswer</c> carries no <c>TransitionId</c>). The path is derived from the
/// answer sequence, and that knows only the question pair. If there are several
/// transitions between the same two questions, they are thereby not distinguishable – then all are marked and all
/// reported as ambiguous, instead of asserting one of them.
/// </remarks>
/// <param name="TransitionId">The transition.</param>
/// <param name="Count">How often the associated question pair was traversed (several times in loops).</param>
/// <param name="IsAmbiguous">Whether several transitions lie between the same question pair.</param>
public sealed record GraphRunEdgeUse(Guid TransitionId, int Count, bool IsAmbiguous);

/// <summary>The run state of a loop – the number on the range frame.</summary>
/// <param name="LoopId">The loop marker.</param>
/// <param name="CollectionKey">Its collection key.</param>
/// <param name="Iterations">
/// The number of iterations of the <b>most recent</b> loop instance (the same selection as in the
/// core <c>LoopResolver</c>); <c>0</c> as long as the loop was not entered.
/// </param>
/// <param name="IsActive">Whether the currently open question lies within the range of this loop.</param>
/// <param name="Body">The questions of the range, in dialog order.</param>
public sealed record GraphRunLoopState(
    Guid LoopId,
    string CollectionKey,
    int Iterations,
    bool IsActive,
    IReadOnlyList<Guid> Body);

/// <summary>
/// A trigger event <b>published</b> during the run – the display form of an entry from the
/// <c>DesignerTriggerLog</c>.
/// </summary>
/// <param name="OccurredAt">The point in time of the observation.</param>
/// <param name="Scope">The associated triggering point in time.</param>
/// <param name="QuestionId">
/// The triggering question or <see langword="null"/> if the event hangs on no question (completion)
/// or the question no longer belongs to the dialog – then it is shown dialog-wide instead of concealed.
/// </param>
/// <param name="Label">The short label of the chip.</param>
/// <param name="Title">The full description for tooltip and screen reader.</param>
/// <param name="Detail">The short description of the event (as in the log of the list view).</param>
/// <param name="IsFresh">
/// Whether the event stems from the <b>last</b> step. Carries the brief flash at the triggering
/// node; the chips remain afterwards.
/// </param>
public sealed record GraphRunTrigger(
    DateTimeOffset OccurredAt,
    TriggerScope Scope,
    Guid? QuestionId,
    string Label,
    string Title,
    string Detail,
    bool IsFresh);

/// <summary>
/// The run state over the drawing model (#104): visited nodes, taken edges, iteration counts
/// and published triggers – the answer to "which path does the dialog take?".
/// </summary>
/// <remarks>
/// <para>
/// Deliberately a <b>separate</b> model beside <see cref="DialogGraphModel"/> rather than additional fields
/// in it: the editor view (#101–#103) knows no run, and the run state changes on every
/// step, while the drawing model only arises anew on a graph change. Common are only
/// the keys – question, transition and loop ids.
/// </para>
/// <para>
/// It is built by <see cref="Flirty.Designer.Services.GraphRunAnalyzer"/> after each engine step.
/// </para>
/// </remarks>
/// <param name="Status">The status of the session.</param>
/// <param name="CurrentQuestionId">The currently open question or <see langword="null"/>.</param>
/// <param name="Visits">The visited nodes in the order of their first visit.</param>
/// <param name="TakenEdges">The taken edges.</param>
/// <param name="Loops">The run state per loop marker, in the order of <c>DialogDetail.Loops</c>.</param>
/// <param name="Triggers">The published events in chronological order.</param>
/// <param name="Summary">The run in words – the alternative to the picture (screen reader).</param>
public sealed record GraphRunOverlay(
    SessionStatus Status,
    Guid? CurrentQuestionId,
    IReadOnlyList<GraphRunVisit> Visits,
    IReadOnlyList<GraphRunEdgeUse> TakenEdges,
    IReadOnlyList<GraphRunLoopState> Loops,
    IReadOnlyList<GraphRunTrigger> Triggers,
    string Summary)
{
    /// <summary>The number of answers captured so far – the step count of the run.</summary>
    public int Steps => Visits.Sum(visit => visit.Answers.Count);

    /// <summary>Finds the visit of a question.</summary>
    /// <param name="questionId">The sought question.</param>
    /// <returns>The visit or <see langword="null"/> if the question did not occur in the run.</returns>
    public GraphRunVisit? Visit(Guid questionId)
        => Visits.FirstOrDefault(visit => visit.QuestionId == questionId);

    /// <summary>Finds the use of an edge.</summary>
    /// <param name="transitionId">The sought transition.</param>
    /// <returns>The use or <see langword="null"/> if the transition did not take effect.</returns>
    public GraphRunEdgeUse? Edge(Guid transitionId)
        => TakenEdges.FirstOrDefault(edge => edge.TransitionId == transitionId);

    /// <summary>Finds the run state of a loop marker.</summary>
    /// <param name="loopId">The sought marker.</param>
    /// <returns>The state or <see langword="null"/>.</returns>
    public GraphRunLoopState? Loop(Guid loopId)
        => Loops.FirstOrDefault(loop => loop.LoopId == loopId);

    /// <summary>The events that hang on a particular question.</summary>
    /// <param name="questionId">The question.</param>
    /// <returns>The events in chronological order.</returns>
    public IReadOnlyList<GraphRunTrigger> TriggersOf(Guid questionId)
        => [.. Triggers.Where(trigger => trigger.QuestionId == questionId)];

    /// <summary>The events without a question reference – start and completion of the dialog.</summary>
    public IReadOnlyList<GraphRunTrigger> DialogTriggers
        => [.. Triggers.Where(trigger => trigger.QuestionId is null)];

    /// <summary>The loops in whose range a question lies.</summary>
    /// <param name="questionId">The question.</param>
    /// <returns>The loop states.</returns>
    public IReadOnlyList<GraphRunLoopState> LoopsOf(Guid questionId)
        => [.. Loops.Where(loop => loop.Body.Contains(questionId))];
}
