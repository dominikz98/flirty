namespace Flirty.Domain;

/// <summary>
/// Eine einzelne Frage innerhalb eines <see cref="Dialog"/>. Der <see cref="Type"/> bestimmt,
/// wie die Antwort geparst und validiert wird.
/// </summary>
public sealed class Question
{
    /// <summary>Eindeutiger Primärschlüssel der Frage.</summary>
    public Guid Id { get; set; }

    /// <summary>Fremdschlüssel auf den zugehörigen <see cref="Dialog"/>.</summary>
    public Guid DialogId { get; set; }

    /// <summary>Fachlicher, stabiler Schlüssel der Frage (z. B. für den Ausdruckskontext).</summary>
    public required string Key { get; set; }

    /// <summary>Der angezeigte Fragetext.</summary>
    public required string Text { get; set; }

    /// <summary>Der Antworttyp der Frage.</summary>
    public QuestionType Type { get; set; }

    /// <summary>Reihenfolge-Index der Frage innerhalb des Dialogs.</summary>
    public int Order { get; set; }

    /// <summary>Gibt an, ob eine Antwort auf diese Frage erforderlich ist.</summary>
    public bool IsRequired { get; set; }

    /// <summary>
    /// Optionale Validierungsregeln als JSON (z. B. Min/Max, Regex). Auswertung erfolgt
    /// durch den Antwort-Validator.
    /// </summary>
    public string? ValidationRules { get; set; }

    /// <summary>Der Dialog, zu dem diese Frage gehört.</summary>
    public Dialog Dialog { get; set; } = null!;

    /// <summary>Die Antwortoptionen dieser Frage (relevant für Choice-Typen).</summary>
    public ICollection<AnswerOption> Options { get; set; } = [];
}
