using Flirty.Designer.Models;
using Flirty.Designer.Services;
using Flirty.Domain;
using Flirty.Expressions;
using Flirty.Persistence;
using Flirty.Runtime;
using Flirty.Runtime.Admin;
using Flirty.Tests.Persistence;
using Flirty.Tests.Runtime;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Flirty.Tests.Designer;

/// <summary>
/// Tests for the <see cref="GraphRunAnalyzer"/> – the run state on top of the graph (#104).
/// </summary>
/// <remarks>
/// <para>
/// Played with the <b>real engine</b> (the same handlers the runner calls over the gateways), because
/// that is exactly what checks the core of the derivation: the path is stored nowhere, it arises from
/// the answer sequence. A hand-built <see cref="ResumeDialogResult"/> would only repeat the
/// expectation.
/// </para>
/// <para>
/// The trigger assignment is the one case checked without the engine: it hangs on the
/// <c>DesignerTriggerLog</c>, whose entries can be supplied directly in the test.
/// </para>
/// </remarks>
public sealed class GraphRunAnalyzerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<FlirtyDbContext> _options;

    /// <summary>
    /// Opens a SQLite in-memory connection (which has to stay open, otherwise the database is
    /// discarded) and creates the schema once via <c>EnsureCreated()</c>.
    /// </summary>
    public GraphRunAnalyzerTests()
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

    /// <summary>
    /// The core of the acceptance criterion: visited nodes, the open question and the edges that
    /// actually took effect – and explicitly <b>not</b> the ones that did not.
    /// </summary>
    [Fact]
    public async Task Build_highlights_visited_nodes_the_open_question_and_the_edges_that_took_effect()
    {
        var (sessionId, ids) = SeedLoopSession();
        var detail = LoadDetail(sessionId);

        // Right after the start: the entry question is open, nothing is answered, nothing took effect.
        var started = BuildOverlay(sessionId);
        var entry = Assert.Single(started.Visits);
        Assert.Equal(ids.PositionQuestionId, entry.QuestionId);
        Assert.True(entry.IsCurrent);
        Assert.Empty(entry.Answers);
        Assert.Empty(started.TakenEdges);

        await SubmitAsync(sessionId, ids.PositionQuestionId, "\"Backend\"");

        var overlay = BuildOverlay(sessionId);

        Assert.Equal(ids.MoreQuestionId, overlay.CurrentQuestionId);
        Assert.False(overlay.Visit(ids.PositionQuestionId)!.IsCurrent);
        Assert.True(overlay.Visit(ids.MoreQuestionId)!.IsCurrent);
        Assert.Null(overlay.Visit(ids.SummaryQuestionId));
        Assert.Equal(1, overlay.Steps);

        // The readable value comes from the AnswerValueCodec, the raw value stays next to it.
        var answer = Assert.Single(overlay.Visit(ids.PositionQuestionId)!.Answers);
        Assert.Equal("Backend", answer.Display);
        Assert.Equal("\"Backend\"", answer.Value);

        // The entry question lies inside the loop range, so the engine assigns an iteration index
        // already in the FIRST round – which is why the node carries "Iteration 1" from the start.
        Assert.Equal(0, answer.IterationIndex);

        // Exactly the transition position -> more took effect.
        var taken = Assert.Single(overlay.TakenEdges);
        Assert.Equal(TransitionOf(detail, ids.PositionQuestionId, ids.MoreQuestionId).Id, taken.TransitionId);
        Assert.Equal(1, taken.Count);
        Assert.False(taken.IsAmbiguous);

        // The back jump and the exit did not take effect – the edge shows that, not the graph.
        Assert.Null(overlay.Edge(TransitionOf(detail, ids.MoreQuestionId, ids.PositionQuestionId).Id));
        Assert.Null(overlay.Edge(TransitionOf(detail, ids.MoreQuestionId, ids.SummaryQuestionId).Id));
    }

    /// <summary>
    /// The iteration count on the loop frame: two passes, then the exit. The back jump is an edge
    /// that took effect <b>once</b>, and after the exit the loop is no longer active.
    /// </summary>
    [Fact]
    public async Task Build_counts_the_loop_iterations_and_leaves_it_again()
    {
        var (sessionId, ids) = SeedLoopSession();
        var detail = LoadDetail(sessionId);

        await SubmitAsync(sessionId, ids.PositionQuestionId, "\"Backend\"");
        await SubmitAsync(sessionId, ids.MoreQuestionId, "\"yes\"");
        await SubmitAsync(sessionId, ids.PositionQuestionId, "\"Frontend\"");

        var inLoop = BuildOverlay(sessionId);
        var running = Assert.Single(inLoop.Loops);
        Assert.Equal("positions", running.CollectionKey);
        Assert.Equal(2, running.Iterations);
        Assert.True(running.IsActive);
        Assert.Equal(
            [ids.PositionQuestionId, ids.MoreQuestionId],
            running.Body);

        // The entry question's node carries both answers – one per iteration.
        var visits = inLoop.Visit(ids.PositionQuestionId)!.Answers;
        Assert.Equal(2, visits.Count);
        Assert.Equal([0, 1], visits.Select(answer => answer.IterationIndex));

        var backJump = inLoop.Edge(TransitionOf(detail, ids.MoreQuestionId, ids.PositionQuestionId).Id);
        Assert.Equal(1, backJump!.Count);

        await SubmitAsync(sessionId, ids.MoreQuestionId, "\"no\"");

        var afterExit = BuildOverlay(sessionId);
        var left = Assert.Single(afterExit.Loops);
        Assert.Equal(2, left.Iterations);
        Assert.False(left.IsActive);
        Assert.NotNull(afterExit.Edge(TransitionOf(detail, ids.MoreQuestionId, ids.SummaryQuestionId).Id));
        Assert.Equal(ids.SummaryQuestionId, afterExit.CurrentQuestionId);

        // And the summary – for screen readers the only rendition of the highlighting.
        Assert.Contains("open question summary", afterExit.Summary, StringComparison.Ordinal);
        Assert.Contains("positions: 2 iterations", afterExit.Summary, StringComparison.Ordinal);
    }

    /// <summary>
    /// The acceptance criterion for editing: an edit recomputes the path – <b>even</b> when the new
    /// path takes a different branch. That needs no logic of its own: the path is derived from the
    /// answer sequence, and <c>EditAnswerCommand</c> discards the downstream answers.
    /// </summary>
    [Fact]
    public async Task Build_recomputes_the_path_after_an_edit_and_switches_the_branch()
    {
        var (sessionId, ids) = SeedBranchingSession();
        var detail = LoadDetail(sessionId);
        var toDev = TransitionOf(detail, ids.RoleQuestionId, ids.DevQuestionId);
        var toPm = TransitionOf(detail, ids.RoleQuestionId, ids.PmQuestionId);

        await SubmitAsync(sessionId, ids.RoleQuestionId, "\"dev\"");
        await SubmitAsync(sessionId, ids.DevQuestionId, "\"C#\"");

        var devPath = BuildOverlay(sessionId);
        Assert.NotNull(devPath.Edge(toDev.Id));
        Assert.Null(devPath.Edge(toPm.Id));
        Assert.NotNull(devPath.Visit(ids.DevQuestionId));
        Assert.Equal("Developer", devPath.Visit(ids.RoleQuestionId)!.Answers[0].Display);

        await EditAsync(sessionId, ids.RoleQuestionId, "\"pm\"");

        var pmPath = BuildOverlay(sessionId);

        // The old branch is gone – edge AND node.
        Assert.Null(pmPath.Edge(toDev.Id));
        Assert.Null(pmPath.Visit(ids.DevQuestionId));

        Assert.NotNull(pmPath.Edge(toPm.Id));
        Assert.Equal(ids.PmQuestionId, pmPath.CurrentQuestionId);
        Assert.Equal("Product Manager", pmPath.Visit(ids.RoleQuestionId)!.Answers[0].Display);
        Assert.Equal(1, pmPath.Steps);
    }

    /// <summary>
    /// Several transitions between the same two questions are <b>not</b> distinguishable: the engine
    /// does not record which one took effect. Then all of them are marked and all reported as
    /// ambiguous – claiming one of them would be an invention.
    /// </summary>
    [Fact]
    public async Task Build_reports_parallel_transitions_as_ambiguous()
    {
        // A second transition with the same condition to the same target: at runtime the one with the
        // lower priority wins, but the answer sequence knows only the question pair.
        var (sessionId, ids) = SeedBranchingSession((dialog, graph) => dialog.Transitions.Add(new Transition
        {
            Id = Guid.NewGuid(),
            DialogId = dialog.Id,
            FromQuestionId = graph.RoleQuestionId,
            TargetQuestionId = graph.DevQuestionId,
            Expression = "role == \"dev\"",
            Priority = 2,
        }));

        await SubmitAsync(sessionId, ids.RoleQuestionId, "\"dev\"");

        var overlay = BuildOverlay(sessionId);
        var detail = LoadDetail(sessionId);

        Assert.Equal(2, overlay.TakenEdges.Count);
        Assert.All(overlay.TakenEdges, edge => Assert.True(edge.IsAmbiguous));
        Assert.All(
            overlay.TakenEdges,
            edge =>
            {
                var transition = detail.Transitions.Single(candidate => candidate.Id == edge.TransitionId);
                Assert.Equal(ids.RoleQuestionId, transition.FromQuestionId);
                Assert.Equal(ids.DevQuestionId, transition.TargetQuestionId);
            });

        // The default into the other branch stays unmarked – ambiguous does not mean "everything on".
        Assert.Null(overlay.Edge(TransitionOf(detail, ids.RoleQuestionId, ids.PmQuestionId).Id));
    }

    /// <summary>
    /// The trigger events hang on the question that fired them; those without a question reference
    /// stay dialog-wide – and so does an event for a deleted question, instead of vanishing silently.
    /// <c>freshFrom</c> marks exactly the events of the last step (they flash up once).
    /// </summary>
    [Fact]
    public void Build_assigns_the_triggers_to_the_firing_node_and_marks_the_new_ones()
    {
        var detail = AdminProjection.ToDetail(TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out var ids));
        var state = new ResumeDialogResult(Guid.NewGuid(), SessionStatus.InProgress, null, []);

        DesignerTriggerEntry[] events =
        [
            new(TestDialogFactory.SampleTime, TriggerScope.OnDialogStarted, "DialogStartedNotification",
                ids.PositionQuestionId, "Session gestartet."),
            new(TestDialogFactory.SampleTime, TriggerScope.AfterQuestion, "QuestionAnsweredNotification",
                Guid.NewGuid(), "Transition evaluated."),
            new(TestDialogFactory.SampleTime, TriggerScope.OnDialogCompleted, "DialogCompletedNotification",
                null, "Dialog abgeschlossen."),
        ];

        var overlay = GraphRunAnalyzer.Build(detail, state, events, freshFrom: 2);

        var atNode = Assert.Single(overlay.TriggersOf(ids.PositionQuestionId));
        Assert.Equal("Start", atNode.Label);
        Assert.Contains("OnDialogStarted", atNode.Title, StringComparison.Ordinal);
        Assert.False(atNode.IsFresh);

        // The event for the deleted question loses its place, not its visibility.
        Assert.Equal(2, overlay.DialogTriggers.Count);
        Assert.True(overlay.Triggers[^1].IsFresh);
        Assert.False(overlay.Triggers[1].IsFresh);
    }

    /// <summary>
    /// Before the first run the state is empty but valid – which is why canvas and inspector know no
    /// <see langword="null"/> case.
    /// </summary>
    [Fact]
    public void NotStarted_returns_an_empty_but_valid_state()
    {
        var overlay = GraphRunAnalyzer.NotStarted();

        Assert.Null(overlay.CurrentQuestionId);
        Assert.Empty(overlay.Visits);
        Assert.Empty(overlay.TakenEdges);
        Assert.Empty(overlay.Loops);
        Assert.Empty(overlay.Triggers);
        Assert.Equal(0, overlay.Steps);
        Assert.Contains("not started yet", overlay.Summary, StringComparison.Ordinal);
    }

    // ---- Setup ---------------------------------------------------------------------------------------

    private FlirtyDbContext CreateContext() => new(_options);

    /// <summary>Builds the run state from the same state the runner reads.</summary>
    /// <param name="sessionId">The running session.</param>
    /// <returns>The run state.</returns>
    private GraphRunOverlay BuildOverlay(Guid sessionId)
        => GraphRunAnalyzer.Build(LoadDetail(sessionId), LoadState(sessionId), []);

    /// <summary>The transition between two questions – unambiguous in the test graph.</summary>
    private static TransitionDetail TransitionOf(DialogDetail detail, Guid from, Guid target)
        => detail.Transitions.Single(
            transition => transition.FromQuestionId == from && transition.TargetQuestionId == target);

    /// <summary>Reads the session state over the same query the runner uses.</summary>
    private ResumeDialogResult LoadState(Guid sessionId)
    {
        using var context = CreateContext();
        return new ResumeDialogQueryHandler(new DialogStore(context))
            .Handle(new ResumeDialogQuery(sessionId), default)
            .AsTask()
            .GetAwaiter()
            .GetResult();
    }

    /// <summary>Reads the dialog graph in the same navigation-free view the runner uses.</summary>
    private DialogDetail LoadDetail(Guid sessionId)
    {
        using var context = CreateContext();
        var dialogId = context.DialogSessions.Single(session => session.Id == sessionId).DialogId;

        return AdminProjection.ToDetail(context.Dialogs
            .Include(dialog => dialog.Questions).ThenInclude(question => question.Options)
            .Include(dialog => dialog.Transitions)
            .Include(dialog => dialog.Loops)
            .Include(dialog => dialog.Triggers)
            .Single(dialog => dialog.Id == dialogId));
    }

    /// <summary>Creates the loop dialog together with a running session on the entry question.</summary>
    private (Guid SessionId, LoopDialogIds Ids) SeedLoopSession()
    {
        var dialogId = Guid.NewGuid();
        var dialog = TestDialogFactory.BuildLoopDialog(dialogId, out var ids);

        return (Seed(dialog, ids.PositionQuestionId), ids);
    }

    /// <summary>
    /// Creates the branching dialog together with a running session. Via <paramref name="arrange"/>
    /// the graph can be extended beforehand (e.g. with a second, parallel transition).
    /// </summary>
    private (Guid SessionId, BranchingDialogIds Ids) SeedBranchingSession(
        Action<Dialog, BranchingDialogIds>? arrange = null)
    {
        var dialogId = Guid.NewGuid();
        var dialog = TestDialogFactory.BuildBranchingDialog(dialogId, out var ids);
        arrange?.Invoke(dialog, ids);

        return (Seed(dialog, ids.RoleQuestionId), ids);
    }

    private Guid Seed(Dialog dialog, Guid currentQuestionId)
    {
        var sessionId = Guid.NewGuid();

        using var context = CreateContext();
        context.Dialogs.Add(dialog);
        context.DialogSessions.Add(new DialogSession
        {
            Id = sessionId,
            DialogId = dialog.Id,
            DialogVersion = dialog.Version,
            ExternalUserKey = "designer-test-1",
            Status = SessionStatus.InProgress,
            CurrentQuestionId = currentQuestionId,
            StartedAt = TestDialogFactory.SampleTime,
        });
        context.SaveChanges();

        return sessionId;
    }

    /// <summary>Submits an answer over the real handler in its own context.</summary>
    private async Task SubmitAsync(Guid sessionId, Guid questionId, string value)
    {
        using var context = CreateContext();
        var handler = new SubmitAnswerCommandHandler(
            new DialogStore(context), new DynamicExpressoExpressionEvaluator(), new SpyPublisher());

        _ = await handler.Handle(new SubmitAnswerCommand(sessionId, questionId, value), default);
    }

    /// <summary>Overwrites an answer over the real handler in its own context.</summary>
    private async Task EditAsync(Guid sessionId, Guid questionId, string value)
    {
        using var context = CreateContext();
        var handler = new EditAnswerCommandHandler(
            new DialogStore(context), new DynamicExpressoExpressionEvaluator(), new SpyPublisher());

        _ = await handler.Handle(new EditAnswerCommand(sessionId, questionId, value), default);
    }
}
