using Microsoft.AspNetCore.Routing;

namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// Provides the extension method <see cref="MapFlirtyMcp"/>, which maps the Flirty Model Context Protocol
/// server onto a route. The namespace <c>Microsoft.AspNetCore.Builder</c> is chosen deliberately so that
/// the method is discoverable on an <see cref="IEndpointRouteBuilder"/> without an additional
/// <c>using</c> – the same trick as <c>MapFlirtyEndpoints</c>.
/// </summary>
public static class FlirtyMcpEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps the Flirty MCP server (Streamable HTTP) under the given <paramref name="pattern"/>. A client
    /// connecting there can list and call the Flirty tools – dialog configuration today, the dialog runtime
    /// and the remaining graph tools in the following build-out stages.
    /// </summary>
    /// <remarks>
    /// The prerequisite is a Flirty stack registered via <c>services.AddFlirty(...)</c> <b>and</b> an MCP
    /// server registered via <c>services.AddFlirtyMcp(...)</c>. Exceptions thrown by the engine are mapped
    /// uniformly onto an error result with the same status and title as the HTTP endpoints produce (404 for
    /// unknown elements, 400 for invalid requests and answers, 409 for key and state conflicts). Since the
    /// tools include write operations, securing them via <c>RequireAuthorization()</c> on the returned
    /// builder is recommended.
    /// </remarks>
    /// <param name="endpoints">The endpoint router of the host app (e.g. the <see cref="WebApplication"/>).</param>
    /// <param name="pattern">
    /// The route pattern under which the MCP endpoint is mapped (default: <c>"/mcp"</c>).
    /// </param>
    /// <returns>
    /// The <see cref="IEndpointConventionBuilder"/> created by the SDK, to configure the endpoint further
    /// (e.g. <c>RequireAuthorization()</c>).
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="endpoints"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="pattern"/> is <see langword="null"/>, empty or only whitespace.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// No MCP server with an HTTP transport is registered – <c>services.AddFlirtyMcp(...)</c> was not called.
    /// </exception>
    /// <example>
    /// <code>
    /// app.MapFlirtyEndpoints("/flirty");
    /// app.MapFlirtyAdminEndpoints("/flirty/admin").RequireAuthorization();
    /// app.MapFlirtyMcp("/mcp").RequireAuthorization();
    /// </code>
    /// </example>
    public static IEndpointConventionBuilder MapFlirtyMcp(
        this IEndpointRouteBuilder endpoints, string pattern = "/mcp")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);

        // Returned unchanged, so RequireAuthorization() chains – the same promise MapFlirtyAdminEndpoints
        // makes about its RouteGroupBuilder.
        return endpoints.MapMcp(pattern);
    }
}
