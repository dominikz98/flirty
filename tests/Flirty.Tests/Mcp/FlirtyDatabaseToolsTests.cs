using Flirty.Mcp;
using Flirty.Mcp.Tools;
using Flirty.Persistence;
using Flirty.Runtime.Admin;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Client;

namespace Flirty.Tests.Mcp;

/// <summary>
/// The database targets of <c>Flirty.Mcp</c> (#129): that a client connected to <c>/mcp/{target}</c>
/// really works against that database, that an unusable target name is rejected rather than silently
/// substituted, and that the four <c>flirty_db_*</c> tools report what they promise.
/// </summary>
/// <remarks>
/// <para>
/// The load-bearing assertion of the whole stage is the <b>counter-check</b>: not that a write reaches
/// target <c>a</c>, but that it reaches <i>neither</i> <c>b</c> <i>nor</i> the host's own database, read
/// through a second <c>FlirtyDbContext</c> that never touched the server. A test that only asserts the
/// positive half would stay green if the target were ignored entirely and everything went to the host
/// database.
/// </para>
/// <para>
/// Note what the in-memory databases here cannot show: the SQLite quirk that <c>CanConnect</c> only
/// succeeds once the file exists does not reproduce with <c>Mode=Memory</c> plus a keep-alive connection.
/// It is documented in the <c>flirty_db_test_connection</c> description, where the person hitting it is,
/// and it is not claimed to be covered.
/// </para>
/// </remarks>
public sealed class FlirtyDatabaseToolsTests
{
    /// <summary>
    /// A write over <c>/mcp/a</c> lands in <c>a</c> and nowhere else – neither in the sibling target nor in
    /// the database the host registered with <c>AddFlirty</c>.
    /// </summary>
    [Fact]
    public async Task A_write_through_a_target_route_lands_only_in_that_target()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync(targets: ["a", "b"]);
        var client = await host.ConnectAsync("a");

        var created = await CreateDialogAsync(client, "isolated");

        await using (var target = host.OpenTargetContext("a"))
        {
            Assert.Single(await target.Dialogs.Where(dialog => dialog.Id == created.Id).ToListAsync());
        }

        await using (var sibling = host.OpenTargetContext("b"))
        {
            Assert.Empty(await sibling.Dialogs.ToListAsync());
        }

