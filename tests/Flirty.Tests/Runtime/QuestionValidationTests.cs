using System.ComponentModel.DataAnnotations;
using Flirty.Domain;
using Flirty.Runtime.Admin;

namespace Flirty.Tests.Runtime;

/// <summary>
/// Verifies the authoring-time guard of issue #136: a custom question type key is only allowed on a
/// question of type <see cref="QuestionType.Json"/>. Driven through the same
/// <see cref="Validator"/> the <c>ValidationPipelineBehavior</c> uses, so it checks the wiring
/// (<see cref="IValidatableObject"/> on the command) and not just the rule.
/// </summary>
public sealed class QuestionValidationTests
{
    private static IReadOnlyList<ValidationResult> Validate(object command)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(
            command, new ValidationContext(command), results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void A_custom_type_key_on_a_non_json_question_is_refused_on_create()
    {
        var results = Validate(new CreateQuestionCommand(
            Guid.NewGuid(), "q", "Question?", QuestionType.FreeText, 0, false, null, "color"));

        var result = Assert.Single(results);
        Assert.Equal(nameof(Question.CustomTypeKey), Assert.Single(result.MemberNames));
        Assert.Contains("FreeText", result.ErrorMessage!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_custom_type_key_on_a_non_json_question_is_refused_on_update()
    {
        var results = Validate(new UpdateQuestionCommand(
            Guid.NewGuid(), Guid.NewGuid(), "q", "Question?", QuestionType.Number, 0, false, null, "color"));

        Assert.Equal(
            nameof(Question.CustomTypeKey), Assert.Single(Assert.Single(results).MemberNames));
    }

    [Fact]
    public void A_custom_type_key_on_a_json_question_is_accepted()
        => Assert.Empty(Validate(new CreateQuestionCommand(
            Guid.NewGuid(), "q", "Question?", QuestionType.Json, 0, false, null, "color")));

    [Fact]
    public void A_json_question_without_a_custom_type_key_is_accepted()
        => Assert.Empty(Validate(new CreateQuestionCommand(
            Guid.NewGuid(), "q", "Question?", QuestionType.Json, 0, false, null)));

    /// <summary>
    /// Blank is not a key. Whitespace must not trip the guard, because it is dropped rather than stored –
    /// the same reading <c>RequiredAttribute</c> takes.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_custom_type_key_does_not_trip_the_guard(string? key)
        => Assert.Empty(Validate(new CreateQuestionCommand(
            Guid.NewGuid(), "q", "Question?", QuestionType.FreeText, 0, false, null, key)));
}
