using Flirty.Domain;

namespace Flirty.AspNetCore.Dtos;

/// <summary>
/// Lean, serializable view of a question for the WebAPI responses. Mirrors
/// <see cref="Flirty.Runtime.QuestionView"/> as the HTTP contract of the package <c>Flirty.AspNetCore</c>.
/// </summary>
/// <param name="Id">The primary key of the question.</param>
/// <param name="Key">The business, stable key of the question.</param>
/// <param name="Text">The question text to display.</param>
/// <param name="Type">The answer type of the question.</param>
/// <param name="CustomTypeKey">
/// The key of the host-declared custom question type, or <see langword="null"/>. A renderer picks the
/// input control for a <see cref="QuestionType.Json"/> question by this key – the engine knows nothing
/// about controls, so this is what makes a custom type presentable at all.
/// </param>
/// <param name="Options">
/// The answer options of the question in display order (empty for free-text/value types).
/// </param>
public sealed record QuestionDto(
    Guid Id,
    string Key,
    string Text,
    QuestionType Type,
    string? CustomTypeKey,
    IReadOnlyList<AnswerOptionDto> Options);

/// <summary>
/// Lean, serializable view of an answer option for the WebAPI responses. Mirrors
/// <see cref="Flirty.Runtime.AnswerOptionView"/>.
/// </summary>
/// <param name="Id">The primary key of the answer option.</param>
/// <param name="Key">The business, stable key of the option.</param>
/// <param name="Label">The label of the option to display.</param>
/// <param name="Value">The value of the option to store.</param>
public sealed record AnswerOptionDto(Guid Id, string Key, string Label, string Value);
