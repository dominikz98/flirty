using Flirty.Persistence;

namespace Flirty.Mcp;

/// <summary>
/// The tool surfaces the Flirty MCP server can expose. A host that only wants a test-run client
/// registers <see cref="Runtime"/> instead of all tools.
/// </summary>
[Flags]
public enum FlirtyMcpSurface
{
    /// <summary>No tools at all. Useful for a host that adds only its own tools to the server.</summary>
    None = 0,

    /// <summary>
    /// The dialog runtime: starting, resuming, submitting and editing answers – the five
    /// <c>flirty_session_*</c> tools. Note that these run dialogs for real: they write sessions and
    /// deliver configured webhook triggers, and one of them starts an unpublished draft. A host that
    /// wants configuration only registers <see cref="Admin"/>.
    /// </summary>
    Runtime = 1,

    /// <summary>
    /// The dialog configuration: dialogs, questions, answer options, transitions, loop markers, triggers
    /// and the canvas layout – the whole configuration graph.
    /// </summary>
    Admin = 2,

    /// <summary>
    /// The database targets: listing them, testing a connection and reading the pending migrations – the
    /// <c>flirty_db_*</c> tools. Applying a migration is gated once more, by
    /// <see cref="FlirtyMcpOptions.AllowMigrations"/>, and is off even when this flag is set.
    /// </summary>
    Database = 4,

    /// <summary>All three surfaces – the default.</summary>
    All = Runtime | Admin | Database,
}

/// <summary>
/// Configuration of the Flirty MCP server, passed to
/// <c>services.AddFlirtyMcp(...)</c>.
/// </summary>
/// <remarks>
/// <para>
/// Beyond the two knobs below, this is where a host declares the <b>database targets</b> a client may
/// work against. The authority is deliberately the host's: a client picks a declared target by
/// <b>name in the route</b> (<c>/mcp/{target}</c>), never by passing a connection string. See ADR 0010.
/// </para>
/// <para>
/// Declaring no target at all is the normal single-database case and changes nothing: the tools then
/// use whatever database <c>AddFlirty(...)</c> registered.
/// </para>
/// </remarks>
public sealed class FlirtyMcpOptions
{
    /// <summary>
    /// The declared targets by name. Case-insensitive, because <c>RouteValueDictionary</c> is – so
    /// <c>/mcp/Staging</c> must find the target declared as <c>staging</c>.
    /// </summary>
    private readonly Dictionary<string, FlirtyMcpTarget> _targets =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The tool surfaces to register. Default: <see cref="FlirtyMcpSurface.All"/>.
    /// </summary>
    public FlirtyMcpSurface Surface { get; set; } = FlirtyMcpSurface.All;

    /// <summary>
    /// The server name reported to the client in the MCP server info. Default: <c>"Flirty"</c>.
    /// </summary>
    public string ServerName { get; set; } = "Flirty";

    /// <summary>The declared database targets, keyed case-insensitively by name.</summary>
    internal IReadOnlyDictionary<string, FlirtyMcpTarget> Targets => _targets;

    /// <summary>
    /// The target served on a route without a <c>{target}</c> segment, or <see langword="null"/> when the
    /// host's own <c>AddFlirty(...)</c> database is served there.
    /// </summary>
    internal string? DefaultTargetName { get; private set; }

    /// <summary>Whether <c>flirty_db_migrate</c> is registered at all.</summary>
    internal bool MigrationsAllowed { get; private set; }

    /// <summary>
    /// Declares a database target a client can select by naming it in the route
    /// (<c>app.MapFlirtyMcp("/mcp/{target}")</c>).
    /// </summary>
    /// <remarks>
    /// The connection string stays on the server: it is never a tool argument, never part of a tool
    /// result, and <c>flirty_db_list_targets</c> reports only name, provider, description and whether the
    /// target is the default one.
    /// </remarks>
    /// <param name="name">
    /// The name a client uses in the route. Must consist of ASCII letters, digits, <c>.</c>, <c>_</c> or
    /// <c>-</c> only – anything else could not be matched by a route segment and would leave the target
    /// unreachable without any diagnostic.
    /// </param>
    /// <param name="provider">The database provider of the target.</param>
    /// <param name="connectionString">The connection string of the target.</param>
    /// <param name="description">
    /// An optional description shown to the client by <c>flirty_db_list_targets</c>, e.g. "nightly
    /// restore of production".
    /// </param>
    /// <returns>The same options instance, so calls chain.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is empty, contains a character other than the ones listed above, or has
    /// already been declared; or <paramref name="connectionString"/> is empty.
    /// </exception>
    public FlirtyMcpOptions AddTarget(
        string name,
        FlirtyDatabaseProvider provider,
        string connectionString,
        string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        if (!IsRoutable(name))
        {
            throw new ArgumentException(
                $"The MCP database target name '{name}' cannot appear in a route segment. Use ASCII "
                + "letters, digits, '.', '_' or '-' only.",
                nameof(name));
        }

        if (!_targets.TryAdd(name, new FlirtyMcpTarget(name, provider, connectionString, description)))
        {
            throw new ArgumentException(
                $"The MCP database target '{name}' is already declared. Target names are compared "
                + "case-insensitively, because route values are.",
                nameof(name));
        }

        return this;
    }

    /// <summary>
    /// Names the target served on a route <b>without</b> a <c>{target}</c> segment, i.e. on a plain
    /// <c>app.MapFlirtyMcp("/mcp")</c>.
    /// </summary>
    /// <remarks>
    /// Without this call such a route serves the database the host registered with
    /// <c>AddFlirty(...)</c>, which is a perfectly good arrangement: <c>/mcp</c> is then the host's own
    /// database and <c>/mcp/{target}</c> the declared extras. The name is cross-checked against
    /// <see cref="AddTarget"/> in <c>AddFlirtyMcp</c> and not here, because the two may be called in
    /// either order inside the configuration lambda.
    /// </remarks>
    /// <param name="name">The name of a target declared with <see cref="AddTarget"/>.</param>
    /// <returns>The same options instance, so calls chain.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
    public FlirtyMcpOptions UseDefaultTarget(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        DefaultTargetName = name;
        return this;
    }

    /// <summary>
    /// Registers <c>flirty_db_migrate</c>, which applies the pending EF Core migrations to the selected
    /// target. Off by default.
    /// </summary>
    /// <remarks>
    /// The gate works by <b>absence</b>: without this call the tool is not registered, so it does not
    /// appear in <c>tools/list</c> at all. That is deliberately not the same as a tool that exists and
    /// always refuses – a model reasons better about a capability that is simply not there, and an
    /// invisible tool is the stronger security posture. Applying a migration is irreversible, so a host
    /// should pair this with <c>RequireAuthorization()</c> on the route.
    /// </remarks>
    /// <returns>The same options instance, so calls chain.</returns>
    public FlirtyMcpOptions AllowMigrations()
    {
        MigrationsAllowed = true;
        return this;
    }

    /// <summary>
    /// Indicates whether the name can survive a round trip through a route segment unescaped.
    /// </summary>
    private static bool IsRoutable(string name)
        => name.All(character
            => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-');
}
