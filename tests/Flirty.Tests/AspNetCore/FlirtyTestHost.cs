using Flirty.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Flirty.Tests.AspNetCore;

/// <summary>
/// Shared in-process <see cref="TestServer"/> infrastructure for the endpoint integration tests of
/// <c>Flirty.AspNetCore</c>. Builds a fresh host per test against a SQLite in-memory database
/// (Docker-free) and registers both the runtime endpoints (<c>MapFlirtyEndpoints</c>) and the admin
/// CRUD endpoints (<c>MapFlirtyAdminEndpoints</c>). The keep-alive connection keeps the shared-cache
/// database alive across all request scopes; on disposal host and connection are cleaned up.
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

    /// <summary>The HTTP client bound to the TestServer.</summary>
    public HttpClient Client { get; }

    /// <summary>
    /// Starts an in-process TestServer with the complete Flirty stack (SQLite in-memory) and applies
    /// the optional <paramref name="seed"/> to the freshly created database.
    /// </summary>
    /// <param name="seed">Optional delegate for seeding the database before the first request.</param>
    /// <returns>The started, usable test host.</returns>
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
