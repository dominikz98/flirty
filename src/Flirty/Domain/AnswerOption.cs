namespace Flirty.Domain;

/// <summary>
/// A predefined answer option of a <see cref="Question"/> (relevant for
/// <see cref="QuestionType.SingleChoice"/> and <see cref="QuestionType.MultiChoice"/>).
/// </summary>
public sealed class AnswerOption
{
    /// <summary>Unique primary key of the answer option.</summary>
    public Guid Id { get; set; }

    /// <summary>Foreign key to the owning <see cref="Question"/>.</summary>
    public Guid QuestionId { get; set; }

    /// <summary>Business, stable key of the option.</summary>
    public required string Key { get; set; }

    /// <summary>The displayed label text of the option.</summary>
    public required string Label { get; set; }

    /// <summary>The value stored when the option is selected.</summary>
    public required string Value { get; set; }

    /// <summary>Order index of the option within the question.</summary>
    public int Order { get; set; }

    /// <summary>The question this answer option belongs to.</summary>
    public Question Question { get; set; } = null!;
}
