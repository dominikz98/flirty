namespace Flirty.Domain;

/// <summary>
/// An answer to a question given within a <see cref="DialogSession"/>. Via
/// <see cref="LoopInstanceId"/> and <see cref="IterationIndex"/> multiple answers per question can
/// exist within a loop (one entry per iteration).
/// </summary>
public sealed class SessionAnswer
{
    /// <summary>Unique primary key of the answer.</summary>
    public Guid Id { get; set; }

    /// <summary>Foreign key to the owning <see cref="DialogSession"/>.</summary>
    public Guid SessionId { get; set; }

    /// <summary>Reference to the answered question (<see cref="Question.Id"/>).</summary>
    public Guid QuestionId { get; set; }

    /// <summary>The submitted answer value as JSON (format depends on the question type).</summary>
    public required string Value { get; set; }

    /// <summary>Timestamp at which the answer was given.</summary>
    public DateTimeOffset AnsweredAt { get; set; }

    /// <summary>Running order of the answer within the session.</summary>
    public int Sequence { get; set; }

    /// <summary>
    /// Identifier of the loop iteration instance or <see langword="null"/> outside a loop.
    /// Groups the answers belonging to one iteration.
    /// </summary>
    public Guid? LoopInstanceId { get; set; }

    /// <summary>
    /// Zero-based iteration index within the loop or <see langword="null"/> outside
    /// a loop.
    /// </summary>
    public int? IterationIndex { get; set; }

    /// <summary>The session this answer belongs to.</summary>
    public DialogSession Session { get; set; } = null!;
}
