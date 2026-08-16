using Flirty.Domain;
using Flirty.Expressions;
using Flirty.Persistence;
using Flirty.Placeholders;
using Flirty.Runtime;
using Flirty.Tests.Persistence;
using Mediator;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Flirty.Tests.Runtime;

/// <summary>
/// Verifies the <see cref="SubmitAnswerCommandHandler"/> (issue #26) against a real SQLite database
/// (in-memory): persistence of the answer, branching over the <see cref="IExpressionEvaluator"/>
/// (conditional and default transition), the continuous <see cref="SessionAnswer.Sequence"/>,
/// completion at a terminal question as well as the error cases (unknown or completed session, wrong
/// question, misconfigured branching, <c>null</c> dependencies).
/// </summary>
public sealed class SubmitAnswerCommandHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<FlirtyDbContext> _options;

    /// <summary>
    /// Opens a SQLite in-memory connection (which has to stay open, otherwise the database is
    /// discarded) and creates the schema once via <c>EnsureCreated()</c>.
    /// </summary>
    public SubmitAnswerCommandHandlerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<FlirtyDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = CreateContext();
        context.Database.EnsureCreated();
    }

    /// <summary>Closes the connection and thereby discards the in-memory database.</summary>
    public void Dispose() => _connection.Dispose();

    private FlirtyDbContext CreateContext() => new(_options);

    private static SubmitAnswerCommandHandler CreateHandler(FlirtyDbContext context)
        => new(new DialogStore(context), new DynamicExpressoExpressionEvaluator(), new SpyPublisher(),
            PlaceholderRenderer.Disabled);

    private static SubmitAnswerCommandHandler CreateHandler(FlirtyDbContext context, IPublisher publisher)
        => new(new DialogStore(context), new DynamicExpressoExpressionEvaluator(), publisher,
            PlaceholderRenderer.Disabled);

    /// <summary>Creates the branching dialog together with a running session on the entry question.</summary>
    private (Guid SessionId, BranchingDialogIds Ids) SeedBranchingSession(string externalUserKey = "user-1")
    {
        var dialogId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        BranchingDialogIds ids;

        using var arrange = CreateContext();
        arrange.Dialogs.Add(TestDialogFactory.BuildBranchingDialog(dialogId, out ids));
        arrange.DialogSessions.Add(new DialogSession
        {
            Id = sessionId,
            DialogId = dialogId,
            DialogVersion = 1,
            ExternalUserKey = externalUserKey,
            Status = SessionStatus.InProgress,
            CurrentQuestionId = ids.RoleQuestionId,
            StartedAt = TestDialogFactory.SampleTime,
        });
        arrange.SaveChanges();

        return (sessionId, ids);
    }

    // ---- Persistence ------------------------------------------------------------------------

    /// <summary>An answer is persisted as a <see cref="SessionAnswer"/> (value, sequence, timestamp).</summary>
    [Fact]
    public async Task Handle_persists_the_answer()
    {
        var (sessionId, ids) = SeedBranchingSession();

        using (var act = CreateContext())
        {
            await CreateHandler(act).Handle(
                new SubmitAnswerCommand(sessionId, ids.RoleQuestionId, "\"dev\""), default);
        }

        using var assert = CreateContext();
        var session = assert.DialogSessions.Include(s => s.Answers).Single(s => s.Id == sessionId);
        var answer = Assert.Single(session.Answers);
        Assert.Equal(ids.RoleQuestionId, answer.QuestionId);
        Assert.Equal("\"dev\"", answer.Value);
        Assert.Equal(0, answer.Sequence);
        Assert.NotEqual(default, answer.AnsweredAt);
    }

    /// <summary>Across several answers the <see cref="SessionAnswer.Sequence"/> keeps counting up.</summary>
    [Fact]
    public async Task Handle_writes_the_sequence_continuously()
    {
        var (sessionId, ids) = SeedBranchingSession();

        using (var first = CreateContext())
        {
            await CreateHandler(first).Handle(
                new SubmitAnswerCommand(sessionId, ids.RoleQuestionId, "\"dev\""), default);
        }

        // After the first submit the session sits on devDetail (terminal) – the second submit completes.
        using (var second = CreateContext())
        {
            await CreateHandler(second).Handle(
                new SubmitAnswerCommand(sessionId, ids.DevQuestionId, "\"C#\""), default);
        }

        using var assert = CreateContext();
        var session = assert.DialogSessions.Include(s => s.Answers).Single(s => s.Id == sessionId);
        Assert.Equal([0, 1], session.Answers.OrderBy(a => a.Sequence).Select(a => a.Sequence));
    }

    // ---- Branching --------------------------------------------------------------------------

    /// <summary>If the conditional expression matches, the session advances to its target question.</summary>
    [Fact]
    public async Task Handle_a_conditional_transition_leads_to_its_target_question()
    {
        var (sessionId, ids) = SeedBranchingSession();

        SubmitAnswerResult result;
        using (var act = CreateContext())
        {
            result = await CreateHandler(act).Handle(
                new SubmitAnswerCommand(sessionId, ids.RoleQuestionId, "\"dev\""), default);
        }

        Assert.False(result.IsCompleted);
        Assert.NotNull(result.NextQuestion);
        Assert.Equal(ids.DevQuestionId, result.NextQuestion.Id);
        Assert.Equal("devDetail", result.NextQuestion.Key);

        using var assert = CreateContext();
        var session = assert.DialogSessions.Single(s => s.Id == sessionId);
        Assert.Equal(ids.DevQuestionId, session.CurrentQuestionId);
        Assert.Equal(SessionStatus.InProgress, session.Status);
    }

    /// <summary>If no conditional transition matches, the default transition takes effect.</summary>
    [Fact]
    public async Task Handle_without_a_match_the_default_transition_takes_effect()
    {
        var (sessionId, ids) = SeedBranchingSession();

        SubmitAnswerResult result;
        using (var act = CreateContext())
        {
            result = await CreateHandler(act).Handle(
                new SubmitAnswerCommand(sessionId, ids.RoleQuestionId, "\"pm\""), default);
        }

        Assert.False(result.IsCompleted);
        Assert.NotNull(result.NextQuestion);
        Assert.Equal(ids.PmQuestionId, result.NextQuestion.Id);
    }

    // ---- Completion -------------------------------------------------------------------------

    /// <summary>A terminal question (without outgoing transitions) completes the dialog.</summary>
    [Fact]
    public async Task Handle_a_terminal_question_completes_the_dialog()
    {
        var (sessionId, ids) = SeedBranchingSession();

        using (var first = CreateContext())
        {
            await CreateHandler(first).Handle(
                new SubmitAnswerCommand(sessionId, ids.RoleQuestionId, "\"dev\""), default);
        }

        SubmitAnswerResult result;
        using (var second = CreateContext())
        {
            result = await CreateHandler(second).Handle(
                new SubmitAnswerCommand(sessionId, ids.DevQuestionId, "\"C#\""), default);
        }

        Assert.True(result.IsCompleted);
        Assert.Null(result.NextQuestion);

        using var assert = CreateContext();
        var session = assert.DialogSessions.Single(s => s.Id == sessionId);
        Assert.Equal(SessionStatus.Completed, session.Status);
        Assert.NotNull(session.CompletedAt);
        Assert.Null(session.CurrentQuestionId);
    }

    // ---- Error cases ------------------------------------------------------------------------

    /// <summary>An unknown session leads to a <see cref="SessionNotFoundException"/>.</summary>
    [Fact]
    public async Task Handle_an_unknown_session_throws_SessionNotFoundException()
    {
        var unknownSession = Guid.NewGuid();
        using var act = CreateContext();

        var exception = await Assert.ThrowsAsync<SessionNotFoundException>(
            async () => await CreateHandler(act).Handle(
                new SubmitAnswerCommand(unknownSession, Guid.NewGuid(), "\"x\""), default));

        Assert.Equal(unknownSession, exception.SessionId);
    }

    /// <summary>A session that is no longer running accepts no answers.</summary>
    [Fact]
    public async Task Handle_a_completed_session_throws_InvalidOperationException()
    {
        var (sessionId, ids) = SeedBranchingSession();
        using (var complete = CreateContext())
        {
            var session = complete.DialogSessions.Single(s => s.Id == sessionId);
            session.Status = SessionStatus.Completed;
            complete.SaveChanges();
        }

        using var act = CreateContext();

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await CreateHandler(act).Handle(
                new SubmitAnswerCommand(sessionId, ids.RoleQuestionId, "\"dev\""), default));
    }

    /// <summary>Any question other than the currently open one is rejected (editing is #28).</summary>
    [Fact]
    public async Task Handle_the_wrong_question_throws_InvalidOperationException()
    {
        var (sessionId, ids) = SeedBranchingSession();
        using var act = CreateContext();

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await CreateHandler(act).Handle(
                new SubmitAnswerCommand(sessionId, ids.DevQuestionId, "\"C#\""), default));
    }

    /// <summary>Existing transitions with no match and no default count as a misconfiguration.</summary>
    [Fact]
    public async Task Handle_no_match_without_a_default_throws_InvalidOperationException()
    {
        var dialogId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var questionId = Guid.NewGuid();

        using (var arrange = CreateContext())
        {
            arrange.Dialogs.Add(new Dialog
            {
                Id = dialogId, Key = "deadend", Name = "Dead End", Version = 1, IsPublished = true,
                StartQuestionId = questionId, CreatedAt = TestDialogFactory.SampleTime,
                UpdatedAt = TestDialogFactory.SampleTime,
                Questions =
                {
                    new Question
                    {
                        Id = questionId, DialogId = dialogId, Key = "q", Text = "Frage?",
                        Type = QuestionType.FreeText, Order = 0,
                    },
                },
                Transitions =
                {
                    new Transition
                    {
                        Id = Guid.NewGuid(), DialogId = dialogId, FromQuestionId = questionId,
                        Expression = "q == \"never\"", TargetQuestionId = Guid.NewGuid(),
                        Priority = 0, IsDefault = false,
                    },
                },
            });
            arrange.DialogSessions.Add(new DialogSession
            {
                Id = sessionId, DialogId = dialogId, DialogVersion = 1, ExternalUserKey = "user-1",
                Status = SessionStatus.InProgress, CurrentQuestionId = questionId,
                StartedAt = TestDialogFactory.SampleTime,
            });
            arrange.SaveChanges();
        }

        using var act = CreateContext();

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await CreateHandler(act).Handle(
                new SubmitAnswerCommand(sessionId, questionId, "\"other\""), default));
    }

    /// <summary>The constructor rejects a <c>null</c> store.</summary>
    [Fact]
    public void Constructor_throws_on_a_null_store()
        => Assert.Throws<ArgumentNullException>(
            () => new SubmitAnswerCommandHandler(
                null!, new DynamicExpressoExpressionEvaluator(), new SpyPublisher(), PlaceholderRenderer.Disabled));

    /// <summary>The constructor rejects a <c>null</c> evaluator.</summary>
    [Fact]
    public void Constructor_throws_on_a_null_evaluator()
    {
        using var context = CreateContext();
        Assert.Throws<ArgumentNullException>(
            () => new SubmitAnswerCommandHandler(
                new DialogStore(context), null!, new SpyPublisher(), PlaceholderRenderer.Disabled));
    }

    /// <summary>The constructor rejects a <c>null</c> publisher.</summary>
    [Fact]
    public void Constructor_throws_on_a_null_publisher()
    {
        using var context = CreateContext();
        Assert.Throws<ArgumentNullException>(
            () => new SubmitAnswerCommandHandler(
                new DialogStore(context), new DynamicExpressoExpressionEvaluator(), null!, PlaceholderRenderer.Disabled));
    }

    /// <summary>The constructor rejects a <c>null</c> renderer.</summary>
    [Fact]
    public void Constructor_throws_on_a_null_renderer()
    {
        using var context = CreateContext();
        Assert.Throws<ArgumentNullException>(
            () => new SubmitAnswerCommandHandler(
                new DialogStore(context), new DynamicExpressoExpressionEvaluator(), new SpyPublisher(), null!));
    }

    // ---- Trigger notifications --------------------------------------------------------------

    /// <summary>
    /// On advancing, <see cref="AnswerSubmittedNotification"/> and
    /// <see cref="QuestionAnsweredNotification"/> (carrying the follow-up question, not completed) are
    /// published – in that order.
    /// </summary>
    [Fact]
    public async Task Handle_advancing_publishes_AnswerSubmitted_and_QuestionAnswered()
    {
        var (sessionId, ids) = SeedBranchingSession();

        var spy = new SpyPublisher();
        using (var act = CreateContext())
        {
            await CreateHandler(act, spy).Handle(
                new SubmitAnswerCommand(sessionId, ids.RoleQuestionId, "\"dev\""), default);
        }

        Assert.Collection(
            spy.Published,
            published =>
            {
                var answer = Assert.IsType<AnswerSubmittedNotification>(published);
                Assert.Equal(sessionId, answer.SessionId);
                Assert.Equal("branching", answer.DialogKey);
                Assert.Equal(ids.RoleQuestionId, answer.QuestionId);
                Assert.Equal("\"dev\"", answer.Value);
            },
            published =>
            {
                var question = Assert.IsType<QuestionAnsweredNotification>(published);
                Assert.Equal(ids.RoleQuestionId, question.QuestionId);
                Assert.Equal(ids.DevQuestionId, question.NextQuestionId);
                Assert.False(question.IsCompleted);
            });
    }

    /// <summary>
    /// On completion, <see cref="AnswerSubmittedNotification"/>, a final
    /// <see cref="QuestionAnsweredNotification"/> and the <see cref="DialogCompletedNotification"/>
    /// (carrying all answers) are published.
    /// </summary>
    [Fact]
    public async Task Handle_completion_publishes_AnswerSubmitted_QuestionAnswered_and_DialogCompleted()
    {
        var (sessionId, ids) = SeedBranchingSession();
        using (var first = CreateContext())
        {
            await CreateHandler(first).Handle(
                new SubmitAnswerCommand(sessionId, ids.RoleQuestionId, "\"dev\""), default);
        }

        var spy = new SpyPublisher();
        using (var second = CreateContext())
        {
            await CreateHandler(second, spy).Handle(
                new SubmitAnswerCommand(sessionId, ids.DevQuestionId, "\"C#\""), default);
        }

        Assert.Collection(
            spy.Published,
            published => Assert.IsType<AnswerSubmittedNotification>(published),
            published =>
            {
                var question = Assert.IsType<QuestionAnsweredNotification>(published);
                Assert.Null(question.NextQuestionId);
                Assert.True(question.IsCompleted);
            },
            published =>
            {
                var completed = Assert.IsType<DialogCompletedNotification>(published);
                Assert.Equal(sessionId, completed.SessionId);
                Assert.Equal("branching", completed.DialogKey);
                Assert.Equal(2, completed.Answers.Count);
            });
    }
}
