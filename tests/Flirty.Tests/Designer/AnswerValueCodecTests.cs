using Flirty.Designer.Services;
using Flirty.Domain;
using Flirty.Runtime.Admin;
using Flirty.Validation;

namespace Flirty.Tests.Designer;

/// <summary>
/// Tests for the test runner's <see cref="AnswerValueCodec"/> (#43). The core check – as with
/// <see cref="QuestionFormModelTests"/> – is the match against the engine: the raw JSON text produced
/// in the designer has to be exactly what the <see cref="AnswerValidator"/> accepts per
/// <see cref="QuestionType"/>. Otherwise every test run would fail on the designer's own encoding
/// instead of on the dialog.
/// </summary>
public sealed class AnswerValueCodecTests
{
    private static readonly AnswerValidator Validator = new();

    // ---- Encoding: accepted by the engine ----------------------------------------------------

    /// <summary>Free text, date and single choice travel as a JSON string.</summary>
    [Theory]
    [InlineData(QuestionType.FreeText, "Hello world", "\"Hello world\"")]
    [InlineData(QuestionType.Date, "2026-07-22", "\"2026-07-22\"")]
    [InlineData(QuestionType.SingleChoice, "dev", "\"dev\"")]
    public void Encode_produces_JSON_strings(QuestionType type, string input, string expected)
        => Assert.Equal(expected, AnswerValueCodec.Encode(type, input));

    /// <summary>Special characters are escaped, so that valid JSON comes out.</summary>
    [Fact]
    public void Encode_escapes_quotation_marks_in_free_text()
    {
        var encoded = AnswerValueCodec.Encode(QuestionType.FreeText, "He said \"hello\"");

        Assert.True(Validator.Validate(NewQuestion(QuestionType.FreeText), encoded).IsValid);
        Assert.Equal("He said \"hello\"", AnswerValueCodec.Describe(null, encoded));
    }

    /// <summary>Numbers travel as a raw JSON number literal – invariant, even with a decimal comma.</summary>
    [Theory]
    [InlineData("42", "42")]
    [InlineData("3.5", "3.5")]
    [InlineData("3,5", "3.5")]
    [InlineData(" 7 ", "7")]
    public void Encode_produces_invariant_number_literals(string input, string expected)
    {
        var encoded = AnswerValueCodec.Encode(QuestionType.Number, input);

        Assert.Equal(expected, encoded);
        Assert.True(Validator.Validate(NewQuestion(QuestionType.Number), encoded).IsValid);
    }

