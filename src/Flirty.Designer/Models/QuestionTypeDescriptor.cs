namespace Flirty.Designer.Models;

/// <summary>
/// One entry of the designer's question-type descriptor file: the data half of a host-declared custom
/// question type (#136), as far as the designer can know it.
/// </summary>
/// <remarks>
/// <para>
/// The designer is a separate process and does not share the host's DI container, so it cannot see
/// <c>o.AddQuestionType(...)</c>. What it can be told is the <b>descriptor</b> – key, display name and an
/// example answer – and that is exactly the shape of the core record
/// <see cref="Flirty.Validation.FlirtyQuestionType"/> minus its <c>ValidatorType</c>. The validator is
/// code and stays in the host; see <c>docs/adr/0012-designer-question-type-descriptors-at-startup.md</c>.
/// </para>
/// <para>
/// Deliberately mutable with settable properties, like <see cref="ConnectionProfile"/>: this is a
/// deserialization target for <c>question-types.json</c>.
/// </para>
/// </remarks>
internal sealed class QuestionTypeDescriptor
{
    /// <summary>
    /// The key a question carries in <see cref="Flirty.Domain.Question.CustomTypeKey"/>. Lowercase ASCII
    /// letters, digits and <c>-</c> only – the core enforces that, this type does not repeat the rule.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>The human-readable name shown instead of the raw key.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>An optional example answer as JSON; prefills the test runner's input field.</summary>
    public string? Sample { get; set; }
}
