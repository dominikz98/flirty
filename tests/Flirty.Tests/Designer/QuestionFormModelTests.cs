using System.Text.Json;
using Flirty.Designer.Models;
using Flirty.Designer.Services;
using Flirty.Domain;
using Flirty.Runtime.Admin;
using Flirty.Validation;

namespace Flirty.Tests.Designer;

/// <summary>
/// Tests for the <see cref="QuestionFormModel"/> (#39): the mapping between the question editor's
/// input fields and the <see cref="Question.ValidationRules"/> stored as JSON. The core is that the
/// produced JSON is exactly what the engine's <see cref="AnswerValidator"/> reads – and that unknown
/// rules are not silently lost.
/// </summary>
public sealed class QuestionFormModelTests
{
    [Fact]
    public void From_reads_known_rules_into_the_structured_fields()
    {
        var model = QuestionFormModel.From(
            Question(QuestionType.FreeText, """{"minLength":2,"maxLength":50,"pattern":"^[a-z]+$"}"""));

        Assert.False(model.UseRawJson);
        Assert.Equal(2, model.MinLength);
        Assert.Equal(50, model.MaxLength);
        Assert.Equal("^[a-z]+$", model.Pattern);
        Assert.Null(model.Min);
        Assert.Null(model.Max);
    }

    [Fact]
    public void From_reads_rules_case_insensitively_like_the_AnswerValidator()
    {
        var model = QuestionFormModel.From(Question(QuestionType.Number, """{"Min":1,"MAX":9}"""));

        Assert.False(model.UseRawJson);
        Assert.Equal(1m, model.Min);
        Assert.Equal(9m, model.Max);
    }

    [Fact]
    public void From_without_rules_leaves_all_fields_empty()
    {
        var model = QuestionFormModel.From(Question(QuestionType.FreeText, validationRules: null));

        Assert.False(model.UseRawJson);
        Assert.Null(model.RawJson);
        Assert.Null(model.MinLength);
        Assert.Null(model.Pattern);
    }

    [Fact]
    public void From_falls_back_to_raw_JSON_on_unknown_fields()
    {
        const string rules = """{"minLength":2,"ownRule":true}""";

        var model = QuestionFormModel.From(Question(QuestionType.FreeText, rules));

        Assert.True(model.UseRawJson);
        Assert.Equal(rules, model.RawJson);
        // The structured fields stay empty: otherwise saving would discard "ownRule".
        Assert.Null(model.MinLength);
    }

    [Fact]
    public void From_falls_back_to_raw_JSON_on_invalid_JSON()
    {
        const string rules = "{ kein JSON";

        var model = QuestionFormModel.From(Question(QuestionType.FreeText, rules));

        Assert.True(model.UseRawJson);
        Assert.Equal(rules, model.RawJson);
    }

    [Fact]
    public void TryBuildValidationRules_returns_null_without_a_rule_set()
    {
        var model = new QuestionFormModel { Key = "firstname", Text = "Name?", Type = QuestionType.FreeText };

        Assert.True(model.TryBuildValidationRules(out var json, out var error));
        Assert.Null(json);
        Assert.Null(error);
    }

    [Fact]
    public void TryBuildValidationRules_serializes_camelCase_without_null_values()
    {
        var model = new QuestionFormModel
        {
            Key = "firstname",
            Text = "Name?",
            Type = QuestionType.FreeText,
            MaxLength = 50,
        };

        Assert.True(model.TryBuildValidationRules(out var json, out _));
        Assert.Equal("""{"maxLength":50}""", json);
    }

    [Fact]
    public void TryBuildValidationRules_takes_over_only_type_relevant_rules()
    {
        // The type was switched from FreeText to Number: lengths and patterns are then no longer
        // evaluated by the engine and must not stay in the JSON as ineffective ballast.
        var model = new QuestionFormModel
        {
            Key = "age",
            Text = "How old?",
            Type = QuestionType.Number,
            MinLength = 2,
            MaxLength = 50,
            Pattern = "^[a-z]+$",
            Min = 18m,
            Max = 99m,
        };

        Assert.True(model.TryBuildValidationRules(out var json, out _));
        Assert.Equal("""{"min":18,"max":99}""", json);
    }

    [Fact]
    public void TryBuildValidationRules_ignores_rules_for_types_without_rule_support()
    {
        var model = new QuestionFormModel
        {
            Key = "farbe",
            Text = "Welche Farbe?",
            Type = QuestionType.SingleChoice,
            MaxLength = 50,
            Max = 99m,
        };

        Assert.True(model.TryBuildValidationRules(out var json, out _));
        Assert.Null(json);
    }

