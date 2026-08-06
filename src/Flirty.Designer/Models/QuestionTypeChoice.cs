using Flirty.Domain;

namespace Flirty.Designer.Models;

/// <summary>
/// One entry of the "what kind of question is this?" list: either a built-in
/// <see cref="QuestionType"/> or a host-declared custom type (#137), which authors as
/// <see cref="QuestionType.Json"/> plus a <see cref="Question.CustomTypeKey"/>.
/// </summary>
/// <remarks>
/// A host type is not an enum member and never becomes one (ADR 0011), so a surface that lets an author
/// pick one cannot iterate <see cref="Enum.GetValues{TEnum}()"/> any more. This record is that list's
/// element; it is produced by <see cref="QuestionTypeLabels.Choices"/> and read back by
/// <see cref="QuestionTypeLabels.TryResolveChoice"/>.
/// </remarks>
/// <param name="Value">
/// Stable identifier for a <c>&lt;select&gt;</c> option or a palette entry. The enum member name for a
/// built-in type, and a prefixed key for a declared one.
/// </param>
/// <param name="Label">The display text.</param>
/// <param name="Type">The <see cref="QuestionType"/> to author.</param>
/// <param name="CustomTypeKey">
/// The <see cref="Question.CustomTypeKey"/> to author, or <see langword="null"/> for a built-in type.
/// </param>
internal sealed record QuestionTypeChoice(
    string Value,
    string Label,
    QuestionType Type,
    string? CustomTypeKey);
