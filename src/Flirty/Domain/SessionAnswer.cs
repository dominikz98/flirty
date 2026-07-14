namespace Flirty.Domain;

/// <summary>
/// Eine im Rahmen einer <see cref="DialogSession"/> gegebene Antwort auf eine Frage. Über
/// <see cref="LoopInstanceId"/> und <see cref="IterationIndex"/> können innerhalb einer Schleife
/// mehrere Antworten pro Frage existieren (ein Eintrag je Iteration).
/// </summary>
public sealed class SessionAnswer
{
    /// <summary>Eindeutiger Primärschlüssel der Antwort.</summary>
    public Guid Id { get; set; }

    /// <summary>Fremdschlüssel auf die zugehörige <see cref="DialogSession"/>.</summary>
    public Guid SessionId { get; set; }

    /// <summary>Verweis auf die beantwortete Frage (<see cref="Question.Id"/>).</summary>
    public Guid QuestionId { get; set; }

    /// <summary>Der abgegebene Antwortwert als JSON (Format abhängig vom Fragetyp).</summary>
    public required string Value { get; set; }

    /// <summary>Zeitpunkt, zu dem die Antwort gegeben wurde.</summary>
    public DateTimeOffset AnsweredAt { get; set; }

    /// <summary>Fortlaufende Reihenfolge der Antwort innerhalb der Session.</summary>
    public int Sequence { get; set; }

    /// <summary>
    /// Kennung der Schleifen-Iterationsinstanz oder <see langword="null"/> außerhalb einer Schleife.
    /// Gruppiert die zu einer Iteration gehörenden Antworten.
    /// </summary>
    public Guid? LoopInstanceId { get; set; }

    /// <summary>
    /// Nullbasierter Iterationsindex innerhalb der Schleife oder <see langword="null"/> außerhalb
    /// einer Schleife.
    /// </summary>
    public int? IterationIndex { get; set; }

    /// <summary>Die Session, zu der diese Antwort gehört.</summary>
    public DialogSession Session { get; set; } = null!;
}
