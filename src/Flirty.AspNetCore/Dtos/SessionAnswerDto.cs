namespace Flirty.AspNetCore.Dtos;

/// <summary>
/// Lean, serializable view of an answer already given for the WebAPI responses. Mirrors
/// <see cref="Flirty.Runtime.SessionAnswerView"/>.
/// </summary>
/// <param name="QuestionId">The primary key of the answered question.</param>
/// <param name="QuestionKey">The business, stable key of the answered question.</param>
/// <param name="Value">The stored answer value as raw JSON text (format depending on the question type).</param>
/// <param name="Sequence">The consecutive position of the answer within the session (starting at 0).</param>
/// <param name="AnsweredAt">The time at which the answer was recorded.</param>
/// <param name="LoopInstanceId">
/// The instance id of the loop the answer belongs to, or <see langword="null"/> outside a loop.
/// </param>
/// <param name="IterationIndex">
/// The zero-based iteration index within the loop, or <see langword="null"/> outside a loop.
/// </param>
public sealed record SessionAnswerDto(
    Guid QuestionId,
    string QuestionKey,
    string Value,
    int Sequence,
    DateTimeOffset AnsweredAt,
    Guid? LoopInstanceId,
    int? IterationIndex);
