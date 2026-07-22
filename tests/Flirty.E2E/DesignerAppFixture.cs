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
/// Hostet den echten Blazor-Designer (<see cref="DesignerApp"/>) in-Prozess auf einem freien
/// Kestrel-Port und stellt ihm ein bereits aktives Connection-Profil auf eine frisch migrierte
/// SQLite-Temp-Datenbank bereit. Damit startet jeder E2E-Test direkt auf <c>/dialogs</c>, ohne
/// vorher die Profil-Verwaltung durchklicken zu müssen.
/// </summary>
public sealed class DesignerAppFixture : IAsyncLifetime
{
    private WebApplication? _app;
    private string? _contentRoot;
    private string? _databasePath;

    /// <summary>Die Basis-URL, unter der der Designer im Browser erreichbar ist.</summary>
    public string BaseUrl { get; private set; } = string.Empty;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        var port = GetFreeTcpPort();
        BaseUrl = $"http://127.0.0.1:{port}";

        // Eigenes ContentRoot je Lauf: dort landet die (per .gitignore ausgeschlossene)
        // connection-profiles.json, statt im Repo oder im Testausgabeverzeichnis.
        _contentRoot = Path.Combine(Path.GetTempPath(), $"flirty-designer-e2e-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_contentRoot);

        _databasePath = Path.Combine(_contentRoot, "designer.db");
        var profile = new ConnectionProfile
        {
            Name = "E2E",
            Provider = FlirtyDatabaseProvider.Sqlite,
            // Pooling=False: sonst hält der SQLite-Connection-Pool die Datei offen und der Cleanup scheitert.
            ConnectionString = $"Data Source={_databasePath};Pooling=False",
        };

        var migration = await new ConnectionProfileOperations().ApplyMigrationsAsync(profile);
        if (!migration.Success)
        {
            throw new InvalidOperationException(
                "Die Temp-Datenbank der Designer-E2E ließ sich nicht migrieren: " + migration.Error);
        }

        // Profil als Standard hinterlegen -> ActiveConnectionProfile aktiviert es in jedem neuen Circuit.
        var store = new JsonConnectionProfileStore(
            Path.Combine(_contentRoot, DesignerApp.ConnectionProfilesFileName));
        store.Save(profile);
        store.SetDefault(profile.Id);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            // Beide Angaben sind funktional zwingend, nicht kosmetisch:
            // - ApplicationName: der StaticWebAssetsLoader sucht die "<App>.staticwebassets.runtime.json"
            //   über Assembly.Load(ApplicationName), MapStaticAssets() leitet den Namen der
            //   endpoints.json davon ab. Ohne das wird _framework/blazor.web.js nicht ausgeliefert,
            //   der Circuit kommt nie zustande und jeder Klick im Test verpufft.
            // - Development: nur in dieser Umgebung ruft der WebApplicationBuilder überhaupt
            //   UseStaticWebAssets() auf. Nebeneffekt (erwünscht): Developer Exception Page statt
            //   /Error, das macht rote Tests lesbar.
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
