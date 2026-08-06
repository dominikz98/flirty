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

    // ---- Json and custom question types (#136) -----------------------------------------------

    /// <summary>
    /// Malformed JSON is a <b>value</b> error, so it must arrive as an
    /// <see cref="AnswerValidationException"/> (HTTP 400) rather than as the
    /// <see cref="InvalidOperationException"/> a misconfiguration produces (HTTP 409). The distinction
    /// is decided here, at the layer that turns a result into an exception.
    /// </summary>
    [Fact]
    public async Task SubmitAnswerAsync_malformed_json_is_a_validation_error_not_a_misconfiguration()
    {
        SeedJsonDialog();

        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<IFlirtyEngine>();

        var start = await engine.StartDialogAsync("json-dialog", "user-1");

        await Assert.ThrowsAsync<AnswerValidationException>(
            async () => await engine.SubmitAnswerAsync(
                start.SessionId, start.CurrentQuestion.Id, "#ff0000"));

        using var assert = new FlirtyDbContext(_options);
        Assert.Empty(assert.DialogSessions.Include(s => s.Answers)
            .Single(s => s.Id == start.SessionId).Answers);
    }

    /// <summary>
    /// The full path with a host-declared type: the decorator runs inside the request scope, the host
    /// validator refuses the value, and nothing is persisted. Without a declaration the same answer
    /// would pass, which is what the second half asserts.
    /// </summary>
    [Fact]
    public async Task SubmitAnswerAsync_runs_the_host_validator_of_a_custom_question_type()
    {
        SeedJsonDialog();

        using var provider = new ServiceCollection()
            .AddLogging()
            .AddFlirty(options => options.AddQuestionType<HexColourValidator>("color", "Colour picker"))
            .AddDbContext<FlirtyDbContext>(options => options.UseSqlite(_connection))
            .BuildServiceProvider();

        using var scope = provider.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<IFlirtyEngine>();

        var start = await engine.StartDialogAsync("json-dialog", "user-1");

        // Well-formed JSON, so the built-in check passes - only the host validator can refuse it.
        var exception = await Assert.ThrowsAsync<AnswerValidationException>(
            async () => await engine.SubmitAnswerAsync(
                start.SessionId, start.CurrentQuestion.Id, "\"not-a-colour\""));
        Assert.Contains("#rrggbb", Assert.Single(exception.Errors), StringComparison.Ordinal);

        await engine.SubmitAnswerAsync(start.SessionId, start.CurrentQuestion.Id, "\"#ff0000\"");

        using var assert = new FlirtyDbContext(_options);
        var answer = Assert.Single(assert.DialogSessions.Include(s => s.Answers)
            .Single(s => s.Id == start.SessionId).Answers);
        Assert.Equal("\"#ff0000\"", answer.Value);
    }

    /// <summary>
    /// The degradation path end-to-end: a second consumer of the same database that never declared the
    /// type reads the same question and validates it as plain JSON. No throw, no 500 – that is what
    /// makes a published dialog (ADR 0005) survive a host that dropped a registration.
    /// </summary>
    [Fact]
    public async Task SubmitAnswerAsync_without_the_declaration_falls_back_to_the_json_check()
    {
        SeedJsonDialog();

        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<IFlirtyEngine>();

        var start = await engine.StartDialogAsync("json-dialog", "user-1");

        // Refused by the host validator above; accepted here, because nothing declares "color".
        await engine.SubmitAnswerAsync(start.SessionId, start.CurrentQuestion.Id, "\"not-a-colour\"");

        using var assert = new FlirtyDbContext(_options);
        Assert.Single(assert.DialogSessions.Include(s => s.Answers)
            .Single(s => s.Id == start.SessionId).Answers);
    }

    /// <summary>Test double: the worked example of the guides, in miniature.</summary>
    private sealed class HexColourValidator : IQuestionTypeValidator
    {
        public AnswerValidationResult Validate(Question question, string value)
        {
            var text = value.Trim('"');
            return text.Length == 7 && text[0] == '#'
                && text[1..].All(Uri.IsHexDigit)
                    ? AnswerValidationResult.Valid
                    : AnswerValidationResult.Invalid(
                        $"The value '{text}' is not a colour in the form #rrggbb.");
        }
    }

    private Guid SeedJsonDialog()
    {
        var dialogId = Guid.NewGuid();
        var questionId = Guid.NewGuid();

        using var seed = new FlirtyDbContext(_options);
        seed.Dialogs.Add(new Dialog
        {
            Id = dialogId,
            Key = "json-dialog",
            Name = "JSON dialog",
            Version = 1,
            IsPublished = true,
            StartQuestionId = questionId,
            CreatedAt = DateTimeOffset.UnixEpoch,
            UpdatedAt = DateTimeOffset.UnixEpoch,
            Questions =
            {
                new Question
                {
                    Id = questionId,
                    DialogId = dialogId,
                    Key = "colour",
                    Text = "Which colour?",
                    Type = QuestionType.Json,
                    Order = 0,
                    IsRequired = true,
                    CustomTypeKey = "color",
                },
            },
        });
        seed.SaveChanges();
        return dialogId;
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
