using System.Text.Json;
using Flirty.Domain;
using Flirty.Validation;

namespace Flirty.Samples.Web;

/// <summary>
/// The worked example of a <b>composite</b> host-declared question type: a postal address as a JSON
/// object of several fields, answered as one answer. Declared in <c>WebSampleApp</c> as <c>address</c>.
/// </summary>
/// <remarks>
/// <para>
/// The counterpart to <see cref="ColourAnswerValidator"/>, and the pair is the point: the same extension
/// point carries a scalar type and a structured one. This is also what shows that the stored value stays
/// <b>opaque</b> to the engine – an object answer binds as a dictionary in a branching condition without
/// the engine knowing anything about addresses.
/// </para>
/// <para>
/// Structure is declared here, in code the host owns, rather than as a schema on the question. A custom
/// type's validator is the single owner of its rules, which is also why it may read
/// <see cref="Question.ValidationRules"/> for its own extra configuration – the same arrangement
/// <c>TriggerConfig</c> uses.
/// </para>
/// </remarks>
public sealed class AddressAnswerValidator : IQuestionTypeValidator
{
    private static readonly string[] RequiredFields = ["street", "city"];

    /// <inheritdoc />
    public AnswerValidationResult Validate(Question question, string value)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(value);

        using var document = JsonDocument.Parse(value);
        var root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
        {
            return AnswerValidationResult.Invalid(
                "An address must be a JSON object, e.g. {\"street\":\"Main 1\",\"city\":\"Berlin\"}.");
        }

        var missing = RequiredFields
            .Where(field => !root.TryGetProperty(field, out var property)
                || property.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(property.GetString()))
            .ToList();

        // One error per missing field, so a caller can point at the offending input rather than being
        // told "the address is wrong".
        return missing.Count == 0
            ? AnswerValidationResult.Valid
            : AnswerValidationResult.Invalid(
                [.. missing.Select(field => $"The address field '{field}' is required.")]);
    }
}
