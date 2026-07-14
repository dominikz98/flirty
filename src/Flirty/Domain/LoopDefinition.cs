namespace Flirty.Domain;

/// <summary>
/// Marker-/Metadaten-Ebene über dem Branching, die einen Zyklus als Schleife kennzeichnet.
/// Zur Laufzeit werden die Antworten des Schleifenbereichs je Iteration unter
/// <see cref="CollectionKey"/> gesammelt (statt überschrieben); im Designer wird der Zyklus
/// als Loop-Block mit markierter Breaking Question visualisiert.
/// </summary>
public sealed class LoopDefinition
{
    /// <summary>Eindeutiger Primärschlüssel der Schleifen-Definition.</summary>
    public Guid Id { get; set; }

    /// <summary>Fremdschlüssel auf den zugehörigen <see cref="Dialog"/>.</summary>
    public Guid DialogId { get; set; }

    /// <summary>
    /// Schlüssel, unter dem die je Iteration gesammelten Antworten im Ausdruckskontext
    /// verfügbar sind (z. B. <c>positions</c> für <c>positions.Count &gt; 0</c>).
    /// </summary>
    public required string CollectionKey { get; set; }

    /// <summary>Verweis auf die Einstiegsfrage der Schleife (<see cref="Question.Id"/>).</summary>
    public Guid EntryQuestionId { get; set; }

    /// <summary>
    /// Verweis auf die Breaking Question (<see cref="Question.Id"/>) – die Frage, deren
    /// Exit-Übergang den Zyklus verlässt.
    /// </summary>
    public Guid BreakingQuestionId { get; set; }

    /// <summary>Der Dialog, zu dem diese Schleifen-Definition gehört.</summary>
    public Dialog Dialog { get; set; } = null!;
}
