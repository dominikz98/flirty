using System.Net;
using System.Net.Sockets;
using Flirty.Samples.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace Flirty.E2E;

/// <summary>
/// Hosts the real web sample (<see cref="WebSampleApp"/>) in-process on a real Kestrel port (free port,
/// file-based SQLite DB) – only that way does the full outbound→inbound webhook round-trip work that the
/// E2E tests verify in the browser. The demo dialog is built by the app's auto-provisioning service;
/// because that runs in <c>StartedAsync</c> and is awaited by the host, the dialog is deterministically
/// present after <see cref="InitializeAsync"/>.
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
