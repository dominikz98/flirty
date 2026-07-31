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
    /// The dialog runtime: starting, resuming, submitting and editing answers. Filled in a later
    /// build-out stage; today this flag registers no tools.
    /// </summary>
    Runtime = 1,

    /// <summary>
    /// The dialog configuration: dialogs, questions, answer options, transitions, loop markers, triggers
    /// and the canvas layout – the whole configuration graph.
    /// </summary>
    Admin = 2,

    /// <summary>Both surfaces – the default.</summary>
    All = Runtime | Admin,
}

/// <summary>
/// Configuration of the Flirty MCP server, passed to
/// <c>services.AddFlirtyMcp(...)</c>.
/// </summary>
public sealed class FlirtyMcpOptions
{
    /// <summary>
    /// The tool surfaces to register. Default: <see cref="FlirtyMcpSurface.All"/>.
    /// </summary>
    public FlirtyMcpSurface Surface { get; set; } = FlirtyMcpSurface.All;

    /// <summary>
    /// The server name reported to the client in the MCP server info. Default: <c>"Flirty"</c>.
    /// </summary>
    public string ServerName { get; set; } = "Flirty";
}
