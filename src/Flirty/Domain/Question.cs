namespace Flirty.Domain;

/// <summary>
/// A single question within a <see cref="Dialog"/>. The <see cref="Type"/> determines
/// how the answer is parsed and validated.
/// </summary>
public sealed class Question
{
    /// <summary>Unique primary key of the question.</summary>
    public Guid Id { get; set; }

    /// <summary>Foreign key to the owning <see cref="Dialog"/>.</summary>
    public Guid DialogId { get; set; }

    /// <summary>Business, stable key of the question (e.g. for the expression context).</summary>
    public required string Key { get; set; }

    /// <summary>The displayed question text.</summary>
    public required string Text { get; set; }

    /// <summary>The answer type of the question.</summary>
    public QuestionType Type { get; set; }

    /// <summary>Order index of the question within the dialog.</summary>
    public int Order { get; set; }

    /// <summary>Indicates whether an answer to this question is required.</summary>
    public bool IsRequired { get; set; }

    /// <summary>
    /// Optional validation rules as JSON (e.g. min/max, regex). Evaluation is performed
    /// by the answer validator.
    /// </summary>
    public string? ValidationRules { get; set; }

    /// <summary>
    /// Optional key of a host-declared custom question type, only meaningful together with
    /// <see cref="QuestionType.Json"/> (the admin commands refuse it on any other type). The key is
    /// resolved against the types the host declared with <c>AddQuestionType</c>; it is deliberately
    /// <b>not</b> a foreign key, because that registry lives in host code rather than in the database.
    /// An unknown key is therefore not a dangling reference and not an error: the answer is then
    /// validated as plain JSON.
    /// </summary>
    public string? CustomTypeKey { get; set; }

    /// <summary>The dialog this question belongs to.</summary>
    public Dialog Dialog { get; set; } = null!;

    /// <summary>The answer options of this question (relevant for choice types).</summary>
    public ICollection<AnswerOption> Options { get; set; } = [];
}
