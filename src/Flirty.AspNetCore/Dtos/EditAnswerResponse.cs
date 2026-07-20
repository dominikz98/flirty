namespace Flirty.AspNetCore.Dtos;

/// <summary>
/// Antwort-Körper von <c>PUT /flirty/sessions/{id}/answers/{questionId}</c>: gibt an, ob der Dialog nach
/// der Neuberechnung abgeschlossen ist, liefert andernfalls die nun als Nächstes zu beantwortende Frage
/// und meldet, wie viele nachgelagerte Antworten dabei verworfen wurden. Gemappt aus
/// <see cref="Flirty.Runtime.EditAnswerResult"/>.
/// </summary>
/// <param name="SessionId">Der Primärschlüssel der betroffenen Session.</param>
/// <param name="IsCompleted">
/// <see langword="true"/>, wenn der Dialog nach der Neuberechnung abgeschlossen ist; andernfalls
/// <see langword="false"/>.
/// </param>
/// <param name="NextQuestion">
/// Die nach der Neuberechnung als Nächstes zu präsentierende Frage oder <see langword="null"/>, wenn der
/// Dialog abgeschlossen ist.
/// </param>
/// <param name="InvalidatedAnswers">
/// Anzahl der wegen der Editierung verworfenen nachgelagerten Antworten; <c>0</c>, wenn keine existierte.
/// </param>
public sealed record EditAnswerResponse(
    Guid SessionId, bool IsCompleted, QuestionDto? NextQuestion, int InvalidatedAnswers);
