using System.Net;
using System.Net.Sockets;
using Flirty.Samples.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace Flirty.E2E;

/// <summary>
/// Hostet die echte Web-Sample (<see cref="WebSampleApp"/>) in-Prozess auf einem echten Kestrel-Port
/// (freier Port, dateibasierte SQLite-DB) – nur so funktioniert der volle Outbound→Inbound-Webhook-Rundlauf,
/// den die E2E-Tests im Browser verifizieren. Der Demo-Dialog wird durch den Auto-Provisioning-Dienst der
/// App aufgebaut; weil dieser in <c>StartedAsync</c> läuft und vom Host abgewartet wird, ist der Dialog nach
/// <see cref="InitializeAsync"/> deterministisch vorhanden.
/// </summary>
public sealed class WebSampleAppFixture : IAsyncLifetime
{
    private WebApplication? _app;
    private string? _databasePath;

    /// <summary>Die Basis-URL, unter der die Sample-App im Browser erreichbar ist.</summary>
    public string BaseUrl { get; private set; } = string.Empty;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        var port = GetFreeTcpPort();
        BaseUrl = $"http://127.0.0.1:{port}";
        _databasePath = Path.Combine(Path.GetTempPath(), $"flirty-e2e-{Guid.NewGuid():N}.db");

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            // wwwroot (per Content-Copy im Testausgabeverzeichnis) muss relativ zum ContentRoot liegen.
            ContentRootPath = AppContext.BaseDirectory,
        });
        builder.WebHost.UseUrls(BaseUrl);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Flirty"] = $"Data Source={_databasePath}",
            ["Flirty:BaseUrl"] = BaseUrl,
            ["Flirty:ApplyMigrations"] = "true",
            ["Flirty:AutoProvision"] = "true",
            ["Flirty:EnableOutboundWebhook"] = "true",
        });

        WebSampleApp.ConfigureServices(builder);

        _app = builder.Build();
        WebSampleApp.MapEndpoints(_app);

        // StartAsync wartet StartedAsync (Provisioning) ab -> Dialog ist danach vorhanden.
        await _app.StartAsync();
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }

        if (_databasePath is not null && File.Exists(_databasePath))
        {
            try { File.Delete(_databasePath); }
            catch (IOException) { /* Datei ggf. noch gelockt – best effort */ }
        }
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }
}
