using Flirty.Domain;
using Flirty.Persistence;
using Flirty.Runtime;
using Flirty.Tests.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Flirty.Tests.Runtime;

/// <summary>
/// End-to-End-Nachweis für die am Dialog konfigurierten Trigger (#42): ein über den Designer/die
/// Admin-API angelegter <see cref="TriggerDefinition"/> mit <see cref="TriggerKind.Webhook"/> wird beim
/// Durchlaufen eines Dialogs tatsächlich zugestellt. Anders als
/// <see cref="WebhookNotificationHandlerTests"/> (Handler isoliert) läuft hier der ganze Weg:
/// <see cref="IFlirtyEngine"/> → Command-Handler → <c>IPublisher</c> → Webhook-Handler →
/// <see cref="IDialogStore"/> (echte SQLite-Datenbank) → HTTP.
/// </summary>
public sealed class DialogTriggerDispatchTests : IDisposable
{
    private const string TargetUrl = "https://example.test/completed";

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<FlirtyDbContext> _options;

    /// <summary>Öffnet eine SQLite-in-memory-Verbindung und erzeugt das Schema einmalig.</summary>
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

    /// <summary>Schließt die Verbindung und verwirft damit die in-memory-Datenbank.</summary>
    public void Dispose() => _connection.Dispose();

    /// <summary>
    /// Ein Webhook-Trigger auf <see cref="TriggerScope.OnDialogCompleted"/> wird beim Abschluss des
    /// Dialogs zugestellt – ohne jede <c>o.AddWebhook(...)</c>-Registrierung im Code.
    /// </summary>
    [Fact]
    public async Task Konfigurierter_Trigger_wird_beim_Abschluss_zugestellt()
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

    /// <summary>Eine zutreffende Bedingung wird gegen die echten Antworten der Session ausgewertet.</summary>
    [Fact]
    public async Task Konfigurierter_Trigger_mit_zutreffender_Bedingung_wird_zugestellt()
    {
        var spy = await RunBranchingDialogAsync(
            Trigger(TriggerScope.OnDialogCompleted, $"{{\"url\":\"{TargetUrl}\"}}", "role == \"dev\""));

        Assert.Single(spy.Requests);
    }

    /// <summary>Trifft die Bedingung nicht zu, wird nichts zugestellt.</summary>
    [Fact]
    public async Task Konfigurierter_Trigger_mit_nicht_zutreffender_Bedingung_bleibt_stumm()
    {
        var spy = await RunBranchingDialogAsync(
            Trigger(TriggerScope.OnDialogCompleted, $"{{\"url\":\"{TargetUrl}\"}}", "role == \"pm\""));

        Assert.Empty(spy.Requests);
    }

    /// <summary>
    /// Ein <see cref="TriggerScope.AfterQuestion"/>-Trigger feuert nur nach seiner Frage – die zweite,
    /// abschließende Antwort löst ihn nicht aus.
    /// </summary>
    [Fact]
    public async Task AfterQuestion_Trigger_feuert_nur_nach_seiner_Frage()
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

        // Zwei Antworten (role, devDetail), aber nur die erste passt zum Frage-Bezug des Triggers.
        Assert.Single(spy.Requests);
        Assert.Equal("AfterQuestion", Assert.Single(spy.Requests).Event);
    }

    /// <summary>
    /// Seedet den Branching-Dialog samt <paramref name="trigger"/> und spielt ihn bis zum Abschluss durch.
    /// </summary>
    /// <param name="trigger">Die zu seedende Trigger-Definition.</param>
    /// <returns>Der HTTP-Spy mit den aufgezeichneten Zustellungen.</returns>
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
    /// Spielt den zuvor geseedeten Branching-Dialog über die Facade bis zum Abschluss durch
    /// (<c>role = "dev"</c> → <c>devDetail</c> → Abschluss).
    /// </summary>
    /// <returns>Der HTTP-Spy mit den aufgezeichneten Zustellungen.</returns>
    private async Task<RecordingHttpMessageHandler> PlayThroughAsync()
    {
        var spy = new RecordingHttpMessageHandler();

        var services = new ServiceCollection()
            .AddLogging()
            .AddFlirty();
        services.AddDbContext<FlirtyDbContext>(options => options.UseSqlite(_connection));

        // Denselben Named-Client wie die Engine erneut konfigurieren: der zuletzt gesetzte
        // Primary-Handler gewinnt -> die Zustellung landet im Spy statt im Netz.
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
