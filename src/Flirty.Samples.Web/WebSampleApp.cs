using Flirty.Domain;
using Flirty.Runtime;

namespace Flirty.Samples.Web;

/// <summary>
/// Central, reusable composition of the web sample. <see cref="ConfigureServices"/> wires up the
/// Flirty stack (persistence, runtime endpoints, MCP server, in-process handler, outbound webhook,
/// provisioning) and
/// <see cref="MapEndpoints"/> registers the HTTP endpoints together with the static chat UI. Both are used by
/// <c>Program.cs</c> (real Kestrel) and by the integration tests (in-process <c>TestServer</c>),
/// so that app and test share the same setup.
/// </summary>
public static class WebSampleApp
{
    /// <summary>Default base URL (overridable via configuration <c>Flirty:BaseUrl</c>).</summary>
    public const string DefaultBaseUrl = "http://localhost:5080";

    /// <summary>Name of the named <see cref="System.Net.Http.HttpClient"/> for admin provisioning.</summary>
    public const string AdminHttpClientName = "Flirty.Admin";

    /// <summary>Route to which the outbound webhook is delivered and which the inbound receiver serves.</summary>
    public const string WebhookReceiverPath = "/demo/webhooks/flirty";

    /// <summary>
    /// Wires up all services of the web sample. Controllable configuration (with defaults):
    /// <list type="bullet">
    /// <item><description><c>ConnectionStrings:Flirty</c> – SQLite connection (default file-based).</description></item>
    /// <item><description><c>Flirty:BaseUrl</c> – own base URL for provisioning + webhook target.</description></item>
    /// <item><description><c>Flirty:ApplyMigrations</c> – auto-migration at startup (default <c>true</c>).</description></item>
    /// <item><description><c>Flirty:EnableOutboundWebhook</c> – register the outbound webhook (default <c>true</c>).</description></item>
    /// <item><description><c>Flirty:AutoProvision</c> – build the demo dialog at startup (default <c>true</c>).</description></item>
    /// </list>
    /// </summary>
    /// <param name="builder">The host builder of the web app.</param>
    public static void ConfigureServices(WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var config = builder.Configuration;
        var connectionString = config.GetConnectionString("Flirty") ?? "Data Source=flirty-sample.db";
        var baseUrl = (config["Flirty:BaseUrl"] ?? DefaultBaseUrl).TrimEnd('/');
        var applyMigrations = config.GetValue("Flirty:ApplyMigrations", true);
        var enableOutboundWebhook = config.GetValue("Flirty:EnableOutboundWebhook", true);
        var autoProvision = config.GetValue("Flirty:AutoProvision", true);

        builder.Services.AddFlirty(options =>
        {
            options.UseSqlite(connectionString);
            if (applyMigrations)
            {
                options.ApplyMigrations();
            }

            if (enableOutboundWebhook)
            {
                // Loopback demo: the engine delivers the completion notification via HTTP to this app's own
                // inbound receiver (visible in the trigger panel of the chat UI).
                options.AddWebhook(TriggerScope.OnDialogCompleted, baseUrl + WebhookReceiverPath);
            }

            // Two host-declared question types (#136), deliberately of different SHAPE rather than of
            // different validation route: "color" is a scalar (a JSON string), "address" is composite (a
            // JSON object of several fields). Both hang off QuestionType.Json and both bring their rules
            // as code, which is where a custom type owns them. The pair is what shows that one extension
            // point carries both, and that the stored value stays opaque to the engine.
            options.AddQuestionType<ColourAnswerValidator>(
                DemoDialog.ColourTypeKey, "Colour picker", sample: "\"#ff0000\"");
            options.AddQuestionType<AddressAnswerValidator>(
                DemoDialog.AddressTypeKey,
                "Postal address",
                sample: """{"street":"Main 1","zip":"10115","city":"Berlin"}""");
        });

        // MCP server over the same engine: an MCP client can configure dialogs where the chat UI only
        // plays them. Deliberately after AddFlirty – AddFlirtyMcp does not register a provider itself.
        builder.Services.AddFlirtyMcp();

        // Own in-process handler (trigger back channel) + in-memory sinks for the UI display.
        builder.Services.AddFlirtyHandler<DialogCompletedNotification, DemoDialogCompletedHandler>();
        builder.Services.AddSingleton<TriggerLog>();
        builder.Services.AddSingleton<WebhookInbox>();

        if (autoProvision)
        {
            builder.Services.AddHttpClient(AdminHttpClientName, client => client.BaseAddress = new Uri(baseUrl));
            builder.Services.AddHostedService<DemoProvisioningHostedService>();
        }
    }

    /// <summary>
    /// Registers the static chat UI, the Flirty runtime and admin endpoints, the MCP server as well as the
    /// demo endpoints (inbound webhook receiver + trigger/webhook display).
    /// </summary>
    /// <param name="app">The built web app.</param>
    public static void MapEndpoints(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Static chat UI from wwwroot (index.html as the default document).
        app.UseDefaultFiles();
        app.UseStaticFiles();

        // Runtime endpoints consumed by the chat UI (start/resume/answer/edit).
        app.MapFlirtyEndpoints("/flirty");

        // Admin CRUD for building the demo dialog. In the sample deliberately WITHOUT RequireAuthorization()
        // (simplicity) – in production make sure to secure it (see docs/GETTING-STARTED-Sample-Web.md).
        app.MapFlirtyAdminEndpoints("/flirty/admin");

        // The MCP server. In the sample deliberately WITHOUT RequireAuthorization() (simplicity) – the
        // tools include write operations, and since #128 also the runtime ones, which start real sessions
        // (flirty_session_start_version even on an unpublished draft) and deliver the outbound webhook
        // registered above. So in production secure it, and register FlirtyMcpSurface.Admin if a client
        // should be able to author dialogs but not run them.
        app.MapFlirtyMcp("/mcp");

        MapDemoEndpoints(app);
    }

    private static void MapDemoEndpoints(WebApplication app)
    {
        var demo = app.MapGroup("/demo").WithTags("Flirty Sample");

        // Inbound webhook receiver: accepts the engine's outbound POST, reads the
        // trigger header (X-Flirty-Event) and the JSON body and stores both for the UI.
        demo.MapPost("/webhooks/flirty", async (HttpRequest request, WebhookInbox inbox, CancellationToken cancellationToken) =>
        {
            var eventName = request.Headers["X-Flirty-Event"].ToString();
            using var reader = new StreamReader(request.Body);
            var payload = await reader.ReadToEndAsync(cancellationToken);
            inbox.Add(eventName, payload);
            return Results.Ok();
        });

        // Display endpoints for the trigger panel of the chat UI.
        demo.MapGet("/webhooks", (WebhookInbox inbox) => Results.Ok(inbox.Snapshot()));
        demo.MapGet("/triggers", (TriggerLog log) => Results.Ok(log.Snapshot()));
    }
}
