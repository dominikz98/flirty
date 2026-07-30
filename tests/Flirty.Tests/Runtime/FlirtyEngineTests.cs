using System.ComponentModel.DataAnnotations;
using Flirty.Domain;
using Flirty.Expressions;
using Flirty.Persistence;
using Flirty.Runtime;
using Flirty.Tests.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Flirty.Tests.Runtime;

/// <summary>
/// Verifies the public facade <see cref="IFlirtyEngine"/> (issue #25) end-to-end through the mediator
/// pipeline: DI registration, starting a dialog over the facade (facade -> <c>ISender</c> -> handler
/// -> <see cref="IDialogStore"/> -> EF Core) and the declarative command validation.
/// </summary>
public sealed class FlirtyEngineTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<FlirtyDbContext> _options;

    /// <summary>
    /// Opens a SQLite in-memory connection (which has to stay open, otherwise the database is
    /// discarded) and creates the schema once via <c>EnsureCreated()</c>.
    /// </summary>
    public FlirtyEngineTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<FlirtyDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = new FlirtyDbContext(_options);
        context.Database.EnsureCreated();
    }

    /// <summary>Closes the connection and thereby discards the in-memory database.</summary>
    public void Dispose() => _connection.Dispose();

    private ServiceProvider BuildProvider()
        => new ServiceCollection()
            .AddLogging()
            .AddFlirty()
            .AddDbContext<FlirtyDbContext>(options => options.UseSqlite(_connection))
            .BuildServiceProvider();

    /// <summary>The facade starts a published dialog and returns the session plus the first question.</summary>
    [Fact]
    public async Task StartDialogAsync_starts_dialog_via_facade()
    {
        var dialogId = Guid.NewGuid();
        Guid questionId;
        using (var seed = new FlirtyDbContext(_options))
        {
            seed.Dialogs.Add(TestDialogFactory.BuildFullDialog(dialogId, out questionId));
            seed.SaveChanges();
        }

        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<IFlirtyEngine>();

        var result = await engine.StartDialogAsync("onboarding", "user-1");

        Assert.False(result.IsResumed);
        Assert.NotEqual(Guid.Empty, result.SessionId);
        Assert.Equal(questionId, result.CurrentQuestion.Id);
        Assert.Equal(2, result.CurrentQuestion.Options.Count);
    }

    /// <summary>An empty <c>DialogKey</c> is rejected by the <c>ValidationPipelineBehavior</c>.</summary>
    [Fact]
    public async Task StartDialogAsync_an_empty_DialogKey_is_rejected_by_the_pipeline()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<IFlirtyEngine>();

        await Assert.ThrowsAsync<ValidationException>(
            async () => await engine.StartDialogAsync(string.Empty, "user-1"));
    }

    /// <summary>The facade submits an answer and returns the next question via branching.</summary>
    [Fact]
    public async Task SubmitAnswerAsync_submits_the_answer_and_returns_the_next_question()
    {
        var dialogId = Guid.NewGuid();
        BranchingDialogIds ids;
        using (var seed = new FlirtyDbContext(_options))
        {
            seed.Dialogs.Add(TestDialogFactory.BuildBranchingDialog(dialogId, out ids));
            seed.SaveChanges();
        }

        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<IFlirtyEngine>();

        var start = await engine.StartDialogAsync("branching", "user-1");
        var result = await engine.SubmitAnswerAsync(start.SessionId, start.CurrentQuestion.Id, "\"dev\"");

        Assert.False(result.IsCompleted);
        Assert.NotNull(result.NextQuestion);
        Assert.Equal(ids.DevQuestionId, result.NextQuestion.Id);
    }

    /// <summary>A <c>null</c> answer value is rejected by the <c>ValidationPipelineBehavior</c>.</summary>
    [Fact]
    public async Task SubmitAnswerAsync_a_null_value_is_rejected_by_the_pipeline()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<IFlirtyEngine>();

        await Assert.ThrowsAsync<ValidationException>(
            async () => await engine.SubmitAnswerAsync(Guid.NewGuid(), Guid.NewGuid(), null!));
    }

    /// <summary>
    /// After a start and one submitted answer the facade reads the session state: status, the now
    /// current question and the answer given so far.
    /// </summary>
    [Fact]
    public async Task ResumeDialogAsync_returns_the_state_and_the_previous_answers()
    {
        var dialogId = Guid.NewGuid();
        BranchingDialogIds ids;
        using (var seed = new FlirtyDbContext(_options))
        {
            seed.Dialogs.Add(TestDialogFactory.BuildBranchingDialog(dialogId, out ids));
            seed.SaveChanges();
        }

        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<IFlirtyEngine>();

        var start = await engine.StartDialogAsync("branching", "user-1");
        await engine.SubmitAnswerAsync(start.SessionId, start.CurrentQuestion.Id, "\"dev\"");

        var result = await engine.ResumeDialogAsync(start.SessionId);

        Assert.Equal(start.SessionId, result.SessionId);
        Assert.Equal(SessionStatus.InProgress, result.Status);
        Assert.NotNull(result.CurrentQuestion);
        Assert.Equal(ids.DevQuestionId, result.CurrentQuestion.Id);

        var answer = Assert.Single(result.Answers);
        Assert.Equal("role", answer.QuestionKey);
        Assert.Equal("\"dev\"", answer.Value);
    }

    /// <summary>An unknown session makes <c>ResumeDialogAsync</c> fail with <see cref="SessionNotFoundException"/>.</summary>
    [Fact]
    public async Task ResumeDialogAsync_an_unknown_session_throws_SessionNotFoundException()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<IFlirtyEngine>();

        await Assert.ThrowsAsync<SessionNotFoundException>(
            async () => await engine.ResumeDialogAsync(Guid.NewGuid()));
    }

    /// <summary>
    /// The facade edits an earlier answer of an already completed session, recomputes the path (dev
    /// branch -> pm branch), reopens the session and reports the discarded downstream answer.
    /// </summary>
    [Fact]
    public async Task EditAnswerAsync_overwrites_and_recomputes_the_path()
    {
        var dialogId = Guid.NewGuid();
        BranchingDialogIds ids;
        using (var seed = new FlirtyDbContext(_options))
        {
            seed.Dialogs.Add(TestDialogFactory.BuildBranchingDialog(dialogId, out ids));
            seed.SaveChanges();
        }

        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<IFlirtyEngine>();

        // Walk the dev branch completely (role -> devDetail -> completion).
        var start = await engine.StartDialogAsync("branching", "user-1");
        var afterRole = await engine.SubmitAnswerAsync(start.SessionId, start.CurrentQuestion.Id, "\"dev\"");
        await engine.SubmitAnswerAsync(start.SessionId, afterRole.NextQuestion!.Id, "\"C#\"");

        var result = await engine.EditAnswerAsync(start.SessionId, start.CurrentQuestion.Id, "\"pm\"");

        Assert.False(result.IsCompleted);
        Assert.NotNull(result.NextQuestion);
        Assert.Equal(ids.PmQuestionId, result.NextQuestion.Id);
        Assert.Equal(1, result.InvalidatedAnswers);
    }

    /// <summary>
    /// The facade walks a loop across several iterations (loop back on <c>more == "yes"</c>, exit on
    /// <c>"no"</c>), makes the iterations visible over <c>ResumeDialogAsync</c> and then edits exactly
    /// the first iteration (issue #29).
    /// </summary>
    [Fact]
    public async Task LoopRuntime_walks_the_iterations_and_edits_one_specifically_over_the_facade()
    {
        var dialogId = Guid.NewGuid();
        LoopDialogIds ids;
        using (var seed = new FlirtyDbContext(_options))
        {
            seed.Dialogs.Add(TestDialogFactory.BuildLoopDialog(dialogId, out ids));
            seed.SaveChanges();
        }

        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<IFlirtyEngine>();

        var start = await engine.StartDialogAsync("loop", "user-1");
        await engine.SubmitAnswerAsync(start.SessionId, ids.PositionQuestionId, "\"A\"");
        var afterFirstMore = await engine.SubmitAnswerAsync(start.SessionId, ids.MoreQuestionId, "\"yes\"");
        Assert.Equal(ids.PositionQuestionId, afterFirstMore.NextQuestion!.Id);   // Loop-Back
        await engine.SubmitAnswerAsync(start.SessionId, ids.PositionQuestionId, "\"B\"");
        var afterSecondMore = await engine.SubmitAnswerAsync(start.SessionId, ids.MoreQuestionId, "\"no\"");
        Assert.Equal(ids.SummaryQuestionId, afterSecondMore.NextQuestion!.Id);   // Exit

        var state = await engine.ResumeDialogAsync(start.SessionId);
        var positionAnswers = state.Answers
            .Where(answer => answer.QuestionKey == "position")
            .OrderBy(answer => answer.IterationIndex)
            .ToList();
        Assert.Equal([0, 1], positionAnswers.Select(answer => answer.IterationIndex));

        var edited = await engine.EditAnswerAsync(start.SessionId, ids.PositionQuestionId, "\"A2\"", iterationIndex: 0);
        Assert.True(edited.InvalidatedAnswers > 0);
    }

    /// <summary>A <c>null</c> answer value is rejected by the pipeline on <c>EditAnswerAsync</c> too.</summary>
    [Fact]
    public async Task EditAnswerAsync_a_null_value_is_rejected_by_the_pipeline()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<IFlirtyEngine>();

        await Assert.ThrowsAsync<ValidationException>(
            async () => await engine.EditAnswerAsync(Guid.NewGuid(), Guid.NewGuid(), null!));
    }

    /// <summary><c>AddFlirty()</c> registers <see cref="IFlirtyEngine"/> as <see cref="FlirtyEngine"/>.</summary>
    [Fact]
    public void AddFlirty_registers_IFlirtyEngine()
    {
        using var provider = new ServiceCollection()
            .AddFlirty()
            .AddDbContext<FlirtyDbContext>(options => options.UseSqlite(_connection))
            .BuildServiceProvider();

        using var scope = provider.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<IFlirtyEngine>();

        Assert.IsType<FlirtyEngine>(engine);
    }

    /// <summary><c>AddFlirty()</c> registers the default <see cref="IExpressionEvaluator"/> (#26).</summary>
    [Fact]
    public void AddFlirty_registers_IExpressionEvaluator()
    {
        using var provider = new ServiceCollection()
            .AddFlirty()
            .AddDbContext<FlirtyDbContext>(options => options.UseSqlite(_connection))
            .BuildServiceProvider();

        var evaluator = provider.GetRequiredService<IExpressionEvaluator>();

        Assert.IsType<DynamicExpressoExpressionEvaluator>(evaluator);
    }
}
