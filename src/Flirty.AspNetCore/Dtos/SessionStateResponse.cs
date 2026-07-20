using Flirty.Domain;

namespace Flirty.AspNetCore.Dtos;

/// <summary>
/// Antwort-Körper von <c>GET /flirty/sessions/{id}</c>: der aktuelle Zustand einer Session – Status, die
/// (ggf.) aktuell offene Frage und die bisher gegebenen Antworten – zum Wiederherstellen einer Befragung.
/// Gemappt aus <see cref="Flirty.Runtime.ResumeDialogResult"/>.
/// </summary>
/// <param name="SessionId">Der Primärschlüssel der abgefragten Session.</param>
/// <param name="Status">Der aktuelle Lebenszyklus-Status der Session.</param>
/// <param name="CurrentQuestion">
/// Die aktuell offene Frage oder <see langword="null"/>, wenn die Session keine offene Frage mehr hat
/// (abgeschlossen bzw. abgebrochen).
/// </param>
/// <param name="Answers">
/// Die bisher gegebenen Antworten in aufsteigender Reihenfolge; leer, wenn noch keine Antwort erfasst wurde.
/// </param>
public sealed record SessionStateResponse(
    Guid SessionId,
    SessionStatus Status,
    QuestionDto? CurrentQuestion,
    IReadOnlyList<SessionAnswerDto> Answers);
