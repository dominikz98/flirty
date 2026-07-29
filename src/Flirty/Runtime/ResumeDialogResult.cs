using Flirty.Domain;

namespace Flirty.Runtime;

/// <summary>
/// Result of <see cref="ResumeDialogQuery"/> or <see cref="IFlirtyEngine.ResumeDialogAsync"/>:
/// the current state of a <see cref="DialogSession"/> – status, the (possibly) currently open question and
/// the answers given so far – for restoring a survey (e.g. after a reload of the
/// host app).
/// </summary>
/// <param name="SessionId">The primary key of the queried <see cref="DialogSession"/>.</param>
/// <param name="Status">
/// The current status of the session (<see cref="SessionStatus.InProgress"/>,
/// <see cref="SessionStatus.Completed"/> or <see cref="SessionStatus.Abandoned"/>).
/// </param>
/// <param name="CurrentQuestion">
/// The currently open question to be presented to the user, or <see langword="null"/> if the
/// session no longer has an open question (completed or abandoned).
/// </param>
/// <param name="Answers">
/// The answers given so far in the session in ascending <see cref="SessionAnswer.Sequence"/>
/// (chronological order); empty if no answer has been recorded yet.
/// </param>
public sealed record ResumeDialogResult(
    Guid SessionId,
    SessionStatus Status,
    QuestionView? CurrentQuestion,
    IReadOnlyList<SessionAnswerView> Answers);

/// <summary>
/// Lean, immutable view of a <see cref="SessionAnswer"/> for the runtime API – without
/// EF Core navigations, so that host apps can display answers already given without knowing the
/// configuration graph.
/// </summary>
/// <param name="QuestionId">The primary key of the answered question.</param>
/// <param name="QuestionKey">
/// The business, stable key of the answered question (resolved from the pinned dialog version).
/// </param>
/// <param name="Value">The stored answer value as raw JSON text (format depends on the question type).</param>
/// <param name="Sequence">The running position of the answer within the session (starting at 0).</param>
/// <param name="AnsweredAt">The point in time at which the answer was recorded.</param>
/// <param name="LoopInstanceId">
/// The instance id of the loop the answer belongs to, or <see langword="null"/> if the answer
/// was given outside a loop.
/// </param>
/// <param name="IterationIndex">
/// The zero-based iteration index within the loop, or <see langword="null"/> outside a
/// loop.
/// </param>
public sealed record SessionAnswerView(
    Guid QuestionId,
    string QuestionKey,
    string Value,
    int Sequence,
    DateTimeOffset AnsweredAt,
    Guid? LoopInstanceId,
    int? IterationIndex);
