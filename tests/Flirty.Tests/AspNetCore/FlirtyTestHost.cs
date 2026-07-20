using Flirty.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Flirty.Tests.AspNetCore;

/// <summary>
/// Gemeinsame In-Process-<see cref="TestServer"/>-Infrastruktur für die Endpunkt-Integrationstests von
/// <c>Flirty.AspNetCore</c>. Baut pro Test einen frischen Host gegen eine SQLite-in-memory-Datenbank
/// (Docker-frei) und registriert sowohl die Laufzeit-Endpunkte (<c>MapFlirtyEndpoints</c>) als auch die
/// Admin-CRUD-Endpunkte (<c>MapFlirtyAdminEndpoints</c>). Die keep-alive-Verbindung hält die
/// Shared-Cache-Datenbank über alle Request-Scopes am Leben; beim Verwerfen werden Host und Verbindung
/// aufgeräumt.
/// </summary>
internal sealed class FlirtyTestHost : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly SqliteConnection _keepAlive;

    private FlirtyTestHost(WebApplication app, SqliteConnection keepAlive)
    {
        _app = app;
        _keepAlive = keepAlive;
        Client = app.GetTestClient();
    }

    /// <summary>Der an den TestServer gebundene HTTP-Client.</summary>
    public HttpClient Client { get; }

    /// <summary>
    /// Startet einen In-Process-TestServer mit dem vollständigen Flirty-Stack (SQLite in-memory) und
    /// wendet den optionalen <paramref name="seed"/> auf die frisch erstellte Datenbank an.
    /// </summary>
    /// <param name="seed">Optionaler Delegat zum Seeden der Datenbank vor dem ersten Request.</param>
    /// <returns>Der gestartete, verwendbare Test-Host.</returns>
    public static async Task<FlirtyTestHost> StartAsync(Action<FlirtyDbContext>? seed = null)
    {
        var connectionString = $"Data Source=FlirtyApiTest-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        var keepAlive = new SqliteConnection(connectionString);
        await keepAlive.OpenAsync();

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddLogging();
        builder.Services.AddFlirty(options => options.UseSqlite(connectionString));

        var app = builder.Build();
        app.MapFlirtyEndpoints("/flirty");
        app.MapFlirtyAdminEndpoints("/flirty/admin");
        await app.StartAsync();

        using (var scope = app.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FlirtyDbContext>();
            await context.Database.EnsureCreatedAsync();
            seed?.Invoke(context);
            await context.SaveChangesAsync();
        }

        return new FlirtyTestHost(app, keepAlive);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.DisposeAsync();
        await _keepAlive.DisposeAsync();
    }
}
