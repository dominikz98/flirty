namespace Flirty.AspNetCore.Dtos;

/// <summary>
/// Schlanke, serialisierbare Sicht auf eine bereits gegebene Antwort für die WebAPI-Antworten. Spiegelt
/// <see cref="Flirty.Runtime.SessionAnswerView"/>.
/// </summary>
/// <param name="QuestionId">Der Primärschlüssel der beantworteten Frage.</param>
/// <param name="QuestionKey">Der fachliche, stabile Schlüssel der beantworteten Frage.</param>
/// <param name="Value">Der gespeicherte Antwortwert als roher JSON-Text (Format abhängig vom Fragetyp).</param>
/// <param name="Sequence">Die fortlaufende Position der Antwort innerhalb der Session (beginnend bei 0).</param>
/// <param name="AnsweredAt">Der Zeitpunkt, zu dem die Antwort erfasst wurde.</param>
/// <param name="LoopInstanceId">
/// Die Instanz-Id der Schleife, zu der die Antwort gehört, oder <see langword="null"/> außerhalb einer Schleife.
/// </param>
/// <param name="IterationIndex">
/// Der nullbasierte Iterationsindex innerhalb der Schleife oder <see langword="null"/> außerhalb einer Schleife.
/// </param>
public sealed record SessionAnswerDto(
    Guid QuestionId,
    string QuestionKey,
    string Value,
    int Sequence,
    DateTimeOffset AnsweredAt,
    Guid? LoopInstanceId,
    int? IterationIndex);
