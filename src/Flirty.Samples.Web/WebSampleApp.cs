using Flirty.Domain;
using Flirty.Runtime;

namespace Flirty.Samples.Web;

/// <summary>
/// Central, reusable composition of the web sample. <see cref="ConfigureServices"/> wires up the
/// Flirty stack (persistence, runtime endpoints, in-process handler, outbound webhook, provisioning) and
/// <see cref="MapEndpoints"/> registers the HTTP endpoints together with the static chat UI. Both are used
/// by <c>Program.cs</c> (real Kestrel) and by the integration tests (in-process <c>TestServer</c>),
/// so that app and test share the same setup.
/// </summary>
public static class WebSampleApp
{
    /// <summary>Default base URL (overridable via configuration <c>Flirty:BaseUrl</c>).</summary>
    public const string DefaultBaseUrl = "http://localhost:5080";

    /// <summary>Name of the named <see cref="System.Net.Http.HttpClient"/> for the admin provisioning.</summary>
    public const string AdminHttpClientName = "Flirty.Admin";

    /// <summary>Route to which the outbound webhook is delivered and which the inbound receiver serves.</summary>
    public const string WebhookReceiverPath = "/demo/webhooks/flirty";

    /// <summary>
    /// Wires up all services of the web sample. Controllable configuration (with defaults):
    /// <list type="bullet">
    /// <item><description><c>ConnectionStrings:Flirty</c> – SQLite connection (default file-based).</description></item>
    /// <item><description><c>Flirty:BaseUrl</c> – own base URL for provisioning + webhook target.</description></item>
    /// <item><description><c>Flirty:ApplyMigrations</c> – auto-migration on startup (default <c>true</c>).</description></item>
    /// <item><description><c>Flirty:EnableOutboundWebhook</c> – register the outbound webhook (default <c>true</c>).</description></item>
    /// <item><description><c>Flirty:AutoProvision</c> – build the demo dialog itself on startup (default <c>true</c>).</description></item>
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
                // Loopback demo: the engine delivers the completion notification via HTTP to this app's
                // own inbound receiver (visible in the chat UI's trigger panel).
                options.AddWebhook(TriggerScope.OnDialogCompleted, baseUrl + WebhookReceiverPath);
            }
        });

        // Custom in-process handler (trigger back-channel) + in-memory sinks for the UI display.
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
    /// Registers the static chat UI, the Flirty runtime and admin endpoints as well as the
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

        // Admin CRUD to build the demo dialog. In the sample deliberately WITHOUT RequireAuthorization()
        // (simplicity) – in production be sure to secure it (see docs/GETTING-STARTED-Sample-Web.md).
        app.MapFlirtyAdminEndpoints("/flirty/admin");

        MapDemoEndpoints(app);
    }

    private static void MapDemoEndpoints(WebApplication app)
    {
        var demo = app.MapGroup("/demo").WithTags("Flirty Sample");

        // Inbound webhook receiver: takes in the engine's outbound POST, reads the
        // trigger header (X-Flirty-Event) and the JSON body and stores both for the UI.
        demo.MapPost("/webhooks/flirty", async (HttpRequest request, WebhookInbox inbox, CancellationToken cancellationToken) =>
        {
            var eventName = request.Headers["X-Flirty-Event"].ToString();
            using var reader = new StreamReader(request.Body);
            var payload = await reader.ReadToEndAsync(cancellationToken);
            inbox.Add(eventName, payload);
            return Results.Ok();
        });

        // Display endpoints for the chat UI's trigger panel.
        demo.MapGet("/webhooks", (WebhookInbox inbox) => Results.Ok(inbox.Snapshot()));
        demo.MapGet("/triggers", (TriggerLog log) => Results.Ok(log.Snapshot()));
    }
}
