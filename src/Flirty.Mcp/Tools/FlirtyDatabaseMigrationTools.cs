using System.ComponentModel;
using Flirty.Persistence;
using ModelContextProtocol.Server;

namespace Flirty.Mcp.Tools;

/// <summary>
/// The one gated tool: applying the pending EF Core migrations to the connected database target.
/// </summary>
/// <remarks>
/// <para>
/// The shape conventions of all ten tool classes are documented once, on <see cref="FlirtyDialogTools"/>;
/// what this area adds is on <see cref="FlirtyDatabaseTools"/>. This class exists separately from that
/// one for a single reason: it is registered <b>conditionally</b>, only when the host called
/// <c>FlirtyMcpOptions.AllowMigrations()</c>, and a class is the unit <c>WithTools&lt;T&gt;()</c> takes.
/// </para>
/// <para>
/// The gate is by <b>absence</b>. Without the flag the tool is not registered, so it never appears in
/// <c>tools/list</c> – rather than existing and refusing every call, which costs a model a round trip to
/// learn nothing and advertises a capability the server will not honour. It is also the stronger
/// security posture, and it is pinned by two tests, one with the flag off and one with it on.
/// </para>
/// </remarks>
[McpServerToolType]
internal sealed class FlirtyDatabaseMigrationTools
{
    // Destructive despite adding rather than removing: an applied migration cannot be taken back through
    // this surface, which is the same reason flirty_dialog_abandon_sessions is destructive without
    // deleting anything. Idempotent, though - a second call finds nothing pending and applies nothing.
    [McpServerTool(
        Name = FlirtyToolNames.DatabaseMigrate,
        UseStructuredContent = true,
        ReadOnly = false,
        Destructive = true,
        Idempotent = true,
        OpenWorld = false)]
    [Description("Applies the pending EF Core migrations to the database of the connected target and "
        + "reports which ones were applied; for SQLite this also creates the database file. Irreversible "
        + "through this server - there is no tool to roll a migration back. Available only because the "
        + "host enabled it; check flirty_db_pending_migrations first to see what this would do. Fails "
        + "with a Database error if the target cannot be reached or its Flirty.Migrations assembly is "
        + "missing.")]
    internal static async Task<FlirtyMigrationsApplied> MigrateAsync(
        FlirtyDbContext context, CancellationToken cancellationToken)
        => await FlirtyMcpDatabaseOperations.MigrateAsync(context, cancellationToken);
}
