namespace Flirty.Domain;

/// <summary>
/// A configurable dialog (chatbot conversation) and at the same time the aggregate root of the
/// configuration layer: it bundles its questions, transitions, loops, triggers and the canvas layout.
/// </summary>
public sealed class Dialog
{
    /// <summary>Unique primary key of the dialog.</summary>
    public Guid Id { get; set; }

    /// <summary>Business, stable key of the dialog (e.g. for starting via <c>dialogKey</c>).</summary>
    public required string Key { get; set; }

    /// <summary>Display name of the dialog.</summary>
    public required string Name { get; set; }

    /// <summary>Optional description of the dialog.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// Version number of the dialog. Running sessions pin this version so that editing a published
    /// dialog does not break existing sessions.
    /// </summary>
    public int Version { get; set; }

    /// <summary>Indicates whether the dialog is published (usable in production).</summary>
    public bool IsPublished { get; set; }

    /// <summary>
    /// Reference to the entry question (<see cref="Question.Id"/>) or <see langword="null"/>
    /// while no start question has been set yet.
    /// </summary>
    public Guid? StartQuestionId { get; set; }

    /// <summary>Timestamp of the dialog's creation.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Timestamp of the dialog's last modification.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>The questions of this dialog.</summary>
    public ICollection<Question> Questions { get; set; } = [];

    /// <summary>The conditional transitions (branching) of this dialog.</summary>
    public ICollection<Transition> Transitions { get; set; } = [];

    /// <summary>The loop definitions (loop markers) of this dialog.</summary>
    public ICollection<LoopDefinition> Loops { get; set; } = [];

    /// <summary>The trigger definitions (back channels) of this dialog.</summary>
    public ICollection<TriggerDefinition> Triggers { get; set; } = [];

    /// <summary>
    /// The stored canvas positions of this dialog - pure display data of the designer, never read by the
    /// runtime.
    /// </summary>
    public ICollection<DialogLayout> Layout { get; set; } = [];
}
