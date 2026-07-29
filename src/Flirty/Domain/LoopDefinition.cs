namespace Flirty.Domain;

/// <summary>
/// A marker/metadata layer on top of the branching that marks a cycle as a loop.
/// At runtime the answers of the loop range are collected per iteration under
/// <see cref="CollectionKey"/> (instead of overwritten); in the designer the cycle is
/// visualized as a loop block with a marked breaking question.
/// </summary>
public sealed class LoopDefinition
{
    /// <summary>Unique primary key of the loop definition.</summary>
    public Guid Id { get; set; }

    /// <summary>Foreign key to the owning <see cref="Dialog"/>.</summary>
    public Guid DialogId { get; set; }

    /// <summary>
    /// Key under which the answers collected per iteration are available in the expression context
    /// (e.g. <c>positions</c> for <c>positions.Count &gt; 0</c>).
    /// </summary>
    public required string CollectionKey { get; set; }

    /// <summary>Reference to the entry question of the loop (<see cref="Question.Id"/>).</summary>
    public Guid EntryQuestionId { get; set; }

    /// <summary>
    /// Reference to the breaking question (<see cref="Question.Id"/>) - the question whose
    /// exit transition leaves the cycle.
    /// </summary>
    public Guid BreakingQuestionId { get; set; }

    /// <summary>The dialog this loop definition belongs to.</summary>
    public Dialog Dialog { get; set; } = null!;
}
