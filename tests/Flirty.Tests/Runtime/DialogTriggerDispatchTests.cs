using Flirty.Domain;
using Flirty.Persistence;
using Flirty.Runtime;
using Flirty.Tests.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Flirty.Tests.Runtime;

/// <summary>
/// End-to-end proof for the triggers configured on the dialog (#42): a
/// <see cref="TriggerDefinition"/> with <see cref="TriggerKind.Webhook"/> created over the
/// designer/the admin API really is delivered while a dialog is played through. Unlike
/// <see cref="WebhookNotificationHandlerTests"/> (handler in isolation), the whole path runs here:
/// <see cref="IFlirtyEngine"/> -> command handler -> <c>IPublisher</c> -> webhook handler ->
/// <see cref="IDialogStore"/> (real SQLite database) -> HTTP.
/// </summary>
public sealed class DialogTriggerDispatchTests : IDisposable
{
    private const string TargetUrl = "https://example.test/completed";

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<FlirtyDbContext> _options;

    /// <summary>Opens a SQLite in-memory connection and creates the schema once.</summary>
    public DialogTriggerDispatchTests()
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

    /// <summary>
    /// A webhook trigger on <see cref="TriggerScope.OnDialogCompleted"/> is delivered when the dialog
    /// completes – without any <c>o.AddWebhook(...)</c> registration in code.
    /// </summary>
    [Fact]
    public async Task A_configured_trigger_is_delivered_on_completion()
    {
        var spy = await RunBranchingDialogAsync(
            Trigger(TriggerScope.OnDialogCompleted, $"{{\"url\":\"{TargetUrl}\",\"name\":\"fertig\"}}"));

        var request = Assert.Single(spy.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(TargetUrl, request.Url?.ToString());
        Assert.Equal("OnDialogCompleted", request.Event);
        Assert.Equal("fertig", request.Trigger);
        Assert.Contains("branching", request.Body);
    }

    /// <summary>A matching condition is evaluated against the session's real answers.</summary>
    [Fact]
    public async Task A_configured_trigger_with_a_matching_condition_is_delivered()
    {
        var spy = await RunBranchingDialogAsync(
            Trigger(TriggerScope.OnDialogCompleted, $"{{\"url\":\"{TargetUrl}\"}}", "role == \"dev\""));

        Assert.Single(spy.Requests);
    }

    /// <summary>If the condition does not match, nothing is delivered.</summary>
    [Fact]
    public async Task A_configured_trigger_with_a_non_matching_condition_stays_silent()
    {
        var spy = await RunBranchingDialogAsync(
            Trigger(TriggerScope.OnDialogCompleted, $"{{\"url\":\"{TargetUrl}\"}}", "role == \"pm\""));

        Assert.Empty(spy.Requests);
    }

    /// <summary>
    /// An <see cref="TriggerScope.AfterQuestion"/> trigger fires only after its own question – the
    /// second, completing answer does not set it off.
    /// </summary>
    [Fact]
    public async Task AfterQuestion_trigger_fires_only_after_its_own_question()
    {
        var dialogId = Guid.NewGuid();
        BranchingDialogIds ids;
        using (var seed = new FlirtyDbContext(_options))
        {
            var dialog = TestDialogFactory.BuildBranchingDialog(dialogId, out ids);
            dialog.Triggers.Add(Trigger(
                TriggerScope.AfterQuestion, $"{{\"url\":\"{TargetUrl}\"}}", questionId: ids.RoleQuestionId));
            seed.Dialogs.Add(dialog);
            seed.SaveChanges();
        }

        var spy = await PlayThroughAsync();

        // Two answers (role, devDetail), but only the first matches the trigger's question reference.
        Assert.Single(spy.Requests);
        Assert.Equal("AfterQuestion", Assert.Single(spy.Requests).Event);
    }

    /// <summary>
    /// Seeds the branching dialog together with <paramref name="trigger"/> and plays it through to
    /// completion.
    /// </summary>
    /// <param name="trigger">The trigger definition to seed.</param>
    /// <returns>The HTTP spy with the recorded deliveries.</returns>
    private async Task<RecordingHttpMessageHandler> RunBranchingDialogAsync(TriggerDefinition trigger)
    {
        var dialogId = Guid.NewGuid();
        using (var seed = new FlirtyDbContext(_options))
        {
            var dialog = TestDialogFactory.BuildBranchingDialog(dialogId, out _);
            dialog.Triggers.Add(trigger);
            seed.Dialogs.Add(dialog);
            seed.SaveChanges();
        }

        return await PlayThroughAsync();
    }

    /// <summary>
    /// Plays the previously seeded branching dialog through to completion over the facade
    /// (<c>role = "dev"</c> -> <c>devDetail</c> -> completion).
    /// </summary>
    /// <returns>The HTTP spy with the recorded deliveries.</returns>
    private async Task<RecordingHttpMessageHandler> PlayThroughAsync()
    {
        var spy = new RecordingHttpMessageHandler();

        var services = new ServiceCollection()
            .AddLogging()
            .AddFlirty();
        services.AddDbContext<FlirtyDbContext>(options => options.UseSqlite(_connection));

        // Configure the same named client the engine uses once more: the primary handler set last
        // wins -> the delivery lands in the spy instead of on the network.
        services
            .AddHttpClient(WebhookNotificationHandler.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => spy);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<IFlirtyEngine>();

        var start = await engine.StartDialogAsync("branching", "user-1");
        var next = await engine.SubmitAnswerAsync(start.SessionId, start.CurrentQuestion.Id, "\"dev\"");
        var final = await engine.SubmitAnswerAsync(start.SessionId, next.NextQuestion!.Id, "\"csharp\"");

        Assert.True(final.IsCompleted);
        return spy;
    }

    private static TriggerDefinition Trigger(
        TriggerScope scope, string config, string? expression = null, Guid? questionId = null) => new()
    {
        Id = Guid.NewGuid(),
        Scope = scope,
        QuestionId = questionId,
        Kind = TriggerKind.Webhook,
        Config = config,
        Expression = expression,
    };
}
