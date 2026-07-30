using System.Net;
using System.Net.Sockets;
using Flirty.Designer;
using Flirty.Designer.Models;
using Flirty.Designer.Services;
using Flirty.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Flirty.E2E;

/// <summary>
/// Hosts the real Blazor designer (<see cref="DesignerApp"/>) in-process on a free Kestrel port and
/// provides it with an already-active connection profile on a freshly migrated SQLite temp database.
/// That way every E2E test starts directly on <c>/dialogs</c>, without having to click through the
/// profile management first.
/// </summary>
public sealed class DesignerAppFixture : IAsyncLifetime
{
    private WebApplication? _app;
    private string? _contentRoot;
    private string? _databasePath;

    /// <summary>The base URL under which the designer is reachable in the browser.</summary>
    public string BaseUrl { get; private set; } = string.Empty;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        var port = GetFreeTcpPort();
        BaseUrl = $"http://127.0.0.1:{port}";

        // Own ContentRoot per run: that is where the (gitignored) connection-profiles.json lands, instead
        // of in the repo or the test output directory.
        _contentRoot = Path.Combine(Path.GetTempPath(), $"flirty-designer-e2e-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_contentRoot);

        _databasePath = Path.Combine(_contentRoot, "designer.db");
        var profile = new ConnectionProfile
        {
            Name = "E2E",
            Provider = FlirtyDatabaseProvider.Sqlite,
            // Pooling=False: otherwise the SQLite connection pool keeps the file open and cleanup fails.
            ConnectionString = $"Data Source={_databasePath};Pooling=False",
        };

        var migration = await new ConnectionProfileOperations().ApplyMigrationsAsync(profile);
        if (!migration.Success)
        {
            throw new InvalidOperationException(
                "The designer E2E temp database could not be migrated: " + migration.Error);
        }

        // Store the profile as the default -> ActiveConnectionProfile activates it in every new circuit.
        var store = new JsonConnectionProfileStore(
            Path.Combine(_contentRoot, DesignerApp.ConnectionProfilesFileName));
        store.Save(profile);
        store.SetDefault(profile.Id);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            // Both settings are functionally mandatory, not cosmetic:
            // - ApplicationName: the StaticWebAssetsLoader looks up the "<App>.staticwebassets.runtime.json"
            //   via Assembly.Load(ApplicationName), and MapStaticAssets() derives the name of the
            //   endpoints.json from it. Without it, _framework/blazor.web.js is not served, the circuit
            //   never comes up and every click in the test fizzles.
            // - Development: only in this environment does the WebApplicationBuilder call
            //   UseStaticWebAssets() at all. Side effect (desired): a Developer Exception Page instead of
            //   /Error, which makes red tests readable.
            ApplicationName = "Flirty.Designer",
            EnvironmentName = "Development",
            ContentRootPath = _contentRoot,
        });
        builder.WebHost.UseUrls(BaseUrl);

        DesignerApp.ConfigureServices(builder);

        _app = builder.Build();
        DesignerApp.Configure(_app);

        await _app.StartAsync();
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }

        if (_contentRoot is not null && Directory.Exists(_contentRoot))
        {
            // Best effort: die SQLite-Datei (samt -shm/-wal) kann noch kurz gelockt sein.
            try { Directory.Delete(_contentRoot, recursive: true); }
            catch (IOException) { /* egal – liegt im Temp-Verzeichnis */ }
            catch (UnauthorizedAccessException) { /* dito */ }
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
