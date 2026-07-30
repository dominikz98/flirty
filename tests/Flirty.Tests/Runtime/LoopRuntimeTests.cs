using Flirty.Domain;
using Flirty.Expressions;
using Flirty.Persistence;
using Flirty.Runtime;
using Flirty.Tests.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Flirty.Tests.Runtime;

/// <summary>
/// Verifies the loop runtime (issue #29) end-to-end over <see cref="SubmitAnswerCommandHandler"/> and
/// <see cref="EditAnswerCommandHandler"/> against a real SQLite database (in-memory): the assignment
/// of <see cref="SessionAnswer.LoopInstanceId"/>/<see cref="SessionAnswer.IterationIndex"/> across
/// several iterations, leaving the cycle over the breaking question, collection- and
/// iteration-index-driven break conditions as well as editing one specific iteration.
/// </summary>
public sealed class LoopRuntimeTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<FlirtyDbContext> _options;

    /// <summary>
    /// Opens a SQLite in-memory connection (which has to stay open, otherwise the database is
    /// discarded) and creates the schema once via <c>EnsureCreated()</c>.
    /// </summary>
    public LoopRuntimeTests()
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

    /// <summary>Creates the loop dialog together with a running session on the entry question.</summary>
    private (Guid SessionId, LoopDialogIds Ids) SeedLoopSession(string loopBackExpression = "more == \"yes\"")
    {
        var dialogId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        LoopDialogIds ids;

        using var arrange = CreateContext();
        arrange.Dialogs.Add(TestDialogFactory.BuildLoopDialog(dialogId, out ids, loopBackExpression));
        arrange.DialogSessions.Add(new DialogSession
        {
            Id = sessionId,
            DialogId = dialogId,
            DialogVersion = 1,
            ExternalUserKey = "user-1",
            Status = SessionStatus.InProgress,
            CurrentQuestionId = ids.PositionQuestionId,
            StartedAt = TestDialogFactory.SampleTime,
        });
        arrange.SaveChanges();

        return (sessionId, ids);
    }

    /// <summary>Submits an answer over the <see cref="SubmitAnswerCommandHandler"/> in its own context.</summary>
    private async Task<SubmitAnswerResult> SubmitAsync(Guid sessionId, Guid questionId, string value)
    {
        using var context = CreateContext();
        var handler = new SubmitAnswerCommandHandler(
            new DialogStore(context), new DynamicExpressoExpressionEvaluator(), new SpyPublisher());
        return await handler.Handle(new SubmitAnswerCommand(sessionId, questionId, value), default);
    }

    /// <summary>Edits an answer over the <see cref="EditAnswerCommandHandler"/> in its own context.</summary>
    private async Task<EditAnswerResult> EditAsync(Guid sessionId, Guid questionId, string value, int? iterationIndex = null)
    {
        using var context = CreateContext();
        var handler = new EditAnswerCommandHandler(
            new DialogStore(context), new DynamicExpressoExpressionEvaluator(), new SpyPublisher());
        return await handler.Handle(new EditAnswerCommand(sessionId, questionId, value, iterationIndex), default);
    }

    private DialogSession LoadSession(Guid sessionId)
    {
        using var context = CreateContext();
        return context.DialogSessions.Include(session => session.Answers).Single(session => session.Id == sessionId);
    }

    // ---- Several iterations -----------------------------------------------------------------

    /// <summary>
    /// Two passes through the loop assign the answers the same
    /// <see cref="SessionAnswer.LoopInstanceId"/> and ascending iteration indexes (0, 1) per question;
    /// the <see cref="SessionAnswer.Sequence"/> keeps running across all iterations.
    /// </summary>
    [Fact]
    public async Task Handle_several_iterations_assigns_the_instance_and_the_index()
    {
        var (sessionId, ids) = SeedLoopSession();

        await SubmitAsync(sessionId, ids.PositionQuestionId, "\"A\"");   // Iteration 0
        await SubmitAsync(sessionId, ids.MoreQuestionId, "\"yes\"");     // -> Loop-Back
        await SubmitAsync(sessionId, ids.PositionQuestionId, "\"B\"");   // Iteration 1
        await SubmitAsync(sessionId, ids.MoreQuestionId, "\"no\"");      // -> Exit

        var session = LoadSession(sessionId);

        var positions = session.Answers
            .Where(answer => answer.QuestionId == ids.PositionQuestionId)
            .OrderBy(answer => answer.Sequence)
            .ToList();
        var moreAnswers = session.Answers
            .Where(answer => answer.QuestionId == ids.MoreQuestionId)
            .OrderBy(answer => answer.Sequence)
            .ToList();

        Assert.Equal(["A", "B"], positions.Select(answer => answer.Value.Trim('"')));
        Assert.Equal([0, 1], positions.Select(answer => answer.IterationIndex));
        Assert.Equal([0, 1], moreAnswers.Select(answer => answer.IterationIndex));

        // All loop answers share the same (non-null) instance id.
        var instanceIds = session.Answers
            .Where(answer => answer.LoopInstanceId is not null)
            .Select(answer => answer.LoopInstanceId!.Value)
            .Distinct()
            .ToList();
        Assert.Single(instanceIds);
        Assert.Equal([0, 1, 2, 3], session.Answers.OrderBy(answer => answer.Sequence).Select(answer => answer.Sequence));
    }

    // ---- Breaking question ------------------------------------------------------------------

    /// <summary>
    /// The breaking question leaves the cycle (exit transition) towards the downstream question; the
    /// answer given there no longer carries loop fields and the dialog completes normally.
    /// </summary>
    [Fact]
    public async Task Handle_the_breaking_question_leaves_the_cycle_and_continues_the_normal_flow()
    {
        var (sessionId, ids) = SeedLoopSession();

        await SubmitAsync(sessionId, ids.PositionQuestionId, "\"A\"");
        var afterBreak = await SubmitAsync(sessionId, ids.MoreQuestionId, "\"no\"");   // -> Exit auf summary

        Assert.False(afterBreak.IsCompleted);
        Assert.NotNull(afterBreak.NextQuestion);
        Assert.Equal(ids.SummaryQuestionId, afterBreak.NextQuestion.Id);

        var completed = await SubmitAsync(sessionId, ids.SummaryQuestionId, "\"fertig\"");
        Assert.True(completed.IsCompleted);

        var session = LoadSession(sessionId);
        var summary = session.Answers.Single(answer => answer.QuestionId == ids.SummaryQuestionId);
        Assert.Null(summary.LoopInstanceId);
        Assert.Null(summary.IterationIndex);
        Assert.Equal(SessionStatus.Completed, session.Status);
    }

    // ---- Collection in the context ----------------------------------------------------------

    /// <summary>
    /// A collection-driven break condition (<c>positions.Count &lt; 2</c>) sees the entry answers
    /// collected per iteration: the loop runs until two positions are recorded and then leaves the
    /// cycle without an explicit "no".
    /// </summary>
    [Fact]
    public async Task The_break_condition_sees_the_collected_collection()
    {
        var (sessionId, ids) = SeedLoopSession(loopBackExpression: "positions.Count < 2");

        await SubmitAsync(sessionId, ids.PositionQuestionId, "\"A\"");
        var afterFirst = await SubmitAsync(sessionId, ids.MoreQuestionId, "\"no\"");   // positions=[A] -> Count 1 < 2 -> Loop-Back
        Assert.Equal(ids.PositionQuestionId, afterFirst.NextQuestion!.Id);

        await SubmitAsync(sessionId, ids.PositionQuestionId, "\"B\"");
        var afterSecond = await SubmitAsync(sessionId, ids.MoreQuestionId, "\"no\"");  // positions=[A,B] -> Count 2 -> Exit
        Assert.Equal(ids.SummaryQuestionId, afterSecond.NextQuestion!.Id);

        var session = LoadSession(sessionId);
        var positions = session.Answers.Count(answer => answer.QuestionId == ids.PositionQuestionId);
        Assert.Equal(2, positions);
    }

    /// <summary>
    /// An iteration-index-driven break condition (<c>iterationIndex &lt; 1</c>) leaves the cycle after
    /// exactly two iterations (index 0 and 1).
    /// </summary>
    [Fact]
    public async Task The_break_condition_sees_the_iteration_index()
    {
        var (sessionId, ids) = SeedLoopSession(loopBackExpression: "iterationIndex < 1");

        await SubmitAsync(sessionId, ids.PositionQuestionId, "\"A\"");
        var afterFirst = await SubmitAsync(sessionId, ids.MoreQuestionId, "\"no\"");   // iterationIndex 0 < 1 -> Loop-Back
        Assert.Equal(ids.PositionQuestionId, afterFirst.NextQuestion!.Id);

        await SubmitAsync(sessionId, ids.PositionQuestionId, "\"B\"");
        var afterSecond = await SubmitAsync(sessionId, ids.MoreQuestionId, "\"no\"");  // iterationIndex 1 -> Exit
        Assert.Equal(ids.SummaryQuestionId, afterSecond.NextQuestion!.Id);
    }

    // ---- Edit within an iteration -----------------------------------------------------------

    /// <summary>
    /// Editing one specific iteration (<c>IterationIndex: 1</c>) overwrites exactly that iteration's
    /// entry answer, discards the downstream answers and recomputes the path.
    /// </summary>
    [Fact]
    public async Task Handle_an_edit_within_an_iteration_overwrites_exactly_that_one_and_invalidates()
    {
        var (sessionId, ids) = SeedLoopSession();
        await SubmitAsync(sessionId, ids.PositionQuestionId, "\"A\"");   // Iteration 0 (seq 0)
        await SubmitAsync(sessionId, ids.MoreQuestionId, "\"yes\"");     // seq 1
        await SubmitAsync(sessionId, ids.PositionQuestionId, "\"B\"");   // Iteration 1 (seq 2)
        await SubmitAsync(sessionId, ids.MoreQuestionId, "\"no\"");      // seq 3 -> summary

        var result = await EditAsync(sessionId, ids.PositionQuestionId, "\"B2\"", iterationIndex: 1);

        Assert.False(result.IsCompleted);
        Assert.Equal(ids.MoreQuestionId, result.NextQuestion!.Id);
        Assert.Equal(1, result.InvalidatedAnswers);   // nur more@Iteration1 (seq 3) liegt hinter position@Iteration1

        var session = LoadSession(sessionId);
        var positions = session.Answers
            .Where(answer => answer.QuestionId == ids.PositionQuestionId)
            .OrderBy(answer => answer.IterationIndex)
            .ToList();
        Assert.Equal(["A", "B2"], positions.Select(answer => answer.Value.Trim('"')));
        Assert.Equal([0, 1], positions.Select(answer => answer.IterationIndex));
    }

    /// <summary>
    /// Without an <c>IterationIndex</c> the handler edits – backwards compatibly – the earliest answer
    /// of the question (iteration 0) and discards all downstream iterations.
    /// </summary>
    [Fact]
    public async Task Handle_an_edit_without_an_IterationIndex_hits_the_earliest_answer()
    {
        var (sessionId, ids) = SeedLoopSession();
        await SubmitAsync(sessionId, ids.PositionQuestionId, "\"A\"");   // Iteration 0 (seq 0)
        await SubmitAsync(sessionId, ids.MoreQuestionId, "\"yes\"");     // seq 1
        await SubmitAsync(sessionId, ids.PositionQuestionId, "\"B\"");   // Iteration 1 (seq 2)
        await SubmitAsync(sessionId, ids.MoreQuestionId, "\"no\"");      // seq 3

        var result = await EditAsync(sessionId, ids.PositionQuestionId, "\"A2\"");

        Assert.Equal(3, result.InvalidatedAnswers);   // more@0, position@1, more@1

        var session = LoadSession(sessionId);
        var positions = session.Answers.Where(answer => answer.QuestionId == ids.PositionQuestionId).ToList();
        var remaining = Assert.Single(positions);
        Assert.Equal("A2", remaining.Value.Trim('"'));
        Assert.Equal(0, remaining.IterationIndex);
    }

    /// <summary>Referring to an iteration that does not exist while editing is rejected.</summary>
    [Fact]
    public async Task Handle_an_edit_of_a_non_existent_iteration_throws_InvalidOperationException()
    {
        var (sessionId, ids) = SeedLoopSession();
        await SubmitAsync(sessionId, ids.PositionQuestionId, "\"A\"");

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await EditAsync(sessionId, ids.PositionQuestionId, "\"X\"", iterationIndex: 5));
    }
}
