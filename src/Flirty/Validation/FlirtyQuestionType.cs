namespace Flirty.Validation;

/// <summary>
/// A question type declared by the host with <c>AddQuestionType</c>. Such a type always maps onto
/// <see cref="Flirty.Domain.QuestionType.Json"/>; a question selects it by carrying the
/// <see cref="Key"/> in <see cref="Flirty.Domain.Question.CustomTypeKey"/>.
/// </summary>
/// <param name="Key">
/// The stable key the type is declared and stored under. Restricted to lowercase ASCII letters,
/// digits and <c>-</c>, and compared ordinally – see <see cref="FlirtyQuestionTypeRegistry"/>.
/// </param>
/// <param name="DisplayName">A human-readable name, reported to clients that ask which types exist.</param>
/// <param name="ValidatorType">
/// The <see cref="IQuestionTypeValidator"/> implementation registered for this type, or
/// <see langword="null"/> if the type declares no semantics of its own beyond well-formed JSON.
/// </param>
/// <param name="SampleValue">
/// An optional example answer as JSON, so a client can see the expected shape without guessing.
/// </param>
public sealed record FlirtyQuestionType(
    string Key,
    string DisplayName,
    Type? ValidatorType,
    string? SampleValue);
