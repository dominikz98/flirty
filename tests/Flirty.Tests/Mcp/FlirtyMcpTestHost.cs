using Flirty.Mcp;
using Flirty.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;

namespace Flirty.Tests.Mcp;

/// <summary>
/// Shared in-process <see cref="TestServer"/> infrastructure for the integration tests of
/// <c>Flirty.Mcp</c>. A sibling of <c>AspNetCore/FlirtyTestHost.cs</c> rather than an overload on it,
/// because it does one thing more: it serves the MCP endpoint <b>and</b> both HTTP surfaces over the
/// <b>same</b> SQLite in-memory database and hands out a connected <see cref="McpClient"/> alongside the
/// <see cref="HttpClient"/>. That is what makes the error-parity tests literally "the same seeded database
/// through both surfaces" instead of two hosts sharing a connection string.
/// </summary>
internal sealed class FlirtyMcpTestHost : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly SqliteConnection _keepAlive;

    private FlirtyMcpTestHost(
        WebApplication app, SqliteConnection keepAlive, McpClient mcp, RecordingLoggerProvider logs)
    {
        _app = app;
        _keepAlive = keepAlive;
        Mcp = mcp;
        Logs = logs;
        Client = app.GetTestClient();
    }

    /// <summary>The HTTP client bound to the TestServer (both Flirty HTTP surfaces).</summary>
    public HttpClient Client { get; }

    /// <summary>The connected MCP client, driving the real Streamable-HTTP wire against <c>/mcp</c>.</summary>
    public McpClient Mcp { get; }

    /// <summary>The server-side log, for the tests that assert on the catch-all branch.</summary>
    public RecordingLoggerProvider Logs { get; }

    /// <summary>
    /// Starts an in-process TestServer with the complete Flirty stack (SQLite in-memory), the two HTTP
    /// surfaces and the MCP server, applies the optional <paramref name="seed"/> and connects an MCP client.
    /// </summary>
    /// <param name="seed">Optional delegate for seeding the database before the first request.</param>
    /// <param name="includeThrowingTools">
    /// Registers <see cref="FlirtyThrowingTestTools"/> in addition. The injection seam that makes all six
    /// engine exceptions reachable over MCP while the runtime tools do not exist yet – see the class docs
    /// of <see cref="FlirtyMcpExceptionParityTests"/>.
    /// </param>
    /// <param name="configureMcp">Optional configuration of the MCP server (tool surfaces, server name).</param>
    /// <returns>The started, usable test host.</returns>
    public static async Task<FlirtyMcpTestHost> StartAsync(
        Action<FlirtyDbContext>? seed = null,
        bool includeThrowingTools = false,
        Action<FlirtyMcpOptions>? configureMcp = null)
    {
        var connectionString = $"Data Source=FlirtyMcpTest-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        var keepAlive = new SqliteConnection(connectionString);
        await keepAlive.OpenAsync();

        var logs = new RecordingLoggerProvider();

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddLogging(logging => logging.AddProvider(logs));
        builder.Services.AddFlirty(options => options.UseSqlite(connectionString));

        var mcpBuilder = builder.Services.AddFlirtyMcp(configureMcp);
        if (includeThrowingTools)
        {
            mcpBuilder.WithTools<FlirtyThrowingTestTools>();
        }

        var app = builder.Build();
        app.MapFlirtyEndpoints("/flirty");
        app.MapFlirtyAdminEndpoints("/flirty/admin");
        app.MapFlirtyMcp("/mcp");
        await app.StartAsync();

        using (var scope = app.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FlirtyDbContext>();
            await context.Database.EnsureCreatedAsync();
            seed?.Invoke(context);
            await context.SaveChangesAsync();
        }

        // StreamableHttp explicitly instead of AutoDetect: the server is stateless, so it maps no GET
        // endpoint, and the auto-detect probe would waste a round trip discovering that.
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri("http://localhost/mcp"),
                TransportMode = HttpTransportMode.StreamableHttp,
            },
            app.GetTestClient());
        var mcp = await McpClient.CreateAsync(transport);

        return new FlirtyMcpTestHost(app, keepAlive, mcp, logs);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await Mcp.DisposeAsync();
        Client.Dispose();
        await _app.DisposeAsync();
        await _keepAlive.DisposeAsync();
    }
}
