using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
using ModelContextProtocol.Client;

namespace Flirty.Tests.Samples;

/// <summary>
/// Checks the web sample (#45) end-to-end over an in-process <see cref="TestServer"/>: the real
/// sample composition (<see cref="WebSampleApp"/>) is hosted, the demo dialog is built via the admin
/// CRUD API and then played through over the runtime endpoints. Covered: branching, loop over a list,
/// resume, edit, in-process handler dispatch and the inbound webhook receiver. The full
/// outbound→inbound webhook round trip is deliberately left to the Playwright E2E (it needs a real
/// Kestrel).
/// </summary>
public sealed class WebSampleTests
{
    [Fact]
    public async Task Branching_dev_branch_loop_and_completion_fire_the_in_process_handler()
    {
        await using var host = await WebSampleTestHost.StartAsync();
        var client = host.Client;

        var start = await StartAsync(client, "dev-user");
        Assert.False(start.IsResumed);
        Assert.Equal(DemoDialog.RoleKey, start.CurrentQuestion.Key);

        // Branching: role == "dev" -> language, not product.
        var afterRole = await SubmitAsync(client, start.SessionId, start.CurrentQuestion.Id, "\"dev\"");
        Assert.Equal(DemoDialog.LanguageKey, afterRole.NextQuestion!.Key);

        var afterLanguage = await SubmitAsync(client, start.SessionId, afterRole.NextQuestion.Id, "\"C#\"");
        Assert.Equal(DemoDialog.SkillKey, afterLanguage.NextQuestion!.Key);

        // Loop over a list: skill (iteration 0) -> more=yes (loop back) -> skill (iteration 1) -> more=no (exit).
        var afterSkill0 = await SubmitAsync(client, start.SessionId, afterLanguage.NextQuestion.Id, "\"EF Core\"");
        Assert.Equal(DemoDialog.MoreKey, afterSkill0.NextQuestion!.Key);
        var afterMoreYes = await SubmitAsync(client, start.SessionId, afterSkill0.NextQuestion.Id, "\"yes\"");
        Assert.Equal(DemoDialog.SkillKey, afterMoreYes.NextQuestion!.Key);
        var afterSkill1 = await SubmitAsync(client, start.SessionId, afterMoreYes.NextQuestion.Id, "\"Blazor\"");
        var afterMoreNo = await SubmitAsync(client, start.SessionId, afterSkill1.NextQuestion!.Id, "\"no\"");
        Assert.Equal(DemoDialog.ColourKey, afterMoreNo.NextQuestion!.Key);

        // The two host-declared custom question types (#136): a scalar one and a composite one, both
        // QuestionType.Json, both checked by the host's own IQuestionTypeValidator.
        var afterColour = await SubmitAsync(
            client, start.SessionId, afterMoreNo.NextQuestion.Id, "\"#ff8800\"");
        Assert.Equal(DemoDialog.AddressKey, afterColour.NextQuestion!.Key);

        var afterAddress = await SubmitAsync(
            client, start.SessionId, afterColour.NextQuestion.Id,
            """{"street":"Main 1","zip":"10115","city":"Berlin"}""");
        Assert.Equal(DemoDialog.SummaryKey, afterAddress.NextQuestion!.Key);

        // The final question carries the {{user-name}} and {{today}} placeholders (#140); by the time it is
        // delivered both markers are filled - no raw marker survives to the client.
        Assert.DoesNotContain("{{", afterAddress.NextQuestion.Text, StringComparison.Ordinal);
        Assert.Contains("dev-user", afterAddress.NextQuestion.Text, StringComparison.Ordinal);

        var afterSummary = await SubmitAsync(client, start.SessionId, afterAddress.NextQuestion.Id, "true");
        Assert.True(afterSummary.IsCompleted);
        Assert.Null(afterSummary.NextQuestion);

        // The in-process handler fired on completion (proof of Publish + AddFlirtyHandler).
        var triggers = host.Services.GetRequiredService<TriggerLog>().Snapshot();
        var trigger = Assert.Single(triggers);
        Assert.Equal(DemoDialog.DialogKey, trigger.DialogKey);
        Assert.Equal(start.SessionId, trigger.SessionId);

        // Resume: the read state shows two collected skill iterations (loop over a list).
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
    public async Task Branching_default_branch_leads_to_product()
    {
        await using var host = await WebSampleTestHost.StartAsync();
        var client = host.Client;

        var start = await StartAsync(client, "pm-user");
        var afterRole = await SubmitAsync(client, start.SessionId, start.CurrentQuestion.Id, "\"pm\"");

        Assert.Equal(DemoDialog.ProductKey, afterRole.NextQuestion!.Key);
    }

    /// <summary>
    /// A message placeholder (#140) is filled at delivery time: the entry question text
    /// <c>"Hi {{user-name}}! …"</c> is delivered with the marker replaced by the value the host's
    /// <see cref="UserNamePlaceholderFiller"/> produces from the session's user key. Proved over the same
    /// HTTP surface the chat UI uses, so it covers the whole engine → endpoint → client path at once.
    /// </summary>
    [Fact]
    public async Task A_message_placeholder_is_filled_at_delivery_time()
    {
        await using var host = await WebSampleTestHost.StartAsync();
        var client = host.Client;

        var start = await StartAsync(client, "Alex");

        Assert.Equal(DemoDialog.RoleKey, start.CurrentQuestion.Key);
        Assert.Equal("Hi Alex! What is your role?", start.CurrentQuestion.Text);
        Assert.DoesNotContain("{{", start.CurrentQuestion.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Edit_of_the_entry_question_switches_the_branch_and_discards_downstream_answers()
    {
        await using var host = await WebSampleTestHost.StartAsync();
        var client = host.Client;

        var start = await StartAsync(client, "edit-user");
        var afterRole = await SubmitAsync(client, start.SessionId, start.CurrentQuestion.Id, "\"dev\"");
        await SubmitAsync(client, start.SessionId, afterRole.NextQuestion!.Id, "\"C#\"");

        // Edit role dev -> pm: the path is recomputed (product), downstream answers are discarded.
        var response = await client.PutAsJsonAsync(
            $"/flirty/sessions/{start.SessionId}/answers/{start.CurrentQuestion.Id}",
            new { value = "\"pm\"" });
        response.EnsureSuccessStatusCode();
        var edit = (await response.Content.ReadFromJsonAsync<EditAnswerResponse>())!;

        Assert.Equal(DemoDialog.ProductKey, edit.NextQuestion!.Key);
        Assert.True(edit.InvalidatedAnswers > 0);
    }

    [Fact]
    public async Task Edit_of_a_loop_iteration_via_iterationIndex_targets_exactly_that_iteration()
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

        // Edit iteration 0 of the skill question specifically.
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
    public async Task Inbound_webhook_receiver_accepts_a_delivery_and_shows_it()
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

    /// <summary>
    /// The sample serves the MCP endpoint too (#126): an MCP client can configure dialogs where the chat UI
    /// only plays them. Driven by a real <c>McpClient</c> against the sample's own composition, so the
    /// opt-in wiring is checked and not just the package.
    /// </summary>
    [Fact]
    public async Task WebSample_serves_the_mcp_endpoint()
    {
        await using var host = await WebSampleTestHost.StartAsync();

        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri("http://localhost/mcp"),
                TransportMode = HttpTransportMode.StreamableHttp,
            },
            host.Client);
        await using var mcp = await McpClient.CreateAsync(transport);

        var tools = await mcp.ListToolsAsync();

        Assert.Contains("flirty_dialog_list", tools.Select(tool => tool.Name));
    }

    private static async Task<StartSessionResponse> StartAsync(HttpClient client, string userKey)
    {
        var response = await client.PostAsJsonAsync(
            "/flirty/sessions", new { dialogKey = DemoDialog.DialogKey, externalUserKey = userKey });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<StartSessionResponse>())!;
    }

