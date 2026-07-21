using System.Net;
using System.Net.Http.Json;
using Flirty.AspNetCore.Dtos;
using Flirty.Domain;
using Flirty.Persistence;
using Flirty.Samples.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Flirty.Tests.Samples;

/// <summary>
/// Prüft die Web-Sample (#45) end-to-end über einen In-Process-<see cref="TestServer"/>: die echte
/// Sample-Komposition (<see cref="WebSampleApp"/>) wird gehostet, der Demo-Dialog über die Admin-CRUD-API
/// aufgebaut und anschließend über die Laufzeit-Endpunkte durchgespielt. Abgedeckt: Branching, Loop über
/// Liste, Resume, Edit, In-Process-Handler-Auslösung und der Inbound-Webhook-Empfänger. Der volle
/// Outbound→Inbound-Webhook-Rundlauf ist bewusst dem Playwright-E2E vorbehalten (braucht echtes Kestrel).
/// </summary>
public sealed class WebSampleTests
{
    [Fact]
    public async Task Branching_dev_Zweig_Loop_und_Abschluss_loesen_InProcess_Handler_aus()
    {
        await using var host = await WebSampleTestHost.StartAsync();
        var client = host.Client;

        var start = await StartAsync(client, "dev-user");
        Assert.False(start.IsResumed);
        Assert.Equal(DemoDialog.RoleKey, start.CurrentQuestion.Key);

        // Branching: role == "dev" -> language (nicht product).
        var afterRole = await SubmitAsync(client, start.SessionId, start.CurrentQuestion.Id, "\"dev\"");
        Assert.Equal(DemoDialog.LanguageKey, afterRole.NextQuestion!.Key);

        var afterLanguage = await SubmitAsync(client, start.SessionId, afterRole.NextQuestion.Id, "\"C#\"");
        Assert.Equal(DemoDialog.SkillKey, afterLanguage.NextQuestion!.Key);

        // Loop über Liste: skill (Iteration 0) -> more=yes (Loop-Back) -> skill (Iteration 1) -> more=no (Exit).
        var afterSkill0 = await SubmitAsync(client, start.SessionId, afterLanguage.NextQuestion.Id, "\"EF Core\"");
        Assert.Equal(DemoDialog.MoreKey, afterSkill0.NextQuestion!.Key);
        var afterMoreYes = await SubmitAsync(client, start.SessionId, afterSkill0.NextQuestion.Id, "\"yes\"");
        Assert.Equal(DemoDialog.SkillKey, afterMoreYes.NextQuestion!.Key);
        var afterSkill1 = await SubmitAsync(client, start.SessionId, afterMoreYes.NextQuestion.Id, "\"Blazor\"");
        var afterMoreNo = await SubmitAsync(client, start.SessionId, afterSkill1.NextQuestion!.Id, "\"no\"");
        Assert.Equal(DemoDialog.SummaryKey, afterMoreNo.NextQuestion!.Key);

        var afterSummary = await SubmitAsync(client, start.SessionId, afterMoreNo.NextQuestion.Id, "true");
        Assert.True(afterSummary.IsCompleted);
        Assert.Null(afterSummary.NextQuestion);

        // In-Process-Handler wurde beim Abschluss ausgelöst (Beleg für Publish + AddFlirtyHandler).
        var triggers = host.Services.GetRequiredService<TriggerLog>().Snapshot();
        var trigger = Assert.Single(triggers);
        Assert.Equal(DemoDialog.DialogKey, trigger.DialogKey);
        Assert.Equal(start.SessionId, trigger.SessionId);

        // Resume: der gelesene Zustand zeigt zwei gesammelte skill-Iterationen (Loop über Liste).
        var state = await client.GetFromJsonAsync<SessionStateResponse>($"/flirty/sessions/{start.SessionId}");
        Assert.NotNull(state);
        Assert.Equal(SessionStatus.Completed, state!.Status);
        var skillIterations = state.Answers
            .Where(a => a.QuestionKey == DemoDialog.SkillKey)
            .Select(a => a.IterationIndex)
            .OrderBy(i => i)
            .ToArray();
        Assert.Equal(new int?[] { 0, 1 }, skillIterations);
    }

    [Fact]
    public async Task Branching_default_Zweig_fuehrt_zu_product()
    {
        await using var host = await WebSampleTestHost.StartAsync();
        var client = host.Client;

        var start = await StartAsync(client, "pm-user");
        var afterRole = await SubmitAsync(client, start.SessionId, start.CurrentQuestion.Id, "\"pm\"");

        Assert.Equal(DemoDialog.ProductKey, afterRole.NextQuestion!.Key);
    }

    [Fact]
    public async Task Edit_der_Startfrage_wechselt_den_Zweig_und_verwirft_nachgelagerte_Antworten()
    {
        await using var host = await WebSampleTestHost.StartAsync();
        var client = host.Client;

        var start = await StartAsync(client, "edit-user");
        var afterRole = await SubmitAsync(client, start.SessionId, start.CurrentQuestion.Id, "\"dev\"");
        await SubmitAsync(client, start.SessionId, afterRole.NextQuestion!.Id, "\"C#\"");

        // role dev -> pm editieren: Pfad wird neu berechnet (product), nachgelagerte Antworten verworfen.
        var response = await client.PutAsJsonAsync(
            $"/flirty/sessions/{start.SessionId}/answers/{start.CurrentQuestion.Id}",
            new { value = "\"pm\"" });
        response.EnsureSuccessStatusCode();
        var edit = (await response.Content.ReadFromJsonAsync<EditAnswerResponse>())!;

        Assert.Equal(DemoDialog.ProductKey, edit.NextQuestion!.Key);
        Assert.True(edit.InvalidatedAnswers > 0);
    }

