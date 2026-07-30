namespace Flirty.Domain;

/// <summary>
/// A conditional transition (branching) from a question to a target question. Per source question
/// the transitions form a list ordered by <see cref="Priority"/>; the first matching
/// transition wins, otherwise the one marked as <see cref="IsDefault"/> applies. If
/// <see cref="TargetQuestionId"/> points to an earlier question, a loop cycle arises.
/// </summary>
public sealed class Transition
{
    /// <summary>Unique primary key of the transition.</summary>
    public Guid Id { get; set; }

    /// <summary>Foreign key to the owning <see cref="Dialog"/>.</summary>
    public Guid DialogId { get; set; }

    /// <summary>Reference to the source question (<see cref="Question.Id"/>).</summary>
    public Guid FromQuestionId { get; set; }

    /// <summary>
    /// Optional condition expression that is evaluated via <see cref="Flirty.Expressions.IExpressionEvaluator"/>.
    /// If it is <see langword="null"/>/empty, the transition is considered unconditionally matching.
    /// </summary>
    public string? Expression { get; set; }

    /// <summary>Reference to the target question (<see cref="Question.Id"/>).</summary>
    public Guid TargetQuestionId { get; set; }

    /// <summary>Priority for the evaluation order (smaller value = checked earlier).</summary>
    public int Priority { get; set; }

    /// <summary>
    /// Indicates whether this transition is the default that applies when no conditional transition
    /// matches.
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>The dialog this transition belongs to.</summary>
    public Dialog Dialog { get; set; } = null!;
}