    /// <summary>
    /// An unreadable number input is deliberately <b>not</b> silently replaced but passed on as a
    /// string – so that the engine rejects it with its own message.
    /// </summary>
    [Fact]
    public void Encode_passes_an_unreadable_number_input_on_to_the_engine()
    {
        var encoded = AnswerValueCodec.Encode(QuestionType.Number, "not a number");

        var result = Validator.Validate(NewQuestion(QuestionType.Number), encoded);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("not a valid number", StringComparison.Ordinal));
    }

    /// <summary>Truth values travel as <c>true</c>/<c>false</c>; everything else counts as <c>false</c>.</summary>
    [Theory]
    [InlineData("true", "true")]
    [InlineData("True", "true")]
    [InlineData("false", "false")]
    [InlineData("", "false")]
    public void Encode_produces_truth_values(string input, string expected)
    {
        var encoded = AnswerValueCodec.Encode(QuestionType.Boolean, input);

        Assert.Equal(expected, encoded);
        Assert.True(Validator.Validate(NewQuestion(QuestionType.Boolean), encoded).IsValid);
    }

    /// <summary>A multi choice travels as a JSON array of strings.</summary>
    [Fact]
    public void Encode_produces_an_array_for_the_multi_choice()
    {
        var question = NewQuestion(QuestionType.MultiChoice, ("csharp", "C#"), ("fsharp", "F#"));

        var encoded = AnswerValueCodec.Encode(QuestionType.MultiChoice, null, ["csharp", "fsharp"]);

        Assert.Equal("[\"csharp\",\"fsharp\"]", encoded);
        Assert.True(Validator.Validate(question, encoded).IsValid);
    }

    /// <summary>An empty multi choice is valid JSON too (the engine checks the required rule separately).</summary>
    [Fact]
    public void Encode_produces_an_empty_array_without_a_choice()
    {
        var encoded = AnswerValueCodec.Encode(QuestionType.MultiChoice, null, []);

        Assert.Equal("[]", encoded);
        Assert.True(Validator.Validate(NewQuestion(QuestionType.MultiChoice), encoded).IsValid);
    }

    /// <summary>A single choice has to run against the configured option values.</summary>
    [Fact]
    public void Encode_yields_the_engines_rejection_for_an_unknown_option()
    {
        var question = NewQuestion(QuestionType.SingleChoice, ("dev", "Developer"));

        Assert.True(Validator.Validate(question, AnswerValueCodec.Encode(question.Type, "dev")).IsValid);
        Assert.False(Validator.Validate(question, AnswerValueCodec.Encode(question.Type, "pm")).IsValid);
    }

    // ---- Display -----------------------------------------------------------------------------

    /// <summary>Choices appear with their label, not with the stored value.</summary>
    [Fact]
    public void Describe_shows_the_label_of_a_choice()
    {
        var question = NewDetail(QuestionType.SingleChoice, ("dev", "Developer"));

        Assert.Equal("Developer", AnswerValueCodec.Describe(question, "\"dev\""));
    }

    /// <summary>A multi choice appears comma-separated with labels.</summary>
    [Fact]
    public void Describe_shows_the_multi_choice_comma_separated()
    {
        var question = NewDetail(QuestionType.MultiChoice, ("csharp", "C#"), ("fsharp", "F#"));

        Assert.Equal("C#, F#", AnswerValueCodec.Describe(question, "[\"csharp\",\"fsharp\"]"));
    }

    /// <summary>Truth values appear as words, not as JSON literals.</summary>
    [Theory]
    [InlineData("true", "Yes")]
    [InlineData("false", "No")]
    public void Describe_shows_truth_values_as_words(string value, string expected)
        => Assert.Equal(expected, AnswerValueCodec.Describe(NewDetail(QuestionType.Boolean), value));

    /// <summary>
    /// If the question no longer belongs to the dialog (deleted), the raw value is read as best it
    /// can be instead of being withheld.
    /// </summary>
    [Fact]
    public void Describe_reads_the_raw_value_without_a_known_question()
        => Assert.Equal("Hello", AnswerValueCodec.Describe(null, "\"Hello\""));

    /// <summary>An unknown option value is shown raw, not suppressed.</summary>
    [Fact]
    public void Describe_shows_an_unknown_option_raw()
    {
        var question = NewDetail(QuestionType.SingleChoice, ("dev", "Developer"));

        Assert.Equal("pm", AnswerValueCodec.Describe(question, "\"pm\""));
    }

    // ---- Round trip --------------------------------------------------------------------------

    /// <summary>
    /// The edit mode reads stored values back into the input fields – encoding them again has to
    /// produce the very same JSON text.
    /// </summary>
    [Theory]
    [InlineData(QuestionType.FreeText, "\"Hello world\"")]
    [InlineData(QuestionType.Date, "\"2026-07-22\"")]
    [InlineData(QuestionType.SingleChoice, "\"dev\"")]
    [InlineData(QuestionType.Number, "42")]
    [InlineData(QuestionType.Boolean, "true")]
    [InlineData(QuestionType.Boolean, "false")]
    [InlineData(QuestionType.MultiChoice, "[\"csharp\",\"fsharp\"]")]
    public void Decode_and_Encode_are_inverse_to_each_other(QuestionType type, string value)
    {
        var (text, selected) = AnswerValueCodec.Decode(type, value);

        Assert.Equal(value, AnswerValueCodec.Encode(type, text, selected));
    }

    // ---- Test data ---------------------------------------------------------------------------

    private static Question NewQuestion(QuestionType type, params (string Value, string Label)[] options)
    {
        var questionId = Guid.NewGuid();
        var question = new Question
        {
            Id = questionId,
            DialogId = Guid.NewGuid(),
            Key = "frage",
            Text = "Frage?",
            Type = type,
            Order = 0,
        };

        foreach (var (value, label) in options)
        {
            question.Options.Add(new AnswerOption
            {
                Id = Guid.NewGuid(),
                QuestionId = questionId,
                Key = value,
                Label = label,
                Value = value,
                Order = question.Options.Count,
            });
        }

        return question;
    }

    private static QuestionDetail NewDetail(QuestionType type, params (string Value, string Label)[] options)
    {
        var question = NewQuestion(type, options);

        return new QuestionDetail(
            question.Id, question.DialogId, question.Key, question.Text, question.Type, question.Order,
            question.IsRequired, question.ValidationRules,
            [.. question.Options.Select(option => new AnswerOptionDetail(
                option.Id, option.QuestionId, option.Key, option.Label, option.Value, option.Order))]);
    }
}
