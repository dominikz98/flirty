using Flirty.Domain;
using Flirty.Expressions;

namespace Flirty.Tests.Expressions;

/// <summary>
/// Verifies the sandboxed default engine from issue #23
/// (<see cref="DynamicExpressoExpressionEvaluator"/>): typed evaluation of answers and loop
/// collections, AND/OR combinations, access to the context variables as well as the
/// sandbox/injection defence (no reflection, no non-whitelisted types, no assignment).
/// </summary>
public sealed class DynamicExpressoExpressionEvaluatorTests
{
    private static readonly DynamicExpressoExpressionEvaluator Evaluator = new();

    private static DialogSession NewSession() => new()
    {
        Id = Guid.NewGuid(),
        DialogId = Guid.NewGuid(),
        DialogVersion = 1,
        ExternalUserKey = "user-1",
        Status = SessionStatus.InProgress,
        StartedAt = DateTimeOffset.UnixEpoch,
    };

    private static ExpressionContext Context(
        IReadOnlyDictionary<string, string?>? answers = null,
        IReadOnlyDictionary<string, IReadOnlyList<string?>>? collections = null,
        DateTimeOffset? now = null,
        int? iterationIndex = null)
        => new(NewSession(), now ?? DateTimeOffset.UnixEpoch, answers, collections, iterationIndex);

    [Theory]
    [InlineData("42", true)]
    [InlineData("18", false)]
    [InlineData("10", false)]
    public void Numeric_comparison_evaluates_the_answer_typed(string age, bool expected)
    {
        var context = Context(new Dictionary<string, string?> { ["age"] = age });

        Assert.Equal(expected, Evaluator.Evaluate("age > 18", context));
    }

    [Fact]
    public void And_combination_combines_a_number_and_a_boolean()
    {
        var context = Context(new Dictionary<string, string?>
        {
            ["age"] = "42",
            ["verified"] = "true",
        });

        Assert.True(Evaluator.Evaluate("age > 18 && verified == true", context));
        Assert.False(Evaluator.Evaluate("age > 18 && verified == false", context));
    }

    [Fact]
    public void Or_combination_matches_when_one_branch_is_true()
    {
        var context = Context(new Dictionary<string, string?> { ["age"] = "42" });

        Assert.True(Evaluator.Evaluate("age > 100 || age > 18", context));
    }

    [Fact]
    public void Boolean_answer_can_be_used_directly_as_a_condition()
    {
        var context = Context(new Dictionary<string, string?> { ["verified"] = "true" });

        Assert.True(Evaluator.Evaluate("verified", context));
    }

    [Fact]
    public void String_answer_is_deserialized_from_JSON()
    {
        var context = Context(new Dictionary<string, string?> { ["name"] = "\"Ada\"" });

        Assert.True(Evaluator.Evaluate("name == \"Ada\"", context));
        Assert.False(Evaluator.Evaluate("name == \"Bob\"", context));
    }

    [Fact]
    public void Non_JSON_value_is_treated_as_a_raw_string()
    {
        // An unquoted choice key is not valid JSON -> fall back to the raw string.
        var context = Context(new Dictionary<string, string?> { ["status"] = "active" });

        Assert.True(Evaluator.Evaluate("status == \"active\"", context));
    }

    [Fact]
    public void Loop_collection_Count_is_evaluable()
    {
        var context = Context(collections: new Dictionary<string, IReadOnlyList<string?>>
        {
            ["positions"] = ["{\"title\":\"Dev\"}", "{\"title\":\"Lead\"}"],
        });

        Assert.True(Evaluator.Evaluate("positions.Count > 0", context));
        Assert.True(Evaluator.Evaluate("positions.Count == 2", context));
    }

    [Fact]
    public void Empty_loop_collection_has_Count_zero()
    {
        var context = Context(collections: new Dictionary<string, IReadOnlyList<string?>>
        {
            ["positions"] = [],
        });

        Assert.False(Evaluator.Evaluate("positions.Count > 0", context));
    }

    [Fact]
    public void Context_variable_now_is_available()
    {
        var context = Context(now: new DateTimeOffset(2026, 7, 16, 12, 0, 0, TimeSpan.Zero));

        Assert.True(Evaluator.Evaluate("now.Year == 2026", context));
    }

    [Theory]
    [InlineData("System.IO.File.ReadAllText(\"secret.txt\") != null")] // type not on the whitelist
    [InlineData("\"x\".GetType().Assembly != null")]                    // reflection is blocked
    [InlineData("typeof(System.Environment) != null")]                  // type/reflection access
    [InlineData("unknownVariable > 1")]                                 // unknown identifier
    public void Non_whitelisted_or_invalid_expressions_throw(string expression)
    {
        Assert.Throws<ExpressionEvaluationException>(() => Evaluator.Evaluate(expression, Context()));
    }

    [Fact]
    public void Assignment_is_disabled_and_throws()
    {
        var context = Context(new Dictionary<string, string?> { ["age"] = "42" });

        Assert.Throws<ExpressionEvaluationException>(() => Evaluator.Evaluate("age = 99", context));
    }

    [Fact]
    public void Non_boolean_expression_throws()
    {
        var context = Context(new Dictionary<string, string?> { ["age"] = "42" });

        Assert.Throws<ExpressionEvaluationException>(() => Evaluator.Evaluate("age", context));
    }

    [Fact]
    public void The_thrown_exception_carries_the_expression()
    {
        var exception = Assert.Throws<ExpressionEvaluationException>(
            () => Evaluator.Evaluate("unknownVariable > 1", Context()));

        Assert.Equal("unknownVariable > 1", exception.Expression);
        Assert.NotNull(exception.InnerException);
    }

