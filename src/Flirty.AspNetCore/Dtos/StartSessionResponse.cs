namespace Flirty.AspNetCore.Dtos;

/// <summary>
/// Antwort-Körper von <c>POST /flirty/sessions</c>: die (neu angelegte oder wiederaufgenommene) Session
/// samt der aktuell zu beantwortenden Frage. Gemappt aus <see cref="Flirty.Runtime.StartDialogResult"/>.
/// </summary>
/// <param name="SessionId">Der Primärschlüssel der laufenden Session.</param>
/// <param name="IsResumed">
/// <see langword="true"/>, wenn eine bereits laufende Session fortgesetzt wurde; <see langword="false"/>
/// bei einem Neu-Start.
/// </param>
/// <param name="CurrentQuestion">Die aktuell offene Frage, die dem Anwender zu präsentieren ist.</param>
public sealed record StartSessionResponse(Guid SessionId, bool IsResumed, QuestionDto CurrentQuestion);
