namespace Flirty.Domain;

/// <summary>
/// Eine vorgegebene Antwortoption einer <see cref="Question"/> (relevant für
/// <see cref="QuestionType.SingleChoice"/> und <see cref="QuestionType.MultiChoice"/>).
/// </summary>
public sealed class AnswerOption
{
    /// <summary>Eindeutiger Primärschlüssel der Antwortoption.</summary>
    public Guid Id { get; set; }

    /// <summary>Fremdschlüssel auf die zugehörige <see cref="Question"/>.</summary>
    public Guid QuestionId { get; set; }

    /// <summary>Fachlicher, stabiler Schlüssel der Option.</summary>
    public required string Key { get; set; }

    /// <summary>Der angezeigte Beschriftungstext der Option.</summary>
    public required string Label { get; set; }

    /// <summary>Der bei Auswahl gespeicherte Wert der Option.</summary>
    public required string Value { get; set; }

    /// <summary>Reihenfolge-Index der Option innerhalb der Frage.</summary>
    public int Order { get; set; }

    /// <summary>Die Frage, zu der diese Antwortoption gehört.</summary>
    public Question Question { get; set; } = null!;
}
