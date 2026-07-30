using Flirty.Domain;
using Flirty.Persistence;
using Flirty.Pipeline;
using Flirty.Runtime;
using Flirty.Tests.Persistence;
using Flirty.Validation;
using Mediator;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Flirty.Tests.Validation;

/// <summary>
/// Verifies the <c>AnswerValidationPipelineBehavior</c> (issue #30) end-to-end through the full
/// mediator pipeline via <see cref="IFlirtyEngine"/> against a real SQLite database: an invalid answer
/// is rejected <b>before</b> the handler with an <see cref="AnswerValidationException"/> (without
/// persistence or invalidation), valid answers pass through unchanged, and the DI registration is
/// correct.
/// </summary>
public sealed class AnswerValidationPipelineBehaviorTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<FlirtyDbContext> _options;

    /// <summary>Opens a SQLite in-memory connection that is kept open and creates the schema.</summary>
    public AnswerValidationPipelineBehaviorTests()
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

    private Guid SeedBranchingDialog()
    {
        var dialogId = Guid.NewGuid();
        using var seed = new FlirtyDbContext(_options);
        seed.Dialogs.Add(TestDialogFactory.BuildBranchingDialog(dialogId, out _));
        seed.SaveChanges();
        return dialogId;
    }

    /// <summary>
    /// A choice that violates the type (no known option value) is rejected before the handler with an
    /// <see cref="AnswerValidationException"/> – <b>no</b> answer is persisted.
    /// </summary>
    [Fact]
    public async Task SubmitAnswerAsync_an_invalid_choice_throws_and_persists_nothing()
    {
        SeedBranchingDialog();

        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<IFlirtyEngine>();

        var start = await engine.StartDialogAsync("branching", "user-1");

        var exception = await Assert.ThrowsAsync<AnswerValidationException>(
            async () => await engine.SubmitAnswerAsync(start.SessionId, start.CurrentQuestion.Id, "\"lead\""));
        Assert.Equal(start.CurrentQuestion.Id, exception.QuestionId);

        using var assert = new FlirtyDbContext(_options);
        var session = assert.DialogSessions.Include(s => s.Answers).Single(s => s.Id == start.SessionId);
        Assert.Empty(session.Answers);
        Assert.Equal(SessionStatus.InProgress, session.Status);
        Assert.Equal(start.CurrentQuestion.Id, session.CurrentQuestionId);
    }

    /// <summary>A valid choice passes through the pipeline unchanged (branching takes effect).</summary>
    [Fact]
    public async Task SubmitAnswerAsync_a_valid_choice_passes_through()
    {
        SeedBranchingDialog();

        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<IFlirtyEngine>();

        var start = await engine.StartDialogAsync("branching", "user-1");
        var result = await engine.SubmitAnswerAsync(start.SessionId, start.CurrentQuestion.Id, "\"dev\"");

        Assert.False(result.IsCompleted);
        Assert.NotNull(result.NextQuestion);
        Assert.Equal("devDetail", result.NextQuestion.Key);
    }

    /// <summary>
    /// An invalid edit value is rejected <b>before</b> the handler invalidates downstream answers or
    /// recomputes the path – the completed session stays unchanged.
    /// </summary>
    [Fact]
    public async Task EditAnswerAsync_an_invalid_value_throws_and_invalidates_nothing()
    {
        SeedBranchingDialog();

        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<IFlirtyEngine>();

        // Walk the dev branch completely (role -> devDetail -> completion, two answers).
        var start = await engine.StartDialogAsync("branching", "user-1");
        var afterRole = await engine.SubmitAnswerAsync(start.SessionId, start.CurrentQuestion.Id, "\"dev\"");
        await engine.SubmitAnswerAsync(start.SessionId, afterRole.NextQuestion!.Id, "\"C#\"");

        await Assert.ThrowsAsync<AnswerValidationException>(
            async () => await engine.EditAnswerAsync(start.SessionId, start.CurrentQuestion.Id, "\"lead\""));

        using var assert = new FlirtyDbContext(_options);
        var session = assert.DialogSessions.Include(s => s.Answers).Single(s => s.Id == start.SessionId);
        Assert.Equal(2, session.Answers.Count);
        Assert.Equal(SessionStatus.Completed, session.Status);
    }

    /// <summary>
    /// <c>AddFlirty()</c> registers the <see cref="IAnswerValidator"/> and the closed
    /// <c>AnswerValidationPipelineBehavior</c> for <see cref="SubmitAnswerCommand"/>.
    /// </summary>
    [Fact]
    public void AddFlirty_registers_the_validator_and_the_behavior()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        Assert.IsType<AnswerValidator>(scope.ServiceProvider.GetRequiredService<IAnswerValidator>());

        var behaviors = scope.ServiceProvider
            .GetServices<IPipelineBehavior<SubmitAnswerCommand, SubmitAnswerResult>>();
        Assert.Contains(
            behaviors,
            behavior => behavior is AnswerValidationPipelineBehavior<SubmitAnswerCommand, SubmitAnswerResult>);
    }
}
