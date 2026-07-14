namespace Flirty.Domain;

/// <summary>
/// Ein konfigurierbarer Dialog (Chatbot-Konversation) und zugleich das Aggregat-Root der
/// Konfigurationsebene: er bündelt seine Fragen, Übergänge, Schleifen und Trigger.
/// </summary>
public sealed class Dialog
{
    /// <summary>Eindeutiger Primärschlüssel des Dialogs.</summary>
    public Guid Id { get; set; }

    /// <summary>Fachlicher, stabiler Schlüssel des Dialogs (z. B. für Start per <c>dialogKey</c>).</summary>
    public required string Key { get; set; }

    /// <summary>Anzeigename des Dialogs.</summary>
    public required string Name { get; set; }

    /// <summary>Optionale Beschreibung des Dialogs.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// Versionsnummer des Dialogs. Laufende Sessions pinnen diese Version, damit das
    /// Editieren eines publizierten Dialogs bestehende Sessions nicht bricht.
    /// </summary>
    public int Version { get; set; }

    /// <summary>Gibt an, ob der Dialog veröffentlicht (produktiv nutzbar) ist.</summary>
    public bool IsPublished { get; set; }

    /// <summary>
    /// Verweis auf die Einstiegsfrage (<see cref="Question.Id"/>) oder <see langword="null"/>,
    /// solange noch keine Startfrage festgelegt ist.
    /// </summary>
    public Guid? StartQuestionId { get; set; }

    /// <summary>Zeitpunkt der Erstellung des Dialogs.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Zeitpunkt der letzten Änderung des Dialogs.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Die Fragen dieses Dialogs.</summary>
    public ICollection<Question> Questions { get; set; } = [];

    /// <summary>Die bedingten Übergänge (Branching) dieses Dialogs.</summary>
    public ICollection<Transition> Transitions { get; set; } = [];

    /// <summary>Die Schleifen-Definitionen (Loop-Marker) dieses Dialogs.</summary>
    public ICollection<LoopDefinition> Loops { get; set; } = [];

    /// <summary>Die Trigger-Definitionen (Rückkanäle) dieses Dialogs.</summary>
    public ICollection<TriggerDefinition> Triggers { get; set; } = [];
}