    [Fact]
    public async Task Edit_einer_Loop_Iteration_ueber_iterationIndex_ist_gezielt_moeglich()
    {
        await using var host = await WebSampleTestHost.StartAsync();
        var client = host.Client;

        var start = await StartAsync(client, "loop-edit-user");
        var afterRole = await SubmitAsync(client, start.SessionId, start.CurrentQuestion.Id, "\"dev\"");
        var afterLanguage = await SubmitAsync(client, start.SessionId, afterRole.NextQuestion!.Id, "\"C#\"");
        var skillId = afterLanguage.NextQuestion!.Id;
        var afterSkill0 = await SubmitAsync(client, start.SessionId, skillId, "\"EF Core\"");
        var afterMoreYes = await SubmitAsync(client, start.SessionId, afterSkill0.NextQuestion!.Id, "\"yes\"");
        await SubmitAsync(client, start.SessionId, afterMoreYes.NextQuestion!.Id, "\"Blazor\"");

        // Gezielt Iteration 0 der skill-Frage editieren.
        var response = await client.PutAsJsonAsync(
            $"/flirty/sessions/{start.SessionId}/answers/{skillId}",
            new { value = "\"EF Core 10\"", iterationIndex = 0 });
        response.EnsureSuccessStatusCode();
        var edit = (await response.Content.ReadFromJsonAsync<EditAnswerResponse>())!;

        Assert.True(edit.InvalidatedAnswers > 0);
        var state = await client.GetFromJsonAsync<SessionStateResponse>($"/flirty/sessions/{start.SessionId}");
        var iter0 = state!.Answers.Single(a => a.QuestionKey == DemoDialog.SkillKey && a.IterationIndex == 0);
        Assert.Equal("\"EF Core 10\"", iter0.Value);
    }

    [Fact]
    public async Task Inbound_Webhook_Empfaenger_nimmt_Zustellung_entgegen_und_zeigt_sie_an()
    {
        await using var host = await WebSampleTestHost.StartAsync();
        var client = host.Client;

        using var request = new HttpRequestMessage(HttpMethod.Post, WebSampleApp.WebhookReceiverPath)
        {
            Content = JsonContent.Create(new { sessionId = Guid.NewGuid(), dialogKey = DemoDialog.DialogKey }),
        };
        request.Headers.Add("X-Flirty-Event", nameof(TriggerScope.OnDialogCompleted));
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var receipts = await client.GetFromJsonAsync<IReadOnlyList<WebhookReceipt>>("/demo/webhooks");
        var receipt = Assert.Single(receipts!);
        Assert.Equal(nameof(TriggerScope.OnDialogCompleted), receipt.Event);
        Assert.Contains(DemoDialog.DialogKey, receipt.Payload);
    }

    private static async Task<StartSessionResponse> StartAsync(HttpClient client, string userKey)
    {
        var response = await client.PostAsJsonAsync(
            "/flirty/sessions", new { dialogKey = DemoDialog.DialogKey, externalUserKey = userKey });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<StartSessionResponse>())!;
    }

    private static async Task<SubmitAnswerResponse> SubmitAsync(HttpClient client, Guid sessionId, Guid questionId, string rawJsonValue)
    {
        var response = await client.PostAsJsonAsync(
            $"/flirty/sessions/{sessionId}/answers", new { questionId, value = rawJsonValue });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SubmitAnswerResponse>())!;
    }

    /// <summary>
    /// In-Process-TestServer-Host, der die echte Sample-Komposition (<see cref="WebSampleApp"/>) gegen eine
    /// SQLite-in-memory-Datenbank hochfährt. Auto-Provisioning und Outbound-Webhook sind für den TestServer
    /// deaktiviert; der Demo-Dialog wird nach dem Start über die Admin-CRUD-API (TestServer-Client) gebaut.
    /// </summary>
    private sealed class WebSampleTestHost : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly SqliteConnection _keepAlive;

        private WebSampleTestHost(WebApplication app, SqliteConnection keepAlive)
        {
            _app = app;
            _keepAlive = keepAlive;
            Client = app.GetTestClient();
        }

        public HttpClient Client { get; }

        public IServiceProvider Services => _app.Services;

        public static async Task<WebSampleTestHost> StartAsync()
        {
            var connectionString = $"Data Source=WebSampleTest-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
            var keepAlive = new SqliteConnection(connectionString);
            await keepAlive.OpenAsync();

            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseTestServer();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Flirty"] = connectionString,
                ["Flirty:ApplyMigrations"] = "false",
                ["Flirty:EnableOutboundWebhook"] = "false",
                ["Flirty:AutoProvision"] = "false",
            });

            WebSampleApp.ConfigureServices(builder);

            var app = builder.Build();
            WebSampleApp.MapEndpoints(app);
            await app.StartAsync();

            using (var scope = app.Services.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<FlirtyDbContext>();
                await context.Database.EnsureCreatedAsync();
            }

            await DemoDialogProvisioner.EnsureProvisionedAsync(app.GetTestClient(), app.Services, NullLogger.Instance);

            return new WebSampleTestHost(app, keepAlive);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.DisposeAsync();
            await _keepAlive.DisposeAsync();
        }
    }
}
