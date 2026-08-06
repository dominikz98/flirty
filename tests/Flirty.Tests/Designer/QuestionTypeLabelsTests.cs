using Flirty.Designer.Models;
using Flirty.Domain;

namespace Flirty.Tests.Designer;

/// <summary>
/// Verifies <see cref="QuestionTypeLabels"/> – the designer's single source of what a question type is
/// called, whether it evaluates answer options and whether the test runner may offer an input for it.
/// </summary>
public sealed class QuestionTypeLabelsTests
{
    /// <summary>
    /// <b>The cheap structural guard for the premise of EPIC 14.</b> <c>Describe</c> ends in a
    /// <c>_ =&gt; type.ToString()</c> fallback, so a new enum member does not break anything – it simply
    /// renders as its bare technical name, in the question list, the type dropdowns, the graph node card
    /// and the screen-reader description, and nothing fails. That is precisely the kind of silent cost
    /// the issue measured across fifteen files. This test makes the next one loud.
    /// </summary>
    [Fact]
    public void Every_question_type_has_a_label_of_its_own()
    {
        foreach (var type in Enum.GetValues<QuestionType>())
        {
            Assert.NotEqual(type.ToString(), QuestionTypeLabels.Describe(type));
        }
    }

    [Fact]
    public void Describe_names_the_custom_type_when_the_question_carries_one()
        => Assert.Equal(
            "Custom type \"color\" (Json)", QuestionTypeLabels.Describe(QuestionType.Json, "color"));

    [Fact]
    public void Describe_falls_back_to_the_plain_json_label_without_a_key()
    {
        Assert.Equal("JSON or custom type (Json)", QuestionTypeLabels.Describe(QuestionType.Json));
        Assert.Equal("JSON or custom type (Json)", QuestionTypeLabels.Describe(QuestionType.Json, "  "));
    }

    /// <summary>The key is only meaningful with <see cref="QuestionType.Json"/> and ignored elsewhere.</summary>
    [Fact]
    public void Describe_ignores_a_custom_type_key_on_another_type()
        => Assert.Equal(
            QuestionTypeLabels.Describe(QuestionType.FreeText),
            QuestionTypeLabels.Describe(QuestionType.FreeText, "color"));

    /// <summary>
    /// A statement about the <b>engine</b>, which does not evaluate options for a JSON question. It says
    /// nothing about a host's validator, which receives them – hence the deliberately separate wording in
    /// the question editor's warning.
    /// </summary>
    [Theory]
    [InlineData(QuestionType.SingleChoice, true)]
    [InlineData(QuestionType.MultiChoice, true)]
    [InlineData(QuestionType.Json, false)]
    [InlineData(QuestionType.FreeText, false)]
    public void UsesOptions_reports_the_choice_types(QuestionType type, bool expected)
        => Assert.Equal(expected, QuestionTypeLabels.UsesOptions(type));

    /// <summary>
    /// The documented limit: a test run writes a real session and delivers real webhooks, and the designer
    /// does not know what shape a host's custom type expects – so it offers no control rather than
    /// guessing a value.
    /// </summary>
    [Fact]
    public void Only_a_json_question_is_unanswerable_in_the_designer()
    {
        Assert.False(QuestionTypeLabels.IsAnswerableInDesigner(QuestionType.Json));

        foreach (var type in Enum.GetValues<QuestionType>().Where(type => type != QuestionType.Json))
        {
            Assert.True(QuestionTypeLabels.IsAnswerableInDesigner(type));
        }
    }
}