    [Fact]
    public void TryBuildValidationRules_reports_an_invalid_pattern()
    {
        var model = new QuestionFormModel
        {
            Key = "firstname",
            Text = "Name?",
            Type = QuestionType.FreeText,
            Pattern = "[",
        };

        Assert.False(model.TryBuildValidationRules(out var json, out var error));
        Assert.Null(json);
        Assert.Contains("regular expression", error);
    }

    [Fact]
    public void TryBuildValidationRules_reports_swapped_lengths()
    {
        var model = new QuestionFormModel
        {
            Key = "firstname",
            Text = "Name?",
            Type = QuestionType.FreeText,
            MinLength = 10,
            MaxLength = 5,
        };

        Assert.False(model.TryBuildValidationRules(out _, out var error));
        Assert.Contains("minimum length", error);
    }

    [Fact]
    public void TryBuildValidationRules_reports_swapped_bounds()
    {
        var model = new QuestionFormModel
        {
            Key = "age",
            Text = "How old?",
            Type = QuestionType.Number,
            Min = 99m,
            Max = 18m,
        };

        Assert.False(model.TryBuildValidationRules(out _, out var error));
        Assert.Contains("minimum", error);
    }

    [Fact]
    public void TryBuildValidationRules_passes_unknown_fields_of_the_raw_JSON_through_unchanged()
    {
        const string rules = """{"minLength":2,"ownRule":true}""";
        var model = QuestionFormModel.From(Question(QuestionType.FreeText, rules));

        Assert.True(model.TryBuildValidationRules(out var json, out _));
        Assert.Equal(rules, json);
    }

    [Fact]
    public void TryBuildValidationRules_reports_unreadable_raw_JSON()
    {
        var model = QuestionFormModel.From(Question(QuestionType.FreeText, "{ kein JSON"));

        Assert.False(model.TryBuildValidationRules(out _, out var error));
        Assert.Contains("valid JSON", error);
    }

    [Fact]
    public void TryBuildValidationRules_removes_the_rules_when_the_raw_JSON_is_emptied()
    {
        var model = QuestionFormModel.From(Question(QuestionType.FreeText, """{"ownRule":true}"""));
        model.RawJson = "   ";

        Assert.True(model.TryBuildValidationRules(out var json, out _));
        Assert.Null(json);
    }

    /// <summary>
    /// The core check: the engine's <see cref="AnswerValidator"/> has to actually apply the JSON
    /// produced in the designer. Ties serialization (designer) and deserialization (core) together –
    /// otherwise a drift in the field names would only show up at runtime.
    /// </summary>
    [Fact]
    public void TryBuildValidationRules_produces_JSON_the_AnswerValidator_applies()
    {
        var model = new QuestionFormModel
        {
            Key = "firstname",
            Text = "Name?",
            Type = QuestionType.FreeText,
            MinLength = 2,
            MaxLength = 4,
            Pattern = "^[a-z]+$",
        };

        Assert.True(model.TryBuildValidationRules(out var json, out _));

        var question = new Question { Key = "firstname", Text = "Name?", Type = QuestionType.FreeText, ValidationRules = json };
        var validator = new AnswerValidator();

        Assert.True(validator.Validate(question, "\"abc\"").IsValid);
        Assert.False(validator.Validate(question, "\"a\"").IsValid);        // zu kurz
        Assert.False(validator.Validate(question, "\"abcde\"").IsValid);    // zu lang
        Assert.False(validator.Validate(question, "\"ABC\"").IsValid);      // Muster verletzt
    }

    /// <summary>
    /// The counter-check to the core check, for the numeric branch.
    /// </summary>
    [Fact]
    public void TryBuildValidationRules_produces_number_bounds_the_AnswerValidator_applies()
    {
        var model = new QuestionFormModel
        {
            Key = "age",
            Text = "How old?",
            Type = QuestionType.Number,
            Min = 18m,
            Max = 99m,
        };

        Assert.True(model.TryBuildValidationRules(out var json, out _));

        var question = new Question { Key = "age", Text = "How old?", Type = QuestionType.Number, ValidationRules = json };
        var validator = new AnswerValidator();

        Assert.True(validator.Validate(question, "42").IsValid);
        Assert.False(validator.Validate(question, "17").IsValid);
        Assert.False(validator.Validate(question, "100").IsValid);
    }

