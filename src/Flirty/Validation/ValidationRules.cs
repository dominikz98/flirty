namespace Flirty.Validation;

/// <summary>
/// Deserialized model of the optional, per-question validation rules
/// (<see cref="Flirty.Domain.Question.ValidationRules"/>, stored as JSON). All fields are
/// optional; an unset field means "no restriction". The rules are
/// <b>type-scoped</b>: length and pattern rules apply to <see cref="Flirty.Domain.QuestionType.FreeText"/>,
/// the numeric bounds to <see cref="Flirty.Domain.QuestionType.Number"/>; rules not applicable to
/// other question types are ignored.
/// </summary>
/// <remarks>
/// The JSON uses camelCase field names (e.g. <c>{ "maxLength": 50 }</c>). The deserialization
/// is case-insensitive by the <see cref="AnswerValidator"/>.
/// </remarks>
public sealed record ValidationRules
{
    /// <summary>Minimum length of the text (characters) for <see cref="Flirty.Domain.QuestionType.FreeText"/>.</summary>
    public int? MinLength { get; init; }

    /// <summary>Maximum length of the text (characters) for <see cref="Flirty.Domain.QuestionType.FreeText"/>.</summary>
    public int? MaxLength { get; init; }

    /// <summary>Smallest allowed value for <see cref="Flirty.Domain.QuestionType.Number"/> (inclusive).</summary>
    public decimal? Min { get; init; }

    /// <summary>Largest allowed value for <see cref="Flirty.Domain.QuestionType.Number"/> (inclusive).</summary>
    public decimal? Max { get; init; }

    /// <summary>
    /// Regular expression the text must match for <see cref="Flirty.Domain.QuestionType.FreeText"/>
    /// (partial match via <c>Regex.IsMatch</c>; anchor the pattern for a full check, e.g. <c>^…$</c>).
    /// </summary>
    public string? Pattern { get; init; }
}