    /// <summary>
    /// The scalar host-declared type end to end: a well-formed JSON string that is not a colour passes
    /// the engine's own check and is refused by the <b>host's</b> validator – which is the whole point of
    /// the extension path. A wrong format cannot be produced by the browser's colour picker, so this
    /// lives here rather than in the E2E suite.
    /// </summary>
    [Fact]
    public async Task A_colour_answer_of_the_wrong_format_is_refused_by_the_host_validator()
    {
        await using var host = await WebSampleTestHost.StartAsync();
        var client = host.Client;

        var (sessionId, colourQuestionId) = await WalkToColourAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/flirty/sessions/{sessionId}/answers",
            new { questionId = colourQuestionId, value = "\"not-a-colour\"" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("#rrggbb", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // Malformed JSON is refused one layer earlier, by the engine itself - also a 400, not a 500.
        var malformed = await client.PostAsJsonAsync(
            $"/flirty/sessions/{sessionId}/answers",
            new { questionId = colourQuestionId, value = "#ff0000" });
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
    }

    /// <summary>
    /// The composite type: a missing required field is refused with one error naming it, and the value
    /// then stored stays an opaque JSON object that the runtime hands back unchanged.
    /// </summary>
    [Fact]
    public async Task An_address_answer_needs_its_required_fields_and_round_trips_as_an_object()
    {
        await using var host = await WebSampleTestHost.StartAsync();
        var client = host.Client;

        var (sessionId, colourQuestionId) = await WalkToColourAsync(client);
        var afterColour = await SubmitAsync(client, sessionId, colourQuestionId, "\"#ff8800\"");
        var addressId = afterColour.NextQuestion!.Id;

        var incomplete = await client.PostAsJsonAsync(
            $"/flirty/sessions/{sessionId}/answers",
            new { questionId = addressId, value = """{"street":"Main 1","zip":"10115"}""" });

        Assert.Equal(HttpStatusCode.BadRequest, incomplete.StatusCode);
        Assert.Contains("'city'", await incomplete.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        await SubmitAsync(
            client, sessionId, addressId, """{"street":"Main 1","zip":"10115","city":"Berlin"}""");

        var state = await client.GetFromJsonAsync<SessionStateResponse>($"/flirty/sessions/{sessionId}");
        var stored = Assert.Single(state!.Answers, answer => answer.QuestionKey == DemoDialog.AddressKey);
        Assert.Contains("\"city\"", stored.Value, StringComparison.Ordinal);
    }

    /// <summary>
    /// The runtime question payload carries <c>customTypeKey</c> – without it the chat UI could not pick
    /// a control, and the whole custom type would be invisible to a client.
    /// </summary>
    [Fact]
    public async Task The_open_question_reports_its_custom_type_key()
    {
        await using var host = await WebSampleTestHost.StartAsync();
        var client = host.Client;

        var (sessionId, _) = await WalkToColourAsync(client);

        var state = await client.GetFromJsonAsync<JsonElement>($"/flirty/sessions/{sessionId}");
        var current = state.GetProperty("currentQuestion");

        Assert.Equal(DemoDialog.ColourKey, current.GetProperty("key").GetString());
        Assert.Equal(DemoDialog.ColourTypeKey, current.GetProperty("customTypeKey").GetString());
    }

    /// <summary>Walks the dev branch and one loop iteration up to the colour question.</summary>
    private static async Task<(Guid SessionId, Guid ColourQuestionId)> WalkToColourAsync(HttpClient client)
    {
        var start = await StartAsync(client, $"custom-types-{Guid.NewGuid():N}");
        var afterRole = await SubmitAsync(client, start.SessionId, start.CurrentQuestion.Id, "\"dev\"");
        var afterLanguage = await SubmitAsync(client, start.SessionId, afterRole.NextQuestion!.Id, "\"C#\"");
        var afterSkill = await SubmitAsync(
            client, start.SessionId, afterLanguage.NextQuestion!.Id, "\"EF Core\"");
        var afterMore = await SubmitAsync(client, start.SessionId, afterSkill.NextQuestion!.Id, "\"no\"");

        Assert.Equal(DemoDialog.ColourKey, afterMore.NextQuestion!.Key);
        return (start.SessionId, afterMore.NextQuestion.Id);
    }

    private static async Task<SubmitAnswerResponse> SubmitAsync(HttpClient client, Guid sessionId, Guid questionId, string rawJsonValue)
    {
        var response = await client.PostAsJsonAsync(
            $"/flirty/sessions/{sessionId}/answers", new { questionId, value = rawJsonValue });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SubmitAnswerResponse>())!;
    }

    /// <summary>
    /// In-process TestServer host that brings up the real sample composition
    /// (<see cref="WebSampleApp"/>) against a SQLite in-memory database. Auto-provisioning and the
    /// outbound webhook are disabled for the TestServer; the demo dialog is built after startup over
    /// the admin CRUD API (TestServer client).
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
