using Flirty.Domain;
using Flirty.Expressions;

namespace Flirty.Tests.Expressions;

/// <summary>
/// Verifiziert das Kontext-Modell aus Issue #22: den unveränderlichen <see cref="ExpressionContext"/>
/// (Guard auf die Session, leere Default-Sammlungen, Zugriff auf Antworten nach Frage-Schlüssel und
/// auf Loop-Collections nach <c>CollectionKey</c>) sowie die Implementier- und Nutzbarkeit des
/// <see cref="IExpressionEvaluator"/>-Vertrags.
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
    public void Konstruktor_ohne_Answers_und_Collections_setzt_leere_nicht_null_Werte()
    {
        var context = new ExpressionContext(NewSession(), DateTimeOffset.UnixEpoch);

        Assert.NotNull(context.Answers);
        Assert.Empty(context.Answers);
        Assert.NotNull(context.Collections);
        Assert.Empty(context.Collections);
        Assert.Null(context.IterationIndex);
    }

    [Fact]
    public void Konstruktor_mit_null_Session_wirft_ArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new ExpressionContext(null!, DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void Session_und_Now_werden_uebernommen()
    {
        var session = NewSession();
        var now = new DateTimeOffset(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);

        var context = new ExpressionContext(session, now);

        Assert.Same(session, context.Session);
        Assert.Equal(now, context.Now);
    }

    [Fact]
    public void Antworten_sind_nach_QuestionKey_zugreifbar()
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
    public void Loop_Collection_ist_nach_CollectionKey_zugreifbar()
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
    public void IterationIndex_ist_ausserhalb_einer_Schleife_null()
    {
        var context = new ExpressionContext(NewSession(), DateTimeOffset.UnixEpoch);

        Assert.Null(context.IterationIndex);
    }

    [Fact]
    public void IterationIndex_wird_innerhalb_einer_Schleife_uebernommen()
    {
        var context = new ExpressionContext(NewSession(), DateTimeOffset.UnixEpoch, iterationIndex: 2);

        Assert.Equal(2, context.IterationIndex);
    }

    [Fact]
    public void Fake_Evaluator_erhaelt_Ausdruck_und_Kontext()
    {
        var evaluator = new SpyExpressionEvaluator();
        var context = new ExpressionContext(NewSession(), DateTimeOffset.UnixEpoch);

        var result = evaluator.Evaluate("age > 18", context);

        Assert.True(result);
        Assert.Equal("age > 18", evaluator.LastExpression);
        Assert.Same(context, evaluator.LastContext);
    }

    /// <summary>
    /// Minimaler Test-Fake, der belegt, dass der <see cref="IExpressionEvaluator"/>-Vertrag von
    /// außerhalb des Cores implementier- und aufrufbar ist (Signatur-Smoke-Test für Issue #34).
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
