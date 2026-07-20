namespace Flirty.AspNetCore.Dtos;

/// <summary>
/// Anfrage-Körper für <c>POST /flirty/sessions/{id}/answers</c>: reicht eine Antwort auf die aktuell
/// offene Frage der Session ein. Die Session-Id stammt aus der Route; dieser Körper trägt die Frage
/// und den Antwortwert. Wird auf das <see cref="Flirty.Runtime.SubmitAnswerCommand"/> gemappt.
/// </summary>
/// <param name="QuestionId">
/// Die Id der zu beantwortenden Frage; muss der aktuell offenen Frage der Session entsprechen.
/// </param>
/// <param name="Value">
/// Der abgegebene Antwortwert als roher JSON-Text (Format abhängig vom Fragetyp, z. B. der Wert einer
/// Auswahloption).
/// </param>
public sealed record SubmitAnswerRequest(Guid QuestionId, string Value);
