namespace Flirty.Domain;

/// <summary>
/// Ein bedingter Übergang (Branching) von einer Frage zu einer Zielfrage. Je Ausgangsfrage
/// bilden die Übergänge eine nach <see cref="Priority"/> geordnete Liste; der erste zutreffende
/// Übergang gewinnt, andernfalls greift der als <see cref="IsDefault"/> markierte. Zeigt
/// <see cref="TargetQuestionId"/> auf eine frühere Frage, entsteht ein Loop-Zyklus.
/// </summary>
public sealed class Transition
{
    /// <summary>Eindeutiger Primärschlüssel des Übergangs.</summary>
    public Guid Id { get; set; }

    /// <summary>Fremdschlüssel auf den zugehörigen <see cref="Dialog"/>.</summary>
    public Guid DialogId { get; set; }

    /// <summary>Verweis auf die Ausgangsfrage (<see cref="Question.Id"/>).</summary>
    public Guid FromQuestionId { get; set; }

    /// <summary>
    /// Optionaler Bedingungsausdruck, der über <see cref="Flirty.Expressions.IConditionEvaluator"/>
    /// ausgewertet wird. Ist er <see langword="null"/>/leer, gilt der Übergang als bedingungslos zutreffend.
    /// </summary>
    public string? ConditionExpression { get; set; }

    /// <summary>Verweis auf die Zielfrage (<see cref="Question.Id"/>).</summary>
    public Guid TargetQuestionId { get; set; }

    /// <summary>Priorität für die Auswertungsreihenfolge (kleinerer Wert = früher geprüft).</summary>
    public int Priority { get; set; }

    /// <summary>
    /// Gibt an, ob dieser Übergang der Default ist, der greift, wenn kein bedingter Übergang
    /// zutrifft.
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>Der Dialog, zu dem dieser Übergang gehört.</summary>
    public Dialog Dialog { get; set; } = null!;
}
