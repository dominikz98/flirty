namespace Flirty.AspNetCore.Dtos;

/// <summary>
/// Response body of <c>PUT /flirty/sessions/{id}/answers/{questionId}</c>: indicates whether the dialog is
/// completed after the recomputation, otherwise delivers the question now to be answered next
/// and reports how many downstream answers were discarded in the process. Mapped from
/// <see cref="Flirty.Runtime.EditAnswerResult"/>.
/// </summary>
/// <param name="SessionId">The primary key of the affected session.</param>
/// <param name="IsCompleted">
/// <see langword="true"/> if the dialog is completed after the recomputation; otherwise
/// <see langword="false"/>.
/// </param>
/// <param name="NextQuestion">
/// The question to present next after the recomputation, or <see langword="null"/> if the
/// dialog is completed.
/// </param>
/// <param name="InvalidatedAnswers">
/// Number of downstream answers discarded because of the edit; <c>0</c> if none existed.
/// </param>
public sealed record EditAnswerResponse(
    Guid SessionId, bool IsCompleted, QuestionDto? NextQuestion, int InvalidatedAnswers);
