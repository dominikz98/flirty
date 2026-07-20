namespace Flirty.AspNetCore.Dtos;

/// <summary>
/// Antwort-Körper von <c>POST /flirty/sessions/{id}/answers</c>: gibt an, ob der Dialog nach der Antwort
/// abgeschlossen ist, und liefert andernfalls die als Nächstes zu beantwortende Frage. Gemappt aus
/// <see cref="Flirty.Runtime.SubmitAnswerResult"/>.
/// </summary>
/// <param name="SessionId">Der Primärschlüssel der betroffenen Session.</param>
/// <param name="IsCompleted">
/// <see langword="true"/>, wenn der Dialog mit dieser Antwort abgeschlossen wurde; andernfalls
/// <see langword="false"/>.
/// </param>
/// <param name="NextQuestion">
/// Die als Nächstes zu präsentierende Frage oder <see langword="null"/>, wenn der Dialog abgeschlossen ist.
/// </param>
public sealed record SubmitAnswerResponse(Guid SessionId, bool IsCompleted, QuestionDto? NextQuestion);
