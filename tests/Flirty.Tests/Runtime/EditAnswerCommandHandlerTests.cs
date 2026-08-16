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
/// Verifies the <see cref="EditAnswerCommandHandler"/> (issue #28) against a real SQLite database
/// (in-memory): overwriting an earlier answer, invalidating the downstream answers, the path
/// recomputation over branching (branch switch / same branch), reopening a completed session as well
/// as the error cases (unknown or abandoned session, an unanswered or foreign question, <c>null</c>
/// dependencies).
/// </summary>
public sealed class EditAnswerCommandHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<FlirtyDbContext> _options;

    /// <summary>
    /// Opens a SQLite in-memory connection (which has to stay open, otherwise the database is
    /// discarded) and creates the schema once via <c>EnsureCreated()</c>.
    /// </summary>
    public EditAnswerCommandHandlerTests()
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

    private static EditAnswerCommandHandler CreateHandler(FlirtyDbContext context)
        => new(new DialogStore(context), new DynamicExpressoExpressionEvaluator(), new SpyPublisher(),
            PlaceholderRenderer.Disabled);

    private static EditAnswerCommandHandler CreateHandler(FlirtyDbContext context, IPublisher publisher)
        => new(new DialogStore(context), new DynamicExpressoExpressionEvaluator(), publisher,
            PlaceholderRenderer.Disabled);

    /// <summary>
    /// Creates the branching dialog together with a <b>completed</b> session that walked the
    /// <c>dev</c> branch completely: <c>role</c> = <c>"dev"</c> (sequence 0) and <c>devDetail</c> =
    /// <c>"C#"</c> (sequence 1). The basis for the edit/invalidation/reopen cases.
    /// </summary>
    private (Guid SessionId, BranchingDialogIds Ids) SeedCompletedDevSession(string externalUserKey = "user-1")
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
            Status = SessionStatus.Completed,
            CurrentQuestionId = null,
            StartedAt = TestDialogFactory.SampleTime,
            CompletedAt = TestDialogFactory.SampleTime,
            Answers =
            {
                new SessionAnswer
                {
                    Id = Guid.NewGuid(), SessionId = sessionId, QuestionId = ids.RoleQuestionId,
                    Value = "\"dev\"", AnsweredAt = TestDialogFactory.SampleTime, Sequence = 0,
                },
                new SessionAnswer
                {
                    Id = Guid.NewGuid(), SessionId = sessionId, QuestionId = ids.DevQuestionId,
                    Value = "\"C#\"", AnsweredAt = TestDialogFactory.SampleTime, Sequence = 1,
                },
            },
        });
        arrange.SaveChanges();

        return (sessionId, ids);
    }

    // ---- Overwriting ------------------------------------------------------------------------

    /// <summary>The edited answer is overwritten; value and timestamp change, the sequence stays.</summary>
    [Fact]
    public async Task Handle_overwrites_the_answer()
    {
        var (sessionId, ids) = SeedCompletedDevSession();

        using (var act = CreateContext())
        {
            await CreateHandler(act).Handle(
                new EditAnswerCommand(sessionId, ids.RoleQuestionId, "\"pm\""), default);
        }

        using var assert = CreateContext();
        var session = assert.DialogSessions.Include(s => s.Answers).Single(s => s.Id == sessionId);
        var role = session.Answers.Single(a => a.QuestionId == ids.RoleQuestionId);
        Assert.Equal("\"pm\"", role.Value);
        Assert.Equal(0, role.Sequence);
        Assert.NotEqual(TestDialogFactory.SampleTime, role.AnsweredAt);
    }

    // ---- Invalidation -----------------------------------------------------------------------

    /// <summary>Downstream answers are discarded; only answers up to the edited question remain.</summary>
    [Fact]
    public async Task Handle_invalidates_the_downstream_answers()
    {
        var (sessionId, ids) = SeedCompletedDevSession();

        EditAnswerResult result;
        using (var act = CreateContext())
        {
            result = await CreateHandler(act).Handle(
                new EditAnswerCommand(sessionId, ids.RoleQuestionId, "\"pm\""), default);
        }

        Assert.Equal(1, result.InvalidatedAnswers);

        using var assert = CreateContext();
        var session = assert.DialogSessions.Include(s => s.Answers).Single(s => s.Id == sessionId);
        var answer = Assert.Single(session.Answers);
        Assert.Equal(ids.RoleQuestionId, answer.QuestionId);
    }

    // ---- Path recomputation -----------------------------------------------------------------

    /// <summary>A changed choice leads over branching into a different branch (a new follow-up question).</summary>
    [Fact]
    public async Task Handle_a_changed_choice_leads_into_a_new_branch()
    {
        var (sessionId, ids) = SeedCompletedDevSession();

        EditAnswerResult result;
        using (var act = CreateContext())
        {
            result = await CreateHandler(act).Handle(
                new EditAnswerCommand(sessionId, ids.RoleQuestionId, "\"pm\""), default);
        }

        Assert.False(result.IsCompleted);
        Assert.NotNull(result.NextQuestion);
        Assert.Equal(ids.PmQuestionId, result.NextQuestion.Id);
        Assert.Equal("pmDetail", result.NextQuestion.Key);

        using var assert = CreateContext();
        var session = assert.DialogSessions.Single(s => s.Id == sessionId);
        Assert.Equal(ids.PmQuestionId, session.CurrentQuestionId);
    }

    /// <summary>If the branch stays the same, the same follow-up question is set – the downstream answer is discarded anyway.</summary>
    [Fact]
    public async Task Handle_the_same_value_sets_the_same_follow_up_question()
    {
        var (sessionId, ids) = SeedCompletedDevSession();

        EditAnswerResult result;
        using (var act = CreateContext())
        {
            result = await CreateHandler(act).Handle(
                new EditAnswerCommand(sessionId, ids.RoleQuestionId, "\"dev\""), default);
        }

        Assert.False(result.IsCompleted);
        Assert.NotNull(result.NextQuestion);
        Assert.Equal(ids.DevQuestionId, result.NextQuestion.Id);
        Assert.Equal(1, result.InvalidatedAnswers);
    }

    // ---- Session status ---------------------------------------------------------------------

    /// <summary>A completed session is reopened when the recomputation is non-terminal.</summary>
    [Fact]
    public async Task Handle_reopens_a_completed_session()
    {
        var (sessionId, ids) = SeedCompletedDevSession();

        using (var act = CreateContext())
        {
            await CreateHandler(act).Handle(
                new EditAnswerCommand(sessionId, ids.RoleQuestionId, "\"pm\""), default);
        }

        using var assert = CreateContext();
        var session = assert.DialogSessions.Single(s => s.Id == sessionId);
        Assert.Equal(SessionStatus.InProgress, session.Status);
        Assert.Null(session.CompletedAt);
        Assert.Equal(ids.PmQuestionId, session.CurrentQuestionId);
    }

    /// <summary>If the terminal question is edited, the dialog stays completed (no invalidation).</summary>
    [Fact]
    public async Task Handle_editing_the_terminal_question_stays_completed()
    {
        var (sessionId, ids) = SeedCompletedDevSession();

        EditAnswerResult result;
        using (var act = CreateContext())
        {
            result = await CreateHandler(act).Handle(
                new EditAnswerCommand(sessionId, ids.DevQuestionId, "\"Rust\""), default);
        }

        Assert.True(result.IsCompleted);
        Assert.Null(result.NextQuestion);
        Assert.Equal(0, result.InvalidatedAnswers);

        using var assert = CreateContext();
        var session = assert.DialogSessions.Include(s => s.Answers).Single(s => s.Id == sessionId);
        Assert.Equal(SessionStatus.Completed, session.Status);
        Assert.NotNull(session.CompletedAt);
        Assert.Null(session.CurrentQuestionId);
        Assert.Equal("\"Rust\"", session.Answers.Single(a => a.QuestionId == ids.DevQuestionId).Value);
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
                new EditAnswerCommand(unknownSession, Guid.NewGuid(), "\"x\""), default));

        Assert.Equal(unknownSession, exception.SessionId);
    }

    /// <summary>A question that has not been answered yet cannot be edited.</summary>
    [Fact]
    public async Task Handle_an_unanswered_question_throws_InvalidOperationException()
    {
        // The session walked the dev branch – pmDetail was never answered.
        var (sessionId, ids) = SeedCompletedDevSession();
        using var act = CreateContext();

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await CreateHandler(act).Handle(
                new EditAnswerCommand(sessionId, ids.PmQuestionId, "\"x\""), default));
    }

    /// <summary>An abandoned session cannot be edited.</summary>
    [Fact]
    public async Task Handle_an_abandoned_session_throws_InvalidOperationException()
    {
        var (sessionId, ids) = SeedCompletedDevSession();
        using (var abandon = CreateContext())
        {
            var session = abandon.DialogSessions.Single(s => s.Id == sessionId);
            session.Status = SessionStatus.Abandoned;
            abandon.SaveChanges();
        }

        using var act = CreateContext();

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await CreateHandler(act).Handle(
                new EditAnswerCommand(sessionId, ids.RoleQuestionId, "\"pm\""), default));
    }

    /// <summary>A question that does not belong to the dialog is rejected.</summary>
    [Fact]
    public async Task Handle_a_foreign_question_throws_InvalidOperationException()
    {
        var (sessionId, _) = SeedCompletedDevSession();
        using var act = CreateContext();

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await CreateHandler(act).Handle(
                new EditAnswerCommand(sessionId, Guid.NewGuid(), "\"x\""), default));
    }

    /// <summary>The constructor rejects a <c>null</c> store.</summary>
    [Fact]
    public void Constructor_throws_on_a_null_store()
        => Assert.Throws<ArgumentNullException>(
            () => new EditAnswerCommandHandler(
                null!, new DynamicExpressoExpressionEvaluator(), new SpyPublisher(), PlaceholderRenderer.Disabled));

    /// <summary>The constructor rejects a <c>null</c> evaluator.</summary>
    [Fact]
    public void Constructor_throws_on_a_null_evaluator()
    {
        using var context = CreateContext();
        Assert.Throws<ArgumentNullException>(
            () => new EditAnswerCommandHandler(
                new DialogStore(context), null!, new SpyPublisher(), PlaceholderRenderer.Disabled));
    }

    /// <summary>The constructor rejects a <c>null</c> publisher.</summary>
    [Fact]
    public void Constructor_throws_on_a_null_publisher()
    {
        using var context = CreateContext();
        Assert.Throws<ArgumentNullException>(
            () => new EditAnswerCommandHandler(
                new DialogStore(context), new DynamicExpressoExpressionEvaluator(), null!, PlaceholderRenderer.Disabled));
    }

    /// <summary>The constructor rejects a <c>null</c> renderer.</summary>
    [Fact]
    public void Constructor_throws_on_a_null_renderer()
    {
        using var context = CreateContext();
        Assert.Throws<ArgumentNullException>(
            () => new EditAnswerCommandHandler(
                new DialogStore(context), new DynamicExpressoExpressionEvaluator(), new SpyPublisher(), null!));
    }

    // ---- Trigger notifications --------------------------------------------------------------

    /// <summary>
    /// Edits a terminal question so that the recomputation completes again: exactly one
    /// <see cref="DialogCompletedNotification"/> (carrying the answers) is published.
    /// </summary>
    [Fact]
    public async Task Handle_completion_publishes_DialogCompleted()
    {
        var (sessionId, ids) = SeedCompletedDevSession();

        var spy = new SpyPublisher();
        using (var act = CreateContext())
        {
            await CreateHandler(act, spy).Handle(
                new EditAnswerCommand(sessionId, ids.DevQuestionId, "\"Rust\""), default);
        }

        var notification = Assert.IsType<DialogCompletedNotification>(Assert.Single(spy.Published));
        Assert.Equal(sessionId, notification.SessionId);
        Assert.Equal("branching", notification.DialogKey);
        Assert.Equal(2, notification.Answers.Count);
    }

    /// <summary>
    /// If the recomputation leads to a non-terminal follow-up question (a reopen), no notification is
    /// published, deliberately.
    /// </summary>
    [Fact]
    public async Task Handle_a_reopen_publishes_no_notification()
    {
        var (sessionId, ids) = SeedCompletedDevSession();

        var spy = new SpyPublisher();
        using (var act = CreateContext())
        {
            var result = await CreateHandler(act, spy).Handle(
                new EditAnswerCommand(sessionId, ids.RoleQuestionId, "\"pm\""), default);
            Assert.False(result.IsCompleted);
        }

        Assert.Empty(spy.Published);
    }
}
