namespace Flirty.AspNetCore.Dtos;

/// <summary>
/// Request body for <c>POST /flirty/sessions/{id}/answers</c>: submits an answer to the session's
/// currently open question. The session id comes from the route; this body carries the question
/// and the answer value. Mapped onto the <see cref="Flirty.Runtime.SubmitAnswerCommand"/>.
/// </summary>
/// <param name="QuestionId">
/// The id of the question to answer; must match the session's currently open question.
/// </param>
/// <param name="Value">
/// The submitted answer value as raw JSON text (format depending on the question type, e.g. the value of a
/// selection option).
/// </param>
public sealed record SubmitAnswerRequest(Guid QuestionId, string Value);