    [Fact]
    public void Empty_expression_throws_ArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Evaluator.Evaluate("   ", Context()));
    }

    [Fact]
    public void Null_expression_throws_ArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => Evaluator.Evaluate(null!, Context()));
    }

    [Fact]
    public void Null_context_throws_ArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => Evaluator.Evaluate("age > 18", null!));
    }

    // ---- Validation / compile check (issue #24) ----

    private static ExpressionContext ValidationContext() => Context(
        answers: new Dictionary<string, string?>
        {
            ["age"] = "42",
            ["verified"] = "true",
            ["name"] = "\"Ada\"",
        },
        collections: new Dictionary<string, IReadOnlyList<string?>>
        {
            ["positions"] = ["{\"title\":\"Dev\"}"],
        },
        now: new DateTimeOffset(2026, 7, 16, 12, 0, 0, TimeSpan.Zero));

    [Theory]
    [InlineData("age > 18")]                     // comparison operator
    [InlineData("age > 18 && verified == true")] // AND combination
    [InlineData("age > 100 || age > 18")]        // OR combination
    [InlineData("verified")]                      // boolean answer used directly
    [InlineData("positions.Count > 0")]          // loop collection
    [InlineData("name == \"Ada\"")]              // string comparison
    [InlineData("now.Year == 2026")]             // context variable
    public void Validate_a_valid_expression_is_valid(string expression)
    {
        var result = Evaluator.Validate(expression, ValidationContext());

        Assert.True(result.IsValid);
        Assert.Null(result.Error);
        Assert.Null(result.ErrorPosition);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_an_empty_expression_is_valid(string? expression)
    {
        // Null/empty counts as "unconditionally matching" (consistent with the runtime semantics).
        var result = Evaluator.Validate(expression!, ValidationContext());

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("age > > 18")] // duplicated operator
    [InlineData("(age > 18")]  // unbalanced parenthesis
    public void Validate_a_syntax_error_is_invalid(string expression)
    {
        var result = Evaluator.Validate(expression, ValidationContext());

        Assert.False(result.IsValid);
        Assert.NotNull(result.Error);
    }

    [Theory]
    [InlineData("System.IO.File.ReadAllText(\"secret.txt\") != null")] // type not on the whitelist
    [InlineData("\"x\".GetType().Assembly != null")]                    // reflection is blocked
    [InlineData("typeof(System.Environment) != null")]                  // type/reflection access
    public void Validate_an_injection_is_invalid(string expression)
    {
        var result = Evaluator.Validate(expression, ValidationContext());

        Assert.False(result.IsValid);
        Assert.NotNull(result.Error);
    }

    /// <summary>
    /// A reflection access is not only rejected but also explained understandably: in its own message
    /// DynamicExpresso advises turning reflection on via <c>Interpreter.EnableReflection()</c> – a hint
    /// aimed at whoever embeds the library, not at the dialog author in the designer, and opposed to
    /// the sandbox decision (ADR 0004).
    /// </summary>
    /// <remarks>
    /// Where the library's boundary runs: it kicks in on members that themselves return a reflective
    /// object (<c>Assembly</c>, <c>MethodInfo</c> …). A bare <c>GetType()</c> and <c>GetType().Name</c>
    /// (a string) pass through – no code can be executed from those, it stays at the type name.
    /// </remarks>
    [Theory]
    [InlineData("\"x\".GetType().Assembly != null")]
    [InlineData("session.GetType().Assembly != null")]
    public void Validate_reflection_reports_its_own_reason_without_the_EnableReflection_advice(string expression)
    {
        var result = Evaluator.Validate(expression, Context());

        Assert.False(result.IsValid);
        Assert.Contains("reflection", result.Error!, StringComparison.Ordinal);
        Assert.DoesNotContain("EnableReflection", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The same message applies at runtime too – an expression can come into being bypassing the designer.</summary>
    [Fact]
    public void Evaluate_reflection_reports_its_own_reason_without_the_EnableReflection_advice()
    {
        var exception = Assert.Throws<ExpressionEvaluationException>(
            () => Evaluator.Evaluate("session.GetType().Assembly != null", Context()));

        Assert.Contains("reflection", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("EnableReflection", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_an_unknown_identifier_is_invalid_with_a_position()
    {
        var result = Evaluator.Validate("unknownVariable > 1", Context());

        Assert.False(result.IsValid);
        Assert.NotNull(result.Error);
        Assert.NotNull(result.ErrorPosition);
    }

    [Fact]
    public void Validate_an_assignment_is_invalid()
    {
        var result = Evaluator.Validate("age = 99", ValidationContext());

        Assert.False(result.IsValid);
        Assert.NotNull(result.Error);
    }

    [Theory]
    [InlineData("age")] // long result
    [InlineData("42")]  // int result
    public void Validate_a_non_boolean_expression_is_invalid(string expression)
    {
        var result = Evaluator.Validate(expression, ValidationContext());

        Assert.False(result.IsValid);
        Assert.Contains("boolean", result.Error!);
    }

    [Fact]
    public void Validate_does_not_throw_on_a_faulty_expression()
    {
        var exception = Record.Exception(
            () => Evaluator.Validate("System.IO.File.ReadAllText(\"x\")", Context()));

        Assert.Null(exception);
    }

    [Fact]
    public void Validate_with_a_null_context_throws_ArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => Evaluator.Validate("age > 18", null!));
    }
}
