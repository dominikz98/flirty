using Flirty.Domain;
using Flirty.Runtime;

namespace Flirty.Samples.Web;

/// <summary>
/// Zentrale, wiederverwendbare Komposition der Web-Sample. <see cref="ConfigureServices"/> verdrahtet den
/// Flirty-Stack (Persistenz, Runtime-Endpunkte, In-Process-Handler, Outbound-Webhook, Provisioning) und
/// <see cref="MapEndpoints"/> registriert die HTTP-Endpunkte samt statischer Chat-UI. Beide werden von
/// <c>Program.cs</c> (echtes Kestrel) und von den Integrationstests (In-Process-<c>TestServer</c>) genutzt,
/// damit App und Test denselben Aufbau teilen.
/// </summary>
public static class WebSampleApp
{
    /// <summary>Standard-Basis-URL (überschreibbar via Konfiguration <c>Flirty:BaseUrl</c>).</summary>
    public const string DefaultBaseUrl = "http://localhost:5080";

    /// <summary>Name des benannten <see cref="System.Net.Http.HttpClient"/> für das Admin-Provisioning.</summary>
    public const string AdminHttpClientName = "Flirty.Admin";

    /// <summary>Route, an die der Outbound-Webhook zugestellt und die vom Inbound-Empfänger bedient wird.</summary>
    public const string WebhookReceiverPath = "/demo/webhooks/flirty";

    /// <summary>
    /// Verdrahtet alle Dienste der Web-Sample. Steuerbare Konfiguration (mit Defaults):
    /// <list type="bullet">
    /// <item><description><c>ConnectionStrings:Flirty</c> – SQLite-Verbindung (Default dateibasiert).</description></item>
    /// <item><description><c>Flirty:BaseUrl</c> – eigene Basis-URL für Provisioning + Webhook-Ziel.</description></item>
    /// <item><description><c>Flirty:ApplyMigrations</c> – Auto-Migration beim Start (Default <c>true</c>).</description></item>
    /// <item><description><c>Flirty:EnableOutboundWebhook</c> – Outbound-Webhook registrieren (Default <c>true</c>).</description></item>
    /// <item><description><c>Flirty:AutoProvision</c> – Demo-Dialog beim Start selbst aufbauen (Default <c>true</c>).</description></item>
    /// </list>
    /// </summary>
    /// <param name="builder">Der Host-Builder der Web-App.</param>
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
                // Loopback-Demo: die Engine liefert die Abschluss-Notification per HTTP an den eigenen
                // Inbound-Empfänger dieser App aus (sichtbar im Trigger-Panel der Chat-UI).
                options.AddWebhook(TriggerScope.OnDialogCompleted, baseUrl + WebhookReceiverPath);
            }
        });

        // Eigener In-Process-Handler (Trigger-Rückkanal) + In-Memory-Senken für die UI-Anzeige.
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
    /// Registriert die statische Chat-UI, die Flirty-Laufzeit- und Admin-Endpunkte sowie die
    /// Demo-Endpunkte (Inbound-Webhook-Empfänger + Trigger-/Webhook-Anzeige).
    /// </summary>
    /// <param name="app">Die gebaute Web-App.</param>
    public static void MapEndpoints(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Statische Chat-UI aus wwwroot (index.html als Default-Dokument).
        app.UseDefaultFiles();
        app.UseStaticFiles();

        // Von der Chat-UI konsumierte Laufzeit-Endpunkte (Start/Resume/Answer/Edit).
        app.MapFlirtyEndpoints("/flirty");

        // Admin-CRUD zum Aufbau des Demo-Dialogs. Im Sample bewusst OHNE RequireAuthorization()
        // (Einfachheit) – in Produktion unbedingt absichern (siehe docs/GETTING-STARTED-Sample-Web.md).
        app.MapFlirtyAdminEndpoints("/flirty/admin");

        MapDemoEndpoints(app);
    }

    private static void MapDemoEndpoints(WebApplication app)
    {
        var demo = app.MapGroup("/demo").WithTags("Flirty Sample");

        // Inbound-Webhook-Empfänger: nimmt den Outbound-POST der Engine entgegen, liest den
        // Trigger-Header (X-Flirty-Event) und den JSON-Body und legt beides für die UI ab.
        demo.MapPost("/webhooks/flirty", async (HttpRequest request, WebhookInbox inbox, CancellationToken cancellationToken) =>
        {
            var eventName = request.Headers["X-Flirty-Event"].ToString();
            using var reader = new StreamReader(request.Body);
            var payload = await reader.ReadToEndAsync(cancellationToken);
            inbox.Add(eventName, payload);
            return Results.Ok();
        });

        // Anzeige-Endpunkte für das Trigger-Panel der Chat-UI.
        demo.MapGet("/webhooks", (WebhookInbox inbox) => Results.Ok(inbox.Snapshot()));
        demo.MapGet("/triggers", (TriggerLog log) => Results.Ok(log.Snapshot()));
    }
}