    /// <summary>
    /// Round trip: read stored rules in, produce them again unchanged and stay with the field names
    /// dictated by the core type <see cref="ValidationRules"/>.
    /// </summary>
    [Fact]
    public void From_and_TryBuildValidationRules_are_lossless()
    {
        const string rules = """{"minLength":2,"maxLength":50,"pattern":"^[a-z]+$"}""";
        var model = QuestionFormModel.From(Question(QuestionType.FreeText, rules));

        Assert.True(model.TryBuildValidationRules(out var json, out _));

        var original = JsonSerializer.Deserialize<ValidationRules>(rules);
        var restored = JsonSerializer.Deserialize<ValidationRules>(json!);

        Assert.Equal(original, restored);
    }

    // ---- Key suggestion for gestures on the canvas (#103) ------------------------------------------

    [Theory]
    [InlineData(QuestionType.FreeText, "text")]
    [InlineData(QuestionType.Number, "number")]
    [InlineData(QuestionType.Date, "date")]
    [InlineData(QuestionType.Boolean, "yesno")]
    [InlineData(QuestionType.SingleChoice, "choice")]
    [InlineData(QuestionType.MultiChoice, "multi")]
    public void SuggestKey_uses_one_stem_per_question_type(QuestionType type, string expected)
        => Assert.Equal(expected, QuestionFormModel.SuggestKey(type, Dialog()));

    [Fact]
    public void SuggestKey_appends_a_number_when_the_key_is_taken()
    {
        var detail = Dialog(Question(QuestionType.FreeText, null) with { Key = "text" });

        Assert.Equal("text2", QuestionFormModel.SuggestKey(QuestionType.FreeText, detail));
    }

    [Fact]
    public void SuggestKey_also_avoids_a_collection_key()
    {
        // A question key that duplicates a CollectionKey is shadowed by it in the expression context –
        // the gesture would otherwise produce a warning on the spot.
        var detail = Dialog() with
        {
            Loops = [new LoopDetail(Guid.NewGuid(), Guid.NewGuid(), "text", Guid.NewGuid(), Guid.NewGuid())],
        };

        Assert.Equal("text2", QuestionFormModel.SuggestKey(QuestionType.FreeText, detail));
    }

    /// <summary>
    /// Unlike <c>LoopFormModel.SuggestCollectionKey</c>, this must <b>never</b> come out empty: the
    /// suggestion carries a gesture that writes immediately – an empty key would make it fail at the
    /// <c>CreateQuestionCommand</c>.
    /// </summary>
    [Fact]
    public void SuggestKey_returns_a_free_key_even_with_many_collisions()
    {
        var taken = Enumerable.Range(1, 5)
            .Select(index => Question(QuestionType.Number, null) with { Key = index == 1 ? "number" : $"number{index}" })
            .ToArray();

        var suggestion = QuestionFormModel.SuggestKey(QuestionType.Number, Dialog(taken));

        Assert.Equal("number6", suggestion);
        Assert.DoesNotContain(suggestion, taken.Select(frage => frage.Key));
    }

    /// <summary>
    /// The suggestion has to be bindable as an expression variable – question keys are bound in the
    /// branching editor's sample context.
    /// </summary>
    [Theory]
    [InlineData(QuestionType.FreeText)]
    [InlineData(QuestionType.Boolean)]
    [InlineData(QuestionType.MultiChoice)]
    public void SuggestKey_is_always_a_bindable_identifier(QuestionType type)
    {
        var suggestion = QuestionFormModel.SuggestKey(type, Dialog());

        Assert.NotEqual(string.Empty, suggestion);
        Assert.True(DesignerExpressionContext.IsBindable(suggestion));
    }

    /// <summary>Builds a question view the way the <c>GetDialogQuery</c> returns it.</summary>
    /// <param name="type">The question type.</param>
    /// <param name="validationRules">The stored rules as JSON.</param>
    /// <returns>The question view.</returns>
    private static QuestionDetail Question(QuestionType type, string? validationRules)
        => new(
            Guid.NewGuid(), Guid.NewGuid(), "firstname", "What is your name?", type, null, 0, true,
            validationRules, []);

    private static DialogDetail Dialog(params QuestionDetail[] questions)
        => new(
            new DialogSummary(
                Guid.NewGuid(), "dialog", "Dialog", null, 1, false, null,
                DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch),
            questions,
            [],
            [],
            [],
            []);
}
