using Flirty.Domain;
using Flirty.Expressions;

namespace Flirty.Tests.Expressions;

/// <summary>
/// Verifies the context model from issue #22: the immutable <see cref="ExpressionContext"/> (guard on
/// the session, empty default collections, access to answers by question key and to loop collections
/// by <c>CollectionKey</c>) as well as the implementability and usability of the
/// <see cref="IExpressionEvaluator"/> contract.
/// </summary>
public sealed class ExpressionContextTests
{
    private static DialogSession NewSession() => new()
    {
        Id = Guid.NewGuid(),
        DialogId = Guid.NewGuid(),
        DialogVersion = 1,
        ExternalUserKey = "user-1",
        Status = SessionStatus.InProgress,
        StartedAt = DateTimeOffset.UnixEpoch,
    };

    [Fact]
    public void Constructor_without_answers_and_collections_sets_empty_non_null_values()
    {
        var context = new ExpressionContext(NewSession(), DateTimeOffset.UnixEpoch);

        Assert.NotNull(context.Answers);
        Assert.Empty(context.Answers);
        Assert.NotNull(context.Collections);
        Assert.Empty(context.Collections);
        Assert.Null(context.IterationIndex);
    }

    [Fact]
    public void Constructor_with_a_null_session_throws_ArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new ExpressionContext(null!, DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void Session_and_Now_are_taken_over()
    {
        var session = NewSession();
        var now = new DateTimeOffset(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);

        var context = new ExpressionContext(session, now);

        Assert.Same(session, context.Session);
        Assert.Equal(now, context.Now);
    }

    [Fact]
    public void Answers_are_accessible_by_QuestionKey()
    {
        var answers = new Dictionary<string, string?>
        {
            ["age"] = "42",
            ["name"] = "\"Ada\"",
        };

        var context = new ExpressionContext(NewSession(), DateTimeOffset.UnixEpoch, answers: answers);

        Assert.Equal("42", context.Answers["age"]);
        Assert.Equal("\"Ada\"", context.Answers["name"]);
    }

    [Fact]
    public void Loop_collection_is_accessible_by_CollectionKey()
    {
        var collections = new Dictionary<string, IReadOnlyList<string?>>
        {
            ["positions"] = ["{\"title\":\"Dev\"}", "{\"title\":\"Lead\"}"],
        };

        var context = new ExpressionContext(NewSession(), DateTimeOffset.UnixEpoch, collections: collections);

        Assert.Equal(2, context.Collections["positions"].Count);
        Assert.Equal("{\"title\":\"Dev\"}", context.Collections["positions"][0]);
    }

    [Fact]
    public void IterationIndex_is_null_outside_a_loop()
    {
        var context = new ExpressionContext(NewSession(), DateTimeOffset.UnixEpoch);

        Assert.Null(context.IterationIndex);
    }

    [Fact]
    public void IterationIndex_is_taken_over_inside_a_loop()
    {
        var context = new ExpressionContext(NewSession(), DateTimeOffset.UnixEpoch, iterationIndex: 2);

        Assert.Equal(2, context.IterationIndex);
    }

    [Fact]
    public void Fake_evaluator_receives_the_expression_and_the_context()
    {
        var evaluator = new SpyExpressionEvaluator();
        var context = new ExpressionContext(NewSession(), DateTimeOffset.UnixEpoch);

        var result = evaluator.Evaluate("age > 18", context);

        Assert.True(result);
        Assert.Equal("age > 18", evaluator.LastExpression);
        Assert.Same(context, evaluator.LastContext);
    }

    /// <summary>
    /// Minimal test fake proving that the <see cref="IExpressionEvaluator"/> contract can be
    /// implemented and called from outside the core (signature smoke test for issue #34).
    /// </summary>
    private sealed class SpyExpressionEvaluator : IExpressionEvaluator
    {
        public string? LastExpression { get; private set; }

        public ExpressionContext? LastContext { get; private set; }

        public bool Evaluate(string expression, ExpressionContext context)
        {
            LastExpression = expression;
            LastContext = context;
            return true;
        }

        public ExpressionValidationResult Validate(string expression, ExpressionContext context)
        {
            LastExpression = expression;
            LastContext = context;
            return ExpressionValidationResult.Valid;
        }
    }
}
