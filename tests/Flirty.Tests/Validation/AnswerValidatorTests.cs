using Flirty.Domain;
using Flirty.Validation;

namespace Flirty.Tests.Validation;

/// <summary>
/// Verifies the <see cref="AnswerValidator"/> (issue #30) in isolation: the type check per
/// <see cref="QuestionType"/>, the type-scoped <see cref="ValidationRules"/>
/// (length/range/pattern), the option membership of the choice types, the tolerant fallback for raw
/// (non-JSON) values as well as the misconfiguration cases.
/// </summary>
public sealed class AnswerValidatorTests
{
    private readonly AnswerValidator _validator = new();

    private static Question NewQuestion(
        QuestionType type, string? validationRules = null, params string[] optionValues)
    {
        var question = new Question
        {
            Id = Guid.NewGuid(),
            DialogId = Guid.NewGuid(),
            Key = "q",
            Text = "Question?",
            Type = type,
            ValidationRules = validationRules,
        };

        var order = 0;
        foreach (var value in optionValues)
        {
            question.Options.Add(new AnswerOption
            {
                Id = Guid.NewGuid(),
                QuestionId = question.Id,
                Key = value,
                Label = value,
                Value = value,
                Order = order++,
            });
        }

        return question;
    }

    // ---- FreeText ----------------------------------------------------------------------------

    [Fact]
    public void FreeText_accepts_arbitrary_text()
        => Assert.True(_validator.Validate(NewQuestion(QuestionType.FreeText), "\"Hello world\"").IsValid);

    [Fact]
    public void FreeText_accepts_raw_non_JSON_text_tolerantly()
        => Assert.True(_validator.Validate(NewQuestion(QuestionType.FreeText), "Hello world").IsValid);

    [Fact]
    public void FreeText_too_short_violates_MinLength()
    {
        var result = _validator.Validate(NewQuestion(QuestionType.FreeText, "{\"minLength\":5}"), "\"ab\"");
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void FreeText_too_long_violates_MaxLength()
    {
        var result = _validator.Validate(NewQuestion(QuestionType.FreeText, "{\"maxLength\":3}"), "\"abcd\"");
        Assert.False(result.IsValid);
    }

    [Fact]
    public void FreeText_within_the_length_bounds_is_valid()
        => Assert.True(_validator.Validate(
            NewQuestion(QuestionType.FreeText, "{\"minLength\":2,\"maxLength\":5}"), "\"abc\"").IsValid);

    [Fact]
    public void FreeText_a_matching_pattern_is_valid()
        => Assert.True(_validator.Validate(
            NewQuestion(QuestionType.FreeText, "{\"pattern\":\"^[a-z]+$\"}"), "\"abc\"").IsValid);

    [Fact]
    public void FreeText_a_non_matching_pattern_is_invalid()
        => Assert.False(_validator.Validate(
            NewQuestion(QuestionType.FreeText, "{\"pattern\":\"^[a-z]+$\"}"), "\"ABC123\"").IsValid);

    // ---- Number ------------------------------------------------------------------------------

    [Fact]
    public void Number_accepts_a_JSON_number()
        => Assert.True(_validator.Validate(NewQuestion(QuestionType.Number), "42").IsValid);

    [Fact]
    public void Number_accepts_a_numeric_string()
        => Assert.True(_validator.Validate(NewQuestion(QuestionType.Number), "\"3.5\"").IsValid);

    [Fact]
    public void Number_rejects_a_non_number()
        => Assert.False(_validator.Validate(NewQuestion(QuestionType.Number), "\"not-a-number\"").IsValid);

    [Fact]
    public void Number_below_the_minimum_is_invalid()
        => Assert.False(_validator.Validate(
            NewQuestion(QuestionType.Number, "{\"min\":1,\"max\":10}"), "0").IsValid);

    [Fact]
    public void Number_above_the_maximum_is_invalid()
        => Assert.False(_validator.Validate(
            NewQuestion(QuestionType.Number, "{\"min\":1,\"max\":10}"), "20").IsValid);

    [Fact]
    public void Number_within_the_range_is_valid()
        => Assert.True(_validator.Validate(
            NewQuestion(QuestionType.Number, "{\"min\":1,\"max\":10}"), "5").IsValid);

    // ---- Boolean -----------------------------------------------------------------------------

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("\"true\"")]
    public void Boolean_accepts_truth_values(string value)
        => Assert.True(_validator.Validate(NewQuestion(QuestionType.Boolean), value).IsValid);

    [Fact]
    public void Boolean_rejects_a_non_boolean()
        => Assert.False(_validator.Validate(NewQuestion(QuestionType.Boolean), "\"maybe\"").IsValid);

    // ---- Date --------------------------------------------------------------------------------

    [Fact]
    public void Date_accepts_an_ISO_date()
        => Assert.True(_validator.Validate(NewQuestion(QuestionType.Date), "\"2026-07-17\"").IsValid);

    [Fact]
    public void Date_rejects_a_non_date()
        => Assert.False(_validator.Validate(NewQuestion(QuestionType.Date), "\"not-a-date\"").IsValid);

    // ---- SingleChoice ------------------------------------------------------------------------

    [Fact]
    public void SingleChoice_accepts_a_known_option()
        => Assert.True(_validator.Validate(
            NewQuestion(QuestionType.SingleChoice, null, "dev", "pm"), "\"dev\"").IsValid);

    [Fact]
    public void SingleChoice_accepts_a_known_option_as_a_raw_string()
        => Assert.True(_validator.Validate(
            NewQuestion(QuestionType.SingleChoice, null, "dev", "pm"), "dev").IsValid);

    [Fact]
    public void SingleChoice_rejects_an_unknown_option()
        => Assert.False(_validator.Validate(
            NewQuestion(QuestionType.SingleChoice, null, "dev", "pm"), "\"lead\"").IsValid);

    // ---- MultiChoice -------------------------------------------------------------------------

    [Fact]
    public void MultiChoice_accepts_a_known_subset()
        => Assert.True(_validator.Validate(
            NewQuestion(QuestionType.MultiChoice, null, "a", "b", "c"), "[\"a\",\"c\"]").IsValid);

    [Fact]
    public void MultiChoice_rejects_an_unknown_element()
        => Assert.False(_validator.Validate(
            NewQuestion(QuestionType.MultiChoice, null, "a", "b"), "[\"a\",\"x\"]").IsValid);

    [Fact]
    public void MultiChoice_rejects_a_non_array()
        => Assert.False(_validator.Validate(
            NewQuestion(QuestionType.MultiChoice, null, "a", "b"), "\"a\"").IsValid);

    // ---- Misconfiguration / arguments --------------------------------------------------------

    [Fact]
    public void Broken_ValidationRules_JSON_throws_InvalidOperationException()
        => Assert.Throws<InvalidOperationException>(
            () => _validator.Validate(NewQuestion(QuestionType.FreeText, "{ not json"), "\"x\""));

    [Fact]
    public void An_invalid_regex_pattern_throws_InvalidOperationException()
        => Assert.Throws<InvalidOperationException>(
            () => _validator.Validate(NewQuestion(QuestionType.FreeText, "{\"pattern\":\"[\"}"), "\"x\""));

    [Fact]
    public void Validate_throws_on_a_null_question()
        => Assert.Throws<ArgumentNullException>(() => _validator.Validate(null!, "\"x\""));

    [Fact]
    public void Validate_throws_on_a_null_value()
        => Assert.Throws<ArgumentNullException>(
            () => _validator.Validate(NewQuestion(QuestionType.FreeText), null!));
}
