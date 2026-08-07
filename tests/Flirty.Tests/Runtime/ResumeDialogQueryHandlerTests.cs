using Flirty.Domain;
using Flirty.Persistence;
using Flirty.Placeholders;
using Flirty.Runtime;
using Flirty.Tests.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Flirty.Tests.Runtime;

/// <summary>
/// Verifies the <see cref="ResumeDialogQueryHandler"/> (issue #27) against a real SQLite database
/// (in-memory): reading status, current question and previous answers of a running session, the
/// chronological order of the answers by <see cref="SessionAnswer.Sequence"/>, the behaviour on a
/// completed session (no open question) as well as the error cases (unknown session, <c>null</c>
/// store).
/// </summary>
public sealed class ResumeDialogQueryHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<FlirtyDbContext> _options;

    /// <summary>
    /// Opens a SQLite in-memory connection (which has to stay open, otherwise the database is
    /// discarded) and creates the schema once via <c>EnsureCreated()</c>.
    /// </summary>
    public ResumeDialogQueryHandlerTests()
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

    private static ResumeDialogQueryHandler CreateHandler(FlirtyDbContext context)
        => new(new DialogStore(context), PlaceholderRenderer.Disabled);

    // ---- Reading the state ------------------------------------------------------------------

    /// <summary>
    /// A running session returns its status, the currently open question and the previous answers
    /// (including the resolved business question key).
    /// </summary>
    [Fact]
    public async Task Handle_a_running_session_returns_status_current_question_and_answers()
    {
        // After answering role with "dev", the session sits on the follow-up question devDetail.
        var (sessionId, ids) = SeedBranchingSession(
            SessionStatus.InProgress,
            selectCurrentQuestion: dialogIds => dialogIds.DevQuestionId,
            withDetailAnswer: false);

        ResumeDialogResult result;
        using (var act = CreateContext())
        {
            result = await CreateHandler(act).Handle(new ResumeDialogQuery(sessionId), default);
        }

        Assert.Equal(sessionId, result.SessionId);
        Assert.Equal(SessionStatus.InProgress, result.Status);
        Assert.NotNull(result.CurrentQuestion);
        Assert.Equal(ids.DevQuestionId, result.CurrentQuestion.Id);
        Assert.Equal("devDetail", result.CurrentQuestion.Key);

        var answer = Assert.Single(result.Answers);
        Assert.Equal(ids.RoleQuestionId, answer.QuestionId);
        Assert.Equal("role", answer.QuestionKey);
        Assert.Equal("\"dev\"", answer.Value);
        Assert.Equal(0, answer.Sequence);
    }

    /// <summary>The previous answers are returned ascending by <see cref="SessionAnswer.Sequence"/>.</summary>
    [Fact]
    public async Task Handle_the_answers_are_ordered_by_Sequence()
    {
        var (sessionId, _) = SeedBranchingSession(
            SessionStatus.Completed,
            selectCurrentQuestion: _ => null,
            withDetailAnswer: true,
            answersUnsorted: true);

        ResumeDialogResult result;
        using (var act = CreateContext())
        {
            result = await CreateHandler(act).Handle(new ResumeDialogQuery(sessionId), default);
        }

        Assert.Equal([0, 1], result.Answers.Select(answer => answer.Sequence));
        Assert.Equal(["\"dev\"", "\"C#\""], result.Answers.Select(answer => answer.Value));
    }

    /// <summary>
    /// A completed session returns <c>null</c> as the current question, the status
    /// <see cref="SessionStatus.Completed"/> and nevertheless all previous answers.
    /// </summary>
    [Fact]
    public async Task Handle_a_completed_session_returns_a_null_CurrentQuestion()
    {
        var (sessionId, _) = SeedBranchingSession(
            SessionStatus.Completed,
            selectCurrentQuestion: _ => null,
            withDetailAnswer: true);

        ResumeDialogResult result;
        using (var act = CreateContext())
        {
            result = await CreateHandler(act).Handle(new ResumeDialogQuery(sessionId), default);
        }

        Assert.Equal(SessionStatus.Completed, result.Status);
        Assert.Null(result.CurrentQuestion);
        Assert.Equal(2, result.Answers.Count);
    }

    // ---- Error cases ------------------------------------------------------------------------

    /// <summary>An unknown session leads to a <see cref="SessionNotFoundException"/>.</summary>
    [Fact]
    public async Task Handle_an_unknown_session_throws_SessionNotFoundException()
    {
        var unknownSession = Guid.NewGuid();
        using var act = CreateContext();

        var exception = await Assert.ThrowsAsync<SessionNotFoundException>(
            async () => await CreateHandler(act).Handle(new ResumeDialogQuery(unknownSession), default));

        Assert.Equal(unknownSession, exception.SessionId);
    }

    /// <summary>The constructor rejects a <c>null</c> store.</summary>
    [Fact]
    public void Constructor_throws_on_a_null_store()
        => Assert.Throws<ArgumentNullException>(
            () => new ResumeDialogQueryHandler(null!, PlaceholderRenderer.Disabled));

    /// <summary>The constructor rejects a <c>null</c> renderer.</summary>
    [Fact]
    public void Constructor_throws_on_a_null_renderer()
    {
        using var context = CreateContext();
        Assert.Throws<ArgumentNullException>(() => new ResumeDialogQueryHandler(new DialogStore(context), null!));
    }

    // ---- Test-data helpers ------------------------------------------------------------------

    /// <summary>
    /// Creates the branching dialog together with a session in the given state. The currently open
    /// question is chosen from the dialog's question ids via <paramref name="selectCurrentQuestion"/>.
    /// The <c>role</c> answer (sequence 0) is always appended; with
    /// <paramref name="withDetailAnswer"/> the <c>devDetail</c> answer (sequence 1) as well.
    /// <paramref name="answersUnsorted"/> reverses the insertion order, to check the sorting in the
    /// handler.
    /// </summary>
    private (Guid SessionId, BranchingDialogIds Ids) SeedBranchingSession(
        SessionStatus status,
        Func<BranchingDialogIds, Guid?> selectCurrentQuestion,
        bool withDetailAnswer,
        bool answersUnsorted = false)
    {
        var dialogId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        BranchingDialogIds ids;

        using var arrange = CreateContext();
        arrange.Dialogs.Add(TestDialogFactory.BuildBranchingDialog(dialogId, out ids));

        var session = new DialogSession
        {
            Id = sessionId,
            DialogId = dialogId,
            DialogVersion = 1,
            ExternalUserKey = "user-1",
            Status = status,
            CurrentQuestionId = selectCurrentQuestion(ids),
            StartedAt = TestDialogFactory.SampleTime,
            CompletedAt = status == SessionStatus.Completed ? TestDialogFactory.SampleTime : null,
        };

        var role = new SessionAnswer
        {
            Id = Guid.NewGuid(), SessionId = sessionId, QuestionId = ids.RoleQuestionId,
            Value = "\"dev\"", AnsweredAt = TestDialogFactory.SampleTime, Sequence = 0,
        };
        var detail = new SessionAnswer
        {
            Id = Guid.NewGuid(), SessionId = sessionId, QuestionId = ids.DevQuestionId,
            Value = "\"C#\"", AnsweredAt = TestDialogFactory.SampleTime, Sequence = 1,
        };

        if (withDetailAnswer && answersUnsorted)
        {
            session.Answers.Add(detail);
            session.Answers.Add(role);
        }
        else
        {
            session.Answers.Add(role);
            if (withDetailAnswer)
            {
                session.Answers.Add(detail);
            }
        }

        arrange.DialogSessions.Add(session);
        arrange.SaveChanges();

        return (sessionId, ids);
    }
}
