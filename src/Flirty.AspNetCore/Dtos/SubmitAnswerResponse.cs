namespace Flirty.AspNetCore.Dtos;

/// <summary>
/// Response body of <c>POST /flirty/sessions/{id}/answers</c>: indicates whether the dialog is completed
/// after the answer, and otherwise delivers the question to be answered next. Mapped from
/// <see cref="Flirty.Runtime.SubmitAnswerResult"/>.
/// </summary>
/// <param name="SessionId">The primary key of the affected session.</param>
/// <param name="IsCompleted">
/// <see langword="true"/> if the dialog was completed with this answer; otherwise
/// <see langword="false"/>.
/// </param>
/// <param name="NextQuestion">
/// The question to present next, or <see langword="null"/> if the dialog is completed.
/// </param>
public sealed record SubmitAnswerResponse(Guid SessionId, bool IsCompleted, QuestionDto? NextQuestion);
