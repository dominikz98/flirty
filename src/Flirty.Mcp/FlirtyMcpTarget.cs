using Flirty.Persistence;

namespace Flirty.Mcp;

/// <summary>
/// A database target declared by the host with <see cref="FlirtyMcpOptions.AddTarget"/>: the name a
/// client selects in the route, plus everything needed to open that database.
/// </summary>
/// <remarks>
/// <para>
/// This is the one type in the package that <b>holds a connection string</b>, and the rule around it is
/// simple: it never appears in a tool method's signature – neither as a parameter nor as a return type.
/// The wire projection is <see cref="FlirtyMcpTargetInfo"/>, which has no member that holds or nests a
/// connection string.
/// </para>
/// <para>
/// Being <c>internal</c> is <b>not</b> what keeps it off the wire, and it is worth saying so plainly
/// because the opposite is easy to assume: <c>System.Text.Json</c> ignores a type's accessibility and
/// serializes its public members happily – every result wrapper in <c>FlirtyToolResults.cs</c> is
/// <c>internal</c> and reaches the client in full. The guarantee is the two facts above plus the test
/// that reads the raw serialized text of <c>flirty_db_list_targets</c>.
/// </para>
/// </remarks>
/// <param name="Name">The declared name, in the host's spelling. Route lookup is case-insensitive.</param>
/// <param name="Provider">The database provider.</param>
/// <param name="ConnectionString">The connection string. Server-local; never serialized.</param>
/// <param name="Description">An optional human-readable description for the client.</param>
internal sealed record FlirtyMcpTarget(
    string Name,
    FlirtyDatabaseProvider Provider,
    string ConnectionString,
    string? Description);
