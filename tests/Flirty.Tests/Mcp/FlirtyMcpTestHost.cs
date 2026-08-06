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
/// <remarks>
/// Since #129 it can also host <b>named database targets</b>. Each one gets its own in-memory database
/// <i>and its own keep-alive connection</i> – a shared-cache in-memory database dies with its last
/// connection, so one keep-alive for all of them would not do. Both MCP routes are mapped always, the
/// plain <c>/mcp</c> and <c>/mcp/{target}</c>, including in a host with no targets at all: naming a target
/// on a single-database server has to be reachable in order to be rejected.
/// </remarks>
internal sealed class FlirtyMcpTestHost : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly List<SqliteConnection> _keepAlive;
    private readonly Dictionary<string, string> _targetConnectionStrings;
    private readonly Dictionary<string, McpClient> _clients = [];

    private FlirtyMcpTestHost(
        WebApplication app,
        List<SqliteConnection> keepAlive,
        Dictionary<string, string> targetConnectionStrings,
        string hostConnectionString,
        McpClient mcp,
        RecordingLoggerProvider logs)
    {
        _app = app;
        _keepAlive = keepAlive;
        _targetConnectionStrings = targetConnectionStrings;
        HostConnectionString = hostConnectionString;
        Mcp = mcp;
        Logs = logs;
        Client = app.GetTestClient();
        _clients[string.Empty] = mcp;
    }

    /// <summary>The HTTP client bound to the TestServer (both Flirty HTTP surfaces).</summary>
    public HttpClient Client { get; }

    /// <summary>The connected MCP client, driving the real Streamable-HTTP wire against <c>/mcp</c>.</summary>
    public McpClient Mcp { get; }

    /// <summary>The server-side log, for the tests that assert on the catch-all branch.</summary>
    public RecordingLoggerProvider Logs { get; }

    /// <summary>
    /// The connection string of the database <c>AddFlirty</c> registered – the one every surface uses when
    /// no target is selected.
    /// </summary>
    public string HostConnectionString { get; }

    /// <summary>
    /// The connection strings of the declared targets – what the "no connection string on the wire"
    /// assertion searches a tool result for.
    /// </summary>
    public IReadOnlyCollection<string> TargetConnectionStrings => _targetConnectionStrings.Values;

    /// <summary>
    /// The host's service provider, for the few assertions that have to look at the database directly
    /// rather than through a surface – a session's stored <c>ExternalUserKey</c>, for instance, which no
    /// tool result carries. Open a scope on it; the <c>FlirtyDbContext</c> is scoped.
    /// </summary>
    public IServiceProvider Services => _app.Services;

    /// <summary>
    /// Starts an in-process TestServer with the complete Flirty stack (SQLite in-memory), the two HTTP
    /// surfaces and the MCP server, applies the optional <paramref name="seed"/> and connects an MCP client.
    /// </summary>
    /// <param name="seed">Optional delegate for seeding the database before the first request.</param>
    /// <param name="includeThrowingTools">
    /// Registers <see cref="FlirtyThrowingTestTools"/> in addition – the injection seam for the exceptions
    /// no real tool can raise. Since #128 the six <i>engine</i> exceptions all have real call paths, so the
    /// parity suite no longer needs it; what it still serves are the four SDK-owned branches and the
    /// mapping table asserted as a table. See the class docs of <see cref="FlirtyThrowingTestTools"/>.
    /// </param>
    /// <param name="configureMcp">
    /// Optional configuration of the MCP server (tool surfaces, server name, further targets). Invoked
    /// <b>after</b> <paramref name="targets"/> have been declared, so it can add
    /// <c>UseDefaultTarget</c>/<c>AllowMigrations</c> or a target of its own with a hand-written connection
    /// string.
    /// </param>
    /// <param name="targets">
    /// Names of database targets to declare, each on its own SQLite in-memory database.
    /// </param>
    /// <param name="prepareTargets">
    /// Whether those databases are migrated before the first request. <see langword="true"/> is what a
    /// working multi-database server looks like; <see langword="false"/> leaves them empty, which is what
    /// the pending-migrations and migrate tools need in order to have anything to do. Deliberately
    /// <c>MigrateAsync</c> and not <c>EnsureCreatedAsync</c>: the latter creates the schema without the
    /// <c>__EFMigrationsHistory</c> row, so every migration would still count as pending and applying them
    /// would then fail on tables that already exist.
    /// </param>
    /// <param name="configureFlirty">
    /// Extra configuration of the <b>engine</b> options, applied after the provider choice. The only way
    /// to declare a custom question type in a test host, because <c>AddQuestionType</c> lives on
    /// <c>FlirtyOptions</c> and not on the MCP options.
    /// </param>
    /// <returns>The started, usable test host.</returns>
    public static async Task<FlirtyMcpTestHost> StartAsync(
        Action<FlirtyDbContext>? seed = null,
        bool includeThrowingTools = false,
        Action<FlirtyMcpOptions>? configureMcp = null,
        IReadOnlyList<string>? targets = null,
        bool prepareTargets = true,
        Action<FlirtyOptions>? configureFlirty = null)
    {
        var connectionString = $"Data Source=FlirtyMcpTest-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        var keepAlive = new List<SqliteConnection>();
        await OpenKeepAliveAsync(keepAlive, connectionString);

        var targetConnectionStrings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in targets ?? [])
        {
            var targetConnectionString =
                $"Data Source=FlirtyMcpTarget-{target}-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
            targetConnectionStrings[target] = targetConnectionString;
            await OpenKeepAliveAsync(keepAlive, targetConnectionString);
        }

        var logs = new RecordingLoggerProvider();

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddLogging(logging => logging.AddProvider(logs));
        builder.Services.AddFlirty(options =>
        {
            options.UseSqlite(connectionString);
            configureFlirty?.Invoke(options);
        });

        var mcpBuilder = builder.Services.AddFlirtyMcp(options =>
        {
            foreach (var (name, target) in targetConnectionStrings)
            {
                options.AddTarget(name, FlirtyDatabaseProvider.Sqlite, target, $"Test target {name}.");
            }

            configureMcp?.Invoke(options);
        });

        if (includeThrowingTools)
        {
            mcpBuilder.WithTools<FlirtyThrowingTestTools>();
        }

        var app = builder.Build();
        app.MapFlirtyEndpoints("/flirty");
        app.MapFlirtyAdminEndpoints("/flirty/admin");
        app.MapFlirtyMcp("/mcp");
        app.MapFlirtyMcp("/mcp/{target}");
        await app.StartAsync();

        using (var scope = app.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FlirtyDbContext>();
            await context.Database.EnsureCreatedAsync();
            seed?.Invoke(context);
            await context.SaveChangesAsync();
        }

        if (prepareTargets)
        {
            foreach (var target in targetConnectionStrings.Values)
            {
                await using var context = CreateContext(target);
                await context.Database.MigrateAsync();
            }
        }

        var mcp = await ConnectAsync(app, "/mcp");

        return new FlirtyMcpTestHost(
            app, keepAlive, targetConnectionStrings, connectionString, mcp, logs);
    }

    /// <summary>
    /// A client connected to a named target's route, or to the plain <c>/mcp</c> when
    /// <paramref name="target"/> is <see langword="null"/>. Cached per route and disposed with the host.
    /// </summary>
    /// <remarks>
    /// Note that connecting to an <i>undeclared</i> target succeeds: the <c>initialize</c> handshake is not
    /// a <c>tools/call</c>, so the target filter never sees it. The rejection happens per tool call, which
    /// is exactly the behaviour the unknown-target tests assert.
    /// </remarks>
    /// <param name="target">The target name to put in the route, or <see langword="null"/> for <c>/mcp</c>.</param>
    /// <returns>The connected client.</returns>
    public async Task<McpClient> ConnectAsync(string? target = null)
    {
        var key = target ?? string.Empty;
        if (_clients.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var client = await ConnectAsync(_app, target is null ? "/mcp" : $"/mcp/{target}");
        _clients[key] = client;
        return client;
    }

    /// <summary>
    /// A fresh context on a declared target's database, bypassing every surface – the second
    /// <see cref="FlirtyDbContext"/> the write-isolation assertions look through. The caller disposes it.
    /// </summary>
    /// <param name="target">The declared target name.</param>
    /// <returns>A context bound to that target's database.</returns>
    public FlirtyDbContext OpenTargetContext(string target) =>
        CreateContext(_targetConnectionStrings[target]);

    /// <summary>A fresh context on the host's own database, bypassing every surface.</summary>
    /// <returns>A context bound to the <c>AddFlirty</c> database.</returns>
    public FlirtyDbContext OpenHostContext() => CreateContext(HostConnectionString);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        foreach (var client in _clients.Values)
        {
            await client.DisposeAsync();
        }

        Client.Dispose();
        await _app.DisposeAsync();

        foreach (var connection in _keepAlive)
        {
            await connection.DisposeAsync();
        }
    }

    private static async Task OpenKeepAliveAsync(List<SqliteConnection> keepAlive, string connectionString)
    {
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        keepAlive.Add(connection);
    }

    private static FlirtyDbContext CreateContext(string connectionString)
    {
        var builder = new DbContextOptionsBuilder<FlirtyDbContext>();
        builder.UseFlirtyProvider(FlirtyDatabaseProvider.Sqlite, connectionString);
        return new FlirtyDbContext(builder.Options);
    }

    private static async Task<McpClient> ConnectAsync(WebApplication app, string route)
    {
        // StreamableHttp explicitly instead of AutoDetect: the server is stateless, so it maps no GET
        // endpoint, and the auto-detect probe would waste a round trip discovering that.
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri($"http://localhost{route}"),
                TransportMode = HttpTransportMode.StreamableHttp,
            },
            app.GetTestClient());

        return await McpClient.CreateAsync(transport);
    }
}
