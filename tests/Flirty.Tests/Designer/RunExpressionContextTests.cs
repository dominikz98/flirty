using Flirty.Designer.Services;
using Flirty.Domain;
using Flirty.Expressions;
using Flirty.Persistence;
using Flirty.Placeholders;
using Flirty.Runtime;
using Flirty.Runtime.Admin;
using Flirty.Tests.Persistence;
using Flirty.Tests.Runtime;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Flirty.Tests.Designer;

/// <summary>
/// Tests for the test runner's <see cref="RunExpressionContext"/> (#43). The core check is the match
/// against the core <c>SessionExpressionContextBuilder</c>: the designer recomputes the bindings,
/// because the builder is <c>internal</c> and needs a <see cref="Dialog"/> entity with navigations –
/// the runner has only <see cref="DialogDetail"/> and <see cref="ResumeDialogResult"/>. They must not
/// drift apart nonetheless, otherwise the runner would show different values than the engine
/// actually evaluates.
/// </summary>
public sealed class RunExpressionContextTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<FlirtyDbContext> _options;

    /// <summary>
    /// Opens a SQLite in-memory connection (which has to stay open, otherwise the database is
    /// discarded) and creates the schema once via <c>EnsureCreated()</c>.
    /// </summary>
    public RunExpressionContextTests()
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

    /// <summary>
    /// The match against the engine – checked at every point of a real run with two iterations:
    /// answers per question key, the gathered loop collection and the iteration index have to be
    /// identical in both implementations.
    /// </summary>
    [Fact]
    public async Task Build_matches_the_SessionExpressionContextBuilder_at_every_step()
    {
        var (sessionId, ids) = SeedLoopSession();

        AssertMatchesEngine(sessionId);

        // Iteration 1
        await SubmitAsync(sessionId, ids.PositionQuestionId, "\"Developer\"");
        AssertMatchesEngine(sessionId);
        await SubmitAsync(sessionId, ids.MoreQuestionId, "\"yes\"");
        AssertMatchesEngine(sessionId);

        // Iteration 2
        await SubmitAsync(sessionId, ids.PositionQuestionId, "\"Architect\"");
        AssertMatchesEngine(sessionId);
        await SubmitAsync(sessionId, ids.MoreQuestionId, "\"no\"");
        AssertMatchesEngine(sessionId);

        // Completion (no open question left)
        await SubmitAsync(sessionId, ids.SummaryQuestionId, "\"fertig\"");
        AssertMatchesEngine(sessionId);
    }

    /// <summary>
    /// The collection gathers the entry question's answers per iteration – that is the value the
    /// runner shows under the <c>CollectionKey</c>.
    /// </summary>
    [Fact]
    public async Task Build_gathers_the_answers_per_iteration_under_the_collection_key()
    {
        var (sessionId, ids) = SeedLoopSession();

        await SubmitAsync(sessionId, ids.PositionQuestionId, "\"Developer\"");
        await SubmitAsync(sessionId, ids.MoreQuestionId, "\"yes\"");
        await SubmitAsync(sessionId, ids.PositionQuestionId, "\"Architect\"");

        var snapshot = BuildSnapshot(sessionId);

        Assert.Equal(["\"Developer\"", "\"Architect\""], snapshot.Collections["positions"]);

        // Under the question key stands the answer of the CURRENT iteration, not the first one.
        Assert.Equal("\"Architect\"", snapshot.Answers["position"]);
    }

    /// <summary>
    /// Before the first iteration the collection is empty but <b>bound</b> – otherwise
    /// <c>positions.Count &gt; 0</c> would be an unknown identifier in the expression.
    /// </summary>
    [Fact]
    public void Build_binds_the_collection_even_before_the_first_iteration()
    {
        var (sessionId, _) = SeedLoopSession();

        var snapshot = BuildSnapshot(sessionId);

        Assert.Empty(Assert.Contains("positions", snapshot.Collections));
        Assert.Empty(snapshot.Answers);
        Assert.Null(snapshot.IterationIndex);
    }

    /// <summary>
    /// The iteration index refers to the currently open question – and there to its <b>most recently
    /// given</b> answer, not to the upcoming one. That is the semantics of
    /// <c>LoopResolver.ResolveIterationIndex</c> and therefore exactly the value a condition evaluates
    /// at this point; that is why the runner shows it only as a context binding and not as a "current
    /// iteration" on the question itself.
    /// </summary>
    [Fact]
    public async Task Build_returns_the_iteration_index_of_the_last_answer_to_the_open_question()
    {
        var (sessionId, ids) = SeedLoopSession();

        await SubmitAsync(sessionId, ids.PositionQuestionId, "\"Developer\"");
        await SubmitAsync(sessionId, ids.MoreQuestionId, "\"yes\"");
        await SubmitAsync(sessionId, ids.PositionQuestionId, "\"Architect\"");

        // The open question is "more". It has been answered only once so far – in iteration 0.
        Assert.Equal(0, BuildSnapshot(sessionId).IterationIndex);

        // After the second "more" (iteration 1) the open question is "summary" – that lies outside the loop.
        await SubmitAsync(sessionId, ids.MoreQuestionId, "\"no\"");
        Assert.Null(BuildSnapshot(sessionId).IterationIndex);
    }

    /// <summary>
    /// Answers to questions that (no longer) exist in the dialog are ignored – as in the core, which
    /// maps over the graph's question keys.
    /// </summary>
    [Fact]
    public async Task Build_ignores_answers_without_a_question_in_the_graph()
    {
        var (sessionId, ids) = SeedLoopSession();
        await SubmitAsync(sessionId, ids.PositionQuestionId, "\"Developer\"");

        var detail = LoadDetail(sessionId);
        var state = LoadState(sessionId);

        // Artificially shorten the graph by the answered question.
        var reduced = detail with
        {
            Questions = [.. detail.Questions.Where(question => question.Id != ids.PositionQuestionId)],
        };

        Assert.DoesNotContain("position", RunExpressionContext.Build(reduced, state).Answers);
    }

    // ---- Setup -------------------------------------------------------------------------------

    /// <summary>
    /// Builds both contexts on the same state and compares them. The core builder gets the same
    /// "current question" the runner sees (<see cref="ResumeDialogResult.CurrentQuestion"/>).
    /// </summary>
    /// <param name="sessionId">The session to compare.</param>
    private void AssertMatchesEngine(Guid sessionId)
    {
        var state = LoadState(sessionId);
        var fromDesigner = RunExpressionContext.Build(LoadDetail(sessionId), state);

        using var context = CreateContext();
        var session = context.DialogSessions
            .Include(entity => entity.Answers)
            .Single(entity => entity.Id == sessionId);
        var dialog = LoadDialog(context, session.DialogId);

        var fromEngine = SessionExpressionContextBuilder.Build(
            dialog, session, state.CurrentQuestion?.Id);

        Assert.Equal(fromEngine.Answers, fromDesigner.Answers);
        Assert.Equal(fromEngine.IterationIndex, fromDesigner.IterationIndex);
        Assert.Equal(
            fromEngine.Collections.ToDictionary(entry => entry.Key, entry => entry.Value.ToList()),
            fromDesigner.Collections.ToDictionary(entry => entry.Key, entry => entry.Value.ToList()));
    }

    private RunExpressionSnapshot BuildSnapshot(Guid sessionId)
        => RunExpressionContext.Build(LoadDetail(sessionId), LoadState(sessionId));

    /// <summary>Reads the session state over the same query the runner uses.</summary>
    /// <param name="sessionId">The session to read.</param>
    /// <returns>The state.</returns>
    private ResumeDialogResult LoadState(Guid sessionId)
    {
        using var context = CreateContext();
        return new ResumeDialogQueryHandler(new DialogStore(context), PlaceholderRenderer.Disabled)
            .Handle(new ResumeDialogQuery(sessionId), default)
            .AsTask()
            .GetAwaiter()
            .GetResult();
    }

    /// <summary>Reads the dialog graph in the same navigation-free view the runner uses.</summary>
    /// <param name="sessionId">The session whose pinned dialog version is loaded.</param>
    /// <returns>The view of the dialog.</returns>
    private DialogDetail LoadDetail(Guid sessionId)
    {
        using var context = CreateContext();
        var dialogId = context.DialogSessions.Single(session => session.Id == sessionId).DialogId;

        return AdminProjection.ToDetail(LoadDialog(context, dialogId));
    }

    private static Dialog LoadDialog(FlirtyDbContext context, Guid dialogId)
        => context.Dialogs
            .Include(dialog => dialog.Questions).ThenInclude(question => question.Options)
            .Include(dialog => dialog.Transitions)
            .Include(dialog => dialog.Loops)
            .Include(dialog => dialog.Triggers)
            .Single(dialog => dialog.Id == dialogId);

    /// <summary>Creates the loop dialog together with a running session on the entry question.</summary>
    /// <returns>The session id and the dialog's question ids.</returns>
    private (Guid SessionId, LoopDialogIds Ids) SeedLoopSession()
    {
        var dialogId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        using var arrange = CreateContext();
        arrange.Dialogs.Add(TestDialogFactory.BuildLoopDialog(dialogId, out var ids));
        arrange.DialogSessions.Add(new DialogSession
        {
            Id = sessionId,
            DialogId = dialogId,
            DialogVersion = 1,
            ExternalUserKey = "designer-test-1",
            Status = SessionStatus.InProgress,
            CurrentQuestionId = ids.PositionQuestionId,
            StartedAt = TestDialogFactory.SampleTime,
        });
        arrange.SaveChanges();

        return (sessionId, ids);
    }

    /// <summary>Submits an answer over the real handler in its own context.</summary>
    /// <param name="sessionId">The running session.</param>
    /// <param name="questionId">The question to answer.</param>
    /// <param name="value">The raw JSON answer value.</param>
    private async Task SubmitAsync(Guid sessionId, Guid questionId, string value)
    {
        using var context = CreateContext();
        var handler = new SubmitAnswerCommandHandler(
            new DialogStore(context), new DynamicExpressoExpressionEvaluator(), new SpyPublisher(),
            PlaceholderRenderer.Disabled);

        _ = await handler.Handle(new SubmitAnswerCommand(sessionId, questionId, value), default);
    }
}
