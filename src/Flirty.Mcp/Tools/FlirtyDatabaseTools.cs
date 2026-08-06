using System.ComponentModel;
using Flirty.Persistence;
using ModelContextProtocol.Server;

namespace Flirty.Mcp.Tools;

/// <summary>
/// The database-level MCP tools: which targets the host declared, whether the selected one answers, and
/// what migrations it still needs. Applying them lives in <see cref="FlirtyDatabaseMigrationTools"/>.
/// </summary>
/// <remarks>
/// <para>
/// The shape conventions of all eleven tool classes are documented once, on <see cref="FlirtyDialogTools"/>,
/// and deliberately not repeated here. Four things are specific to this area.
/// </para>
/// <para>
/// <b>This is the only class with no <c>MapXxxEndpoints</c> counterpart.</b> The engine has no command
/// for "is this database reachable?" – the designer does these three through its connection profiles
/// (<c>ConnectionProfileOperations</c>), and that, not an HTTP route group, is what
/// <see cref="FlirtyMcpDatabaseOperations"/> is reviewable against. It is also why these tools inject a
/// <see cref="FlirtyDbContext"/> instead of an <c>ISender</c>: same mechanism, since both are
/// container-registered and therefore excluded from the input schema, but a different destination.
/// </para>
/// <para>
/// <b>No tool here takes a target.</b> The database is chosen by the <i>route</i> a client connects to
/// (<c>/mcp/staging</c>), which keeps a <c>target</c> parameter off all thirty-seven schemas and makes the
/// choice explicit at connect time. There is deliberately no <c>select_target</c> tool either: protocol
/// revision <c>2026-07-28</c> removed sessions from the wire, so there is nothing to hold a selection in,
/// and a select-then-edit pair behind a load balancer would edit the wrong database. See ADR 0010.
/// </para>
/// <para>
/// <b>Only <c>flirty_db_list_targets</c> works without a resolvable target</b> – or rather, it would:
/// the target filter rejects an undeclared name before any tool runs, this one included. That is
/// deliberate, because the rejection message enumerates the declared names, so a client that guessed
/// wrong learns the same thing it came here for.
/// </para>
/// <para>
/// <b>Errors split by whether the tool can still answer.</b> <c>flirty_db_test_connection</c> reports an
/// unreachable database as its <i>result</i>; <c>flirty_db_pending_migrations</c> reports it as an error.
/// The reasoning is on <see cref="FlirtyMcpDatabaseOperations"/>.
/// </para>
/// </remarks>
[McpServerToolType]
internal sealed class FlirtyDatabaseTools
{
    /// <summary>The note that keeps an empty list from reading as a failure or a permission problem.</summary>
    private const string SingleDatabaseNote =
        "This server declares no database targets and works on the single database its host configured. "
        + "Connect to the MCP route without a target segment.";

    // Read-only and idempotent: the declared targets are host configuration and do not change at runtime.
    [McpServerTool(
        Name = FlirtyToolNames.DatabaseListTargets,
        UseStructuredContent = true,
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description("Lists the database targets this server offers. Pick one by connecting to the MCP route "
        + "with the target name as the last path segment, for example /mcp/staging - there is no tool "
        + "argument and no tool to switch, because the choice is made per connection. The target marked "
        + "isDefault is the one served on the route without a target segment. An empty list means the "
        + "server works on a single database and the plain route is the only one. Connection strings are "
        + "never reported.")]
    internal static FlirtyTargetList ListTargets(FlirtyMcpTargetRegistry registry)
    {
        var targets = registry.Describe();

        return new FlirtyTargetList(targets, targets.Count == 0 ? SingleDatabaseNote : null);
    }

    // Read-only and idempotent: it opens a connection and closes it again. A failure is the answer, not
    // an error, so this tool never fails for an unreachable database.
    [McpServerTool(
        Name = FlirtyToolNames.DatabaseTestConnection,
        UseStructuredContent = true,
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description("Checks whether the database of the connected target answers, and reports the failure "
        + "as its result rather than as an error. SQLite note: this only reports success once the "
        + "database file exists, so a fresh SQLite target has to be migrated first and tested "
        + "afterwards, not the other way round.")]
    internal static async Task<FlirtyConnectionTest> TestConnectionAsync(
        FlirtyDbContext context, CancellationToken cancellationToken)
        => await FlirtyMcpDatabaseOperations.TestConnectionAsync(context, cancellationToken);

    // Read-only and idempotent: it compares the applied migrations with the ones the assembly carries.
    [McpServerTool(
        Name = FlirtyToolNames.DatabasePendingMigrations,
        UseStructuredContent = true,
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description("Lists the EF Core migrations that the database of the connected target still needs, in "
        + "the order they would be applied. An empty list means the schema is up to date. Fails with a "
        + "Database error if the target cannot be reached or its Flirty.Migrations assembly is missing.")]
    internal static async Task<FlirtyPendingMigrations> GetPendingMigrationsAsync(
        FlirtyDbContext context, CancellationToken cancellationToken)
        => await FlirtyMcpDatabaseOperations.GetPendingMigrationsAsync(context, cancellationToken);
}