        await using var hostDatabase = host.OpenHostContext();
        Assert.Empty(await hostDatabase.Dialogs.ToListAsync());
    }

    /// <summary>
    /// The route decides per connection, not once per server: two clients on two routes write into two
    /// databases in the same process.
    /// </summary>
    /// <remarks>
    /// The single-route version of this test would pass on an implementation that resolves the target once
    /// and caches it in a singleton – which is exactly the mistake a stateful transport would force.
    /// </remarks>
    [Fact]
    public async Task Two_routes_in_one_server_write_into_two_databases()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync(targets: ["a", "b"]);

        await CreateDialogAsync(await host.ConnectAsync("a"), "only-in-a");
        await CreateDialogAsync(await host.ConnectAsync("b"), "only-in-b");

        await using var first = host.OpenTargetContext("a");
        await using var second = host.OpenTargetContext("b");
        Assert.Equal("only-in-a", (await first.Dialogs.SingleAsync()).Key);
        Assert.Equal("only-in-b", (await second.Dialogs.SingleAsync()).Key);
    }

    /// <summary>
    /// Without declared targets the tools use the database <c>AddFlirty(...)</c> registered – the
    /// single-database path is untouched by this stage.
    /// </summary>
    [Fact]
    public async Task Without_declared_targets_the_tools_use_the_database_AddFlirty_registered()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync();

        var created = await host.CreateDialogAsync("plain");

        await using var hostDatabase = host.OpenHostContext();
        Assert.Equal(created.Id, (await hostDatabase.Dialogs.SingleAsync()).Id);
    }

    /// <summary>
    /// Naming a target on a server that declares none is a validation error, not a silent fallback to the
    /// host's database.
    /// </summary>
    /// <remarks>
    /// This is the half that needs the filter. Resolving the target lazily, on the first
    /// <c>FlirtyDbContext</c>, would have nothing to resolve in here – with no targets declared the context
    /// registration is not replaced at all – and the call would quietly succeed against the host database
    /// while the client believed it had switched.
    /// </remarks>
    [Fact]
    public async Task Naming_a_target_on_a_single_database_server_is_a_validation_error()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync();
        var client = await host.ConnectAsync("staging");

        var result = await client.CallToolAsync(FlirtyToolNames.DialogList);

        var problem = FlirtyMcpExceptionParityTests.ReadProblem(result);
        Assert.True(result.IsError);
        Assert.Equal(400, problem.Status);
        Assert.Equal("Invalid request", problem.Title);
        Assert.Contains("declares no database targets", problem.Detail, StringComparison.Ordinal);

        await using var hostDatabase = host.OpenHostContext();
        Assert.Empty(await hostDatabase.Dialogs.ToListAsync());
    }

    /// <summary>
    /// An unknown target is a validation error whose message enumerates the declared names – the error is
    /// the list, so a client that guessed wrong is not stranded.
    /// </summary>
    [Fact]
    public async Task An_unknown_target_reports_validation_and_lists_the_declared_names()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync(targets: ["alpha", "beta"]);
        var client = await host.ConnectAsync("gamma");

        var result = await client.CallToolAsync(FlirtyToolNames.DialogList);

        var problem = FlirtyMcpExceptionParityTests.ReadProblem(result);
        Assert.True(result.IsError);
        Assert.Equal(400, problem.Status);
        Assert.Equal("Invalid request", problem.Title);
        Assert.Contains("'gamma' is unknown", problem.Detail, StringComparison.Ordinal);
        Assert.Contains("alpha", problem.Detail, StringComparison.Ordinal);
        Assert.Contains("beta", problem.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// The rejection covers <c>flirty_db_list_targets</c> too, and that is deliberate rather than an
    /// oversight: the message already carries what that tool would have answered.
    /// </summary>
    [Fact]
    public async Task An_unknown_target_is_rejected_for_the_target_listing_as_well()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync(targets: ["alpha"]);
        var client = await host.ConnectAsync("gamma");

        var result = await client.CallToolAsync(FlirtyToolNames.DatabaseListTargets);

        Assert.True(result.IsError);
        Assert.Contains("alpha", FlirtyMcpExceptionParityTests.ReadProblem(result).Detail, StringComparison.Ordinal);
    }

    /// <summary>A target name is matched case-insensitively, because route values are.</summary>
    [Fact]
    public async Task A_target_name_is_matched_case_insensitively()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync(targets: ["staging"]);
        var client = await host.ConnectAsync("STAGING");

        await CreateDialogAsync(client, "mixed-case");

        await using var target = host.OpenTargetContext("staging");
        Assert.Equal("mixed-case", (await target.Dialogs.SingleAsync()).Key);
    }

    /// <summary>
    /// A route without a target segment serves the declared default target rather than the host's database.
    /// </summary>
    [Fact]
    public async Task The_plain_route_serves_the_declared_default_target()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync(
            configureMcp: options => options.UseDefaultTarget("primary"),
            targets: ["primary", "secondary"]);

        await host.CreateDialogAsync("defaulted");

        await using (var target = host.OpenTargetContext("primary"))
        {
            Assert.Equal("defaulted", (await target.Dialogs.SingleAsync()).Key);
        }

        await using var hostDatabase = host.OpenHostContext();
        Assert.Empty(await hostDatabase.Dialogs.ToListAsync());
    }

    /// <summary>
    /// With targets declared but no default, the plain route stays the host's own database – <c>/mcp</c> is
    /// the host's, <c>/mcp/{target}</c> the declared extras.
    /// </summary>
    [Fact]
    public async Task The_plain_route_stays_the_host_database_without_a_declared_default()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync(targets: ["extra"]);

        await host.CreateDialogAsync("on-the-host");

        await using (var hostDatabase = host.OpenHostContext())
        {
            Assert.Equal("on-the-host", (await hostDatabase.Dialogs.SingleAsync()).Key);
        }

        await using var target = host.OpenTargetContext("extra");
        Assert.Empty(await target.Dialogs.ToListAsync());
    }

    /// <summary>
    /// The target listing carries name, provider, description and the default marker – and no connection
    /// string, asserted against the <b>raw serialized text</b> rather than against the projection type.
    /// </summary>
    /// <remarks>
    /// The raw text is the point. Asserting on <c>FlirtyMcpTargetInfo</c>'s members would only restate its
    /// declaration; this reads what actually crossed the wire, so it would also catch a connection string
    /// arriving through some future nested member or a serializer setting nobody expected. It is the real
    /// guarantee behind "no connection string ever crosses the wire" – being <c>internal</c> is not, since
    /// <c>System.Text.Json</c> serializes internal types perfectly well.
    /// </remarks>
    [Fact]
    public async Task The_target_listing_reports_no_connection_string()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync(
            configureMcp: options => options.UseDefaultTarget("live"),
            targets: ["live", "spare"]);

        var result = await host.Mcp.CallToolAsync(FlirtyToolNames.DatabaseListTargets);

        var raw = result.StructuredContent!.Value.GetRawText();
        Assert.DoesNotContain("Data Source", raw, StringComparison.OrdinalIgnoreCase);
        foreach (var connectionString in host.TargetConnectionStrings)
        {
            Assert.DoesNotContain(connectionString, raw, StringComparison.Ordinal);
        }

        var targets = FlirtyMcpToolCalls.Read<FlirtyTargetList>(result);
        Assert.Null(targets.Note);
        Assert.Equal(["live", "spare"], targets.Targets.Select(target => target.Name));
        Assert.All(targets.Targets, target => Assert.Equal(FlirtyDatabaseProvider.Sqlite, target.Provider));
        Assert.Equal("live", Assert.Single(targets.Targets, target => target.IsDefault).Name);
    }

    /// <summary>
    /// On a single-database server the listing is empty <b>and</b> carries a note, so the emptiness does not
    /// read as a failure or as a permission problem.
    /// </summary>
    [Fact]
    public async Task The_target_listing_notes_the_single_database_case()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync();

        var targets = await host.CallAsync<FlirtyTargetList>(FlirtyToolNames.DatabaseListTargets);

        Assert.Empty(targets.Targets);
        Assert.NotNull(targets.Note);
        Assert.Contains("single database", targets.Note, StringComparison.Ordinal);
    }

    /// <summary>A reachable target answers the connection test.</summary>
    [Fact]
    public async Task TestConnection_succeeds_for_a_reachable_target()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync(targets: ["reachable"]);
        var client = await host.ConnectAsync("reachable");

        var result = await client.CallToolAsync(FlirtyToolNames.DatabaseTestConnection);

        var test = FlirtyMcpToolCalls.Read<FlirtyConnectionTest>(result);
        Assert.True(test.Succeeded);
    }

    /// <summary>
    /// An unreachable target is the connection test's <i>result</i>, not an error – "no" is the answer it
    /// was asked for, exactly as in the designer's <c>ConnectionProfileOperations.TestConnectionAsync</c>.
    /// </summary>
    [Fact]
    public async Task TestConnection_reports_an_unreachable_target_as_its_result()
    {
        await using var host = await StartWithUnreachableTargetAsync();
        var client = await host.ConnectAsync(OfflineTarget);

        var result = await client.CallToolAsync(FlirtyToolNames.DatabaseTestConnection);

        Assert.NotEqual(true, result.IsError);
        var test = FlirtyMcpToolCalls.Read<FlirtyConnectionTest>(result);
        Assert.False(test.Succeeded);
        Assert.Contains("Connection failed", test.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// An unreachable target makes <c>flirty_db_pending_migrations</c> report an error instead of throwing
    /// – the try/catch the designer's version never had.
    /// </summary>
    /// <remarks>
    /// Both halves are asserted. Without the try/catch the exception would still reach the client, but
    /// through the filter's catch-all: a 500 whose detail is the generic "An unexpected error occurred",
    /// with the provider's message only in the server log. The title is what separates the two.
    /// </remarks>
    [Fact]
    public async Task PendingMigrations_reports_a_database_error_for_an_unusable_target()
    {
        await using var broken = await BrokenTarget.StartAsync();
        var client = await broken.Host.ConnectAsync(OfflineTarget);

        var result = await client.CallToolAsync(FlirtyToolNames.DatabasePendingMigrations);

        var problem = FlirtyMcpExceptionParityTests.ReadProblem(result);
        Assert.True(result.IsError);
        Assert.Equal(500, problem.Status);
        Assert.Equal("Database error", problem.Title);
        Assert.Contains("not a database", problem.Detail, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A target whose database does not exist <b>yet</b> is not an error: everything is pending, which is
    /// exactly what a caller asks before migrating.
    /// </summary>
    /// <remarks>
    /// Worth pinning next to the test above, because the two look alike and are not. EF answers "nothing
    /// applied" for a database it cannot find – no exception – so an error here would be wrong, and a test
    /// that only proved the failure path would leave the far more common case unclaimed.
    /// </remarks>
    [Fact]
    public async Task PendingMigrations_reports_everything_as_pending_for_a_database_that_does_not_exist_yet()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync(
            targets: ["fresh"], prepareTargets: false);
        var client = await host.ConnectAsync("fresh");

        var result = await client.CallToolAsync(FlirtyToolNames.DatabasePendingMigrations);

        Assert.NotEqual(true, result.IsError);
        Assert.NotEmpty(FlirtyMcpToolCalls.Read<FlirtyPendingMigrations>(result).Pending);
    }

    /// <summary>
    /// An unmigrated target reports its pending migrations, and <c>flirty_db_migrate</c> applies exactly
    /// those and leaves nothing pending.
    /// </summary>
    [Fact]
    public async Task Migrate_applies_the_pending_migrations_of_a_target()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync(
            configureMcp: options => options.AllowMigrations(),
            targets: ["fresh"],
            prepareTargets: false);
        var client = await host.ConnectAsync("fresh");

        var pending = FlirtyMcpToolCalls.Read<FlirtyPendingMigrations>(
            await client.CallToolAsync(FlirtyToolNames.DatabasePendingMigrations));
        Assert.NotEmpty(pending.Pending);

        var applied = FlirtyMcpToolCalls.Read<FlirtyMigrationsApplied>(
            await client.CallToolAsync(FlirtyToolNames.DatabaseMigrate));

        Assert.Equal(pending.Pending, applied.Applied);
        Assert.Empty(FlirtyMcpToolCalls.Read<FlirtyPendingMigrations>(
            await client.CallToolAsync(FlirtyToolNames.DatabasePendingMigrations)).Pending);

        // The schema really exists afterwards, not just the history table.
        await CreateDialogAsync(client, "after-migration");
    }

    /// <summary>Migrating twice is idempotent: the second call finds nothing pending and applies nothing.</summary>
    [Fact]
    public async Task Migrate_applies_nothing_on_an_already_migrated_target()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync(
            configureMcp: options => options.AllowMigrations(),
            targets: ["current"]);
        var client = await host.ConnectAsync("current");

        var applied = FlirtyMcpToolCalls.Read<FlirtyMigrationsApplied>(
            await client.CallToolAsync(FlirtyToolNames.DatabaseMigrate));

        Assert.Empty(applied.Applied);
    }

    /// <summary>
    /// Without <c>AllowMigrations()</c> the migrate tool is <b>absent</b> from <c>tools/list</c> – gated by
    /// absence, not by a tool that exists and refuses.
    /// </summary>
    [Fact]
    public async Task Migrate_is_absent_from_the_tool_list_without_AllowMigrations()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync();

        var tools = (await host.Mcp.ListToolsAsync()).Select(tool => tool.Name).ToList();

        Assert.DoesNotContain(FlirtyToolNames.DatabaseMigrate, tools);
        Assert.Contains(FlirtyToolNames.DatabasePendingMigrations, tools);
    }

    /// <summary>The other direction of the same gate: with the flag the tool is served.</summary>
    [Fact]
    public async Task Migrate_appears_in_the_tool_list_with_AllowMigrations()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync(
            configureMcp: options => options.AllowMigrations());

        var tools = (await host.Mcp.ListToolsAsync()).Select(tool => tool.Name).ToList();

        Assert.Contains(FlirtyToolNames.DatabaseMigrate, tools);
    }

    /// <summary>
    /// A missing <c>Flirty.Migrations.*</c> assembly is translated into a message that names it and says
    /// what to do about it.
    /// </summary>
    /// <remarks>
    /// A unit test rather than an integration one, and that is not a shortcut: <c>Flirty.Tests.csproj</c>
    /// references all three migration projects, so the failure cannot be provoked in this process at all.
    /// The <c>FileName</c> is the assembly <i>display</i> name, which is why the translation splits on the
    /// comma instead of reading the property as a simple name.
    /// </remarks>
    [Fact]
    public void Describe_names_the_missing_migrations_assembly()
    {
        var failure = new FileNotFoundException(
            "Could not load file or assembly 'Flirty.Migrations.PostgreSql'.",
            "Flirty.Migrations.PostgreSql, Culture=neutral, PublicKeyToken=null");

        var description = FlirtyMcpDatabaseException.Describe(failure);

        Assert.Contains("'Flirty.Migrations.PostgreSql'", description, StringComparison.Ordinal);
        Assert.Contains("Flirty NuGet package", description, StringComparison.Ordinal);
    }

    /// <summary>An unrelated failure keeps its own message.</summary>
    [Fact]
    public void Describe_passes_an_unrelated_failure_through()
    {
        Assert.Equal(
            "unable to open database file",
            FlirtyMcpDatabaseException.Describe(new InvalidOperationException("unable to open database file")));
    }

    /// <summary>The translation also finds the failure nested inside a wrapping exception.</summary>
    [Fact]
    public void Describe_finds_the_missing_assembly_in_an_inner_exception()
    {
        var description = FlirtyMcpDatabaseException.Describe(new InvalidOperationException(
            "The migrations assembly could not be resolved.",
            new FileNotFoundException("nope", "Flirty.Migrations.SqlServer, Culture=neutral")));

        Assert.Contains("'Flirty.Migrations.SqlServer'", description, StringComparison.Ordinal);
    }

    /// <summary>The name of the deliberately broken target used by two of the tests above.</summary>
    private const string OfflineTarget = "offline";

    /// <summary>
    /// A host with one target pointing at a SQLite file that does not exist, opened read-only so the
    /// provider cannot create it – the connection genuinely fails.
    /// </summary>
    /// <remarks>
    /// Deterministic and instant, unlike a dead PostgreSQL host, which would spend its connect timeout on
    /// every run. Declared through <c>configureMcp</c> rather than through the host's <c>targets</c> list,
    /// because the latter migrates what it declares and this one is meant to stay unusable.
    /// </remarks>
    private static Task<FlirtyMcpTestHost> StartWithUnreachableTargetAsync()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"flirty-mcp-missing-{Guid.NewGuid():N}.db");

        return FlirtyMcpTestHost.StartAsync(configureMcp: options => options.AddTarget(
            OfflineTarget, FlirtyDatabaseProvider.Sqlite, $"Data Source={missing};Mode=ReadOnly"));
    }

    /// <summary>
    /// A host whose one target points at a real file that is not a SQLite database, plus the cleanup of
    /// that file.
    /// </summary>
    /// <remarks>
    /// A <i>missing</i> database would not do here, and that is the finding worth keeping: EF answers
    /// "nothing applied yet" for a database it cannot find, so <c>GetPendingMigrationsAsync</c> succeeds
    /// and reports every migration as pending. Only content it cannot read is a genuine failure – hence a
    /// real file with garbage in it rather than a path pointing nowhere.
    /// </remarks>
    private sealed class BrokenTarget : IAsyncDisposable
    {
        private readonly string _path;

        private BrokenTarget(FlirtyMcpTestHost host, string path)
        {
            Host = host;
            _path = path;
        }

        /// <summary>The started host.</summary>
        public FlirtyMcpTestHost Host { get; }

        /// <summary>Writes the unreadable file and starts a host declaring it as the target.</summary>
        /// <returns>The started host together with its cleanup.</returns>
        public static async Task<BrokenTarget> StartAsync()
        {
            var path = Path.Combine(Path.GetTempPath(), $"flirty-mcp-broken-{Guid.NewGuid():N}.db");
            await File.WriteAllTextAsync(path, "This file exists and is not a SQLite database.");

            // Pooling=False so the file is really released when the failed connection closes; pooled, it
            // survives the host's disposal and the cleanup below fails with a sharing violation.
            var host = await FlirtyMcpTestHost.StartAsync(configureMcp: options => options.AddTarget(
                OfflineTarget, FlirtyDatabaseProvider.Sqlite, $"Data Source={path};Pooling=False"));

            return new BrokenTarget(host, path);
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            await Host.DisposeAsync();
            File.Delete(_path);
        }
    }

    private static async Task<DialogSummary> CreateDialogAsync(McpClient client, string key)
        => FlirtyMcpToolCalls.Read<DialogSummary>(await client.CallToolAsync(
            FlirtyToolNames.DialogCreate,
            new Dictionary<string, object?> { ["key"] = key, ["name"] = key }));
}
