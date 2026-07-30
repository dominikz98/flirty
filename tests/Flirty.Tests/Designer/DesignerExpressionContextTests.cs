using Flirty.Designer.Models;
using Flirty.Designer.Services;
using Flirty.Domain;
using Flirty.Expressions;
using Flirty.Runtime.Admin;

namespace Flirty.Tests.Designer;

/// <summary>
/// Tests for the branching editor's sample context (#40). The core is that the <b>real</b> engine
/// (<see cref="DynamicExpressoExpressionEvaluator"/>) accepts against this context exactly the
/// expressions that also work at runtime – and rejects typos instead of passing them on into a
/// running session.
/// </summary>
public sealed class DesignerExpressionContextTests
{
    private static readonly IExpressionEvaluator Evaluator = new DynamicExpressoExpressionEvaluator();

    [Theory]
    [InlineData("age > 18")]                          // number
    [InlineData("age >= 18.5")]                       // number against a decimal literal
    [InlineData("role == \"dev\"")]                     // single choice (string)
    [InlineData("consent")]                          // boolean used directly
    [InlineData("consent == true")]                  // boolean in a comparison
    [InlineData("remark.Length > 3")]                // free text as a string
    [InlineData("languages.Count > 0")]                  // multi choice as a list
    [InlineData("skills.Count > 0")]                    // loop collection
    [InlineData("birthday == \"2026-01-01\"")]        // a date is present as a string
    [InlineData("now.Year >= 2026")]                    // reserved context variable
    [InlineData("iterationIndex == 0")]                 // iteration index (int?)
    [InlineData("session.ExternalUserKey == \"customer\"")] // Session
    [InlineData("role == \"dev\" && age > 18")]       // combination
    public void Validate_a_valid_expression_against_the_sample_context(string expression)
    {
        var result = DesignerExpressionContext.Validate(Evaluator, expression, DesignerExpressionContext.Build(Dialog()));

        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public void Validate_reports_a_typo_in_the_question_key_with_a_position()
    {
        var result = DesignerExpressionContext.Validate(
            Evaluator, "rolle == \"dev\"", DesignerExpressionContext.Build(Dialog()));

        Assert.False(result.IsValid);
        Assert.NotNull(result.Error);
        Assert.NotNull(result.ErrorPosition);
    }

    [Fact]
    public void Validate_an_empty_expression_is_valid()
    {
        var result = DesignerExpressionContext.Validate(Evaluator, null, DesignerExpressionContext.Build(Dialog()));

        Assert.True(result.IsValid);
    }

    /// <summary>
    /// At runtime, date answers are bound as a string (the value is raw JSON text). The sample
    /// context has to do the same – otherwise the designer would wave through a comparison that fails
    /// in a running session.
    /// </summary>
    [Fact]
    public void Validate_rejects_a_date_comparison_with_now_like_the_runtime_does()
    {
        var result = DesignerExpressionContext.Validate(
            Evaluator, "birthday < now", DesignerExpressionContext.Build(Dialog()));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Build_binds_loop_collections_even_without_an_iteration()
    {
        var context = DesignerExpressionContext.Build(Dialog());

        // Like the LoopResolver at runtime: the key is always bound, and empty before the first iteration.
        Assert.True(context.Collections.ContainsKey("skills"));
        Assert.Empty(context.Collections["skills"]);
    }

    [Fact]
    public void Describe_returns_question_collection_and_context_identifiers()
    {
        var variables = DesignerExpressionContext.Describe(Dialog());

        Assert.Equal(ExpressionValueKind.Number, variables.Single(variable => variable.Name == "age").Kind);
        Assert.Equal(ExpressionValueKind.List, variables.Single(variable => variable.Name == "skills").Kind);
        Assert.Contains(variables, variable => variable.Name == "now");
        Assert.Contains(variables, variable => variable.Name == "session");
        Assert.All(
            variables.Where(variable => variable.IsUsable),
            variable => Assert.True(
                DesignerExpressionContext.Validate(
                    Evaluator, variable.Example, DesignerExpressionContext.Build(Dialog())).IsValid,
                $"The example '{variable.Example}' is not valid."));
    }

    /// <summary>
    /// A question key such as <c>now</c> is shadowed at runtime by the reserved context variable (the
    /// evaluator sets it last) – the reference table has to say so instead of offering it as usable.
    /// </summary>
    [Fact]
    public void Describe_marks_keys_shadowed_by_reserved_names()
    {
        var detail = Dialog(Question("now", QuestionType.FreeText));

        var variable = DesignerExpressionContext.Describe(detail).First(entry => entry.Name == "now");
        var context = DesignerExpressionContext.Build(detail);

        Assert.False(variable.IsUsable);
        Assert.Contains("Shadowed", variable.Note);
        Assert.DoesNotContain("now", context.Answers.Keys);
    }

    [Fact]
    public void Describe_marks_keys_that_are_not_identifiers()
    {
        var detail = Dialog(Question("vor-name", QuestionType.FreeText));

        var variable = DesignerExpressionContext.Describe(detail).First(entry => entry.Name == "vor-name");

        Assert.False(variable.IsUsable);
        Assert.Contains("identifier", variable.Note);
        Assert.DoesNotContain("vor-name", DesignerExpressionContext.Build(detail).Answers.Keys);
    }

    // The value kind arrives as a name instead of an enum: ExpressionValueKind is internal, so it is
    // no good as the parameter type of a public test method (CS0051).
    [Theory]
    [InlineData("role", "Text", "==", "dev", "role == \"dev\"")]
    [InlineData("role", "Text", "==", "\"quoted\"", "role == \"\\\"quoted\\\"\"")]
    [InlineData("age", "Number", ">", "18", "age > 18")]
    [InlineData("age", "Number", ">", "", "age > 0")]
    [InlineData("consent", "Boolean", "==", "false", "consent == false")]
    [InlineData("skills", "List", "Count >", "0", "skills.Count > 0")]
    [InlineData("skills", "List", "contains", "csharp", "skills.Contains(\"csharp\")")]
    public void BuildCondition_assembles_the_building_block_type_correctly(
        string name, string kind, string operatorToken, string value, string expected)
    {
        var variable = new ExpressionVariable(
            name, Enum.Parse<ExpressionValueKind>(kind), kind, name, true, null);

        Assert.Equal(expected, DesignerExpressionContext.BuildCondition(variable, operatorToken, value));
    }

    /// <summary>A building block assembled from user input has to be compilable too.</summary>
    [Fact]
    public void BuildCondition_produces_a_compilable_expression_even_with_quotation_marks()
    {
        var variable = DesignerExpressionContext.Describe(Dialog()).First(entry => entry.Name == "role");
        var condition = DesignerExpressionContext.BuildCondition(variable, "==", "de\"v");

        var result = DesignerExpressionContext.Validate(Evaluator, condition, DesignerExpressionContext.Build(Dialog()));

        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public void Append_only_combines_with_an_existing_expression()
    {
        Assert.Equal("age > 18", DesignerExpressionContext.Append(null, "age > 18", "&&"));
        Assert.Equal(
            "role == \"dev\" && age > 18",
            DesignerExpressionContext.Append("role == \"dev\"", "age > 18", "&&"));
    }

    [Fact]
    public void Validate_catches_exceptions_from_a_foreign_engine()
    {
        var result = DesignerExpressionContext.Validate(
            new ThrowingEvaluator(), "age > 18", DesignerExpressionContext.Build(Dialog()));

        Assert.False(result.IsValid);
        Assert.Contains("could not be checked", result.Error);
    }

    // ---- Test data --------------------------------------------------------------------------------

    /// <summary>
    /// Builds a dialog graph with one question per type and a loop (collection <c>skills</c>) –
    /// optionally extended by further questions.
    /// </summary>
    private static DialogDetail Dialog(params QuestionDetail[] additional)
    {
        var dialogId = Guid.NewGuid();

        var questions = new List<QuestionDetail>
        {
            Question("role", QuestionType.SingleChoice, dialogId, "dev"),
            Question("age", QuestionType.Number, dialogId),
            Question("consent", QuestionType.Boolean, dialogId),
            Question("remark", QuestionType.FreeText, dialogId),
            Question("birthday", QuestionType.Date, dialogId),
            Question("languages", QuestionType.MultiChoice, dialogId, "de"),
        };
        questions.AddRange(additional);

        return new DialogDetail(
            new DialogSummary(
                dialogId, "onboarding", "Onboarding", null, 1, false, null,
                DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch),
            questions,
            [],
            [new LoopDetail(Guid.NewGuid(), dialogId, "skills", Guid.NewGuid(), Guid.NewGuid())],
            [],
            []);
    }

    private static QuestionDetail Question(
        string key, QuestionType type, Guid? dialogId = null, string? optionValue = null)
    {
        var questionId = Guid.NewGuid();
        IReadOnlyList<AnswerOptionDetail> options = optionValue is null
            ? []
            : [new AnswerOptionDetail(Guid.NewGuid(), questionId, optionValue, optionValue, optionValue, 0)];

        return new QuestionDetail(
            questionId, dialogId ?? Guid.NewGuid(), key, $"Frage {key}?", type, 0, false, null, options);
    }

    /// <summary>Hand-written test double: an engine that throws while checking (no mocking framework).</summary>
    private sealed class ThrowingEvaluator : IExpressionEvaluator
    {
        public bool Evaluate(string expression, ExpressionContext context)
            => throw new InvalidOperationException("kaputt");

        public ExpressionValidationResult Validate(string expression, ExpressionContext context)
            => throw new InvalidOperationException("kaputt");
    }
}
