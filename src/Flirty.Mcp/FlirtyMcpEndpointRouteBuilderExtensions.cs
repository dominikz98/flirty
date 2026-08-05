using Flirty.Mcp;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore;

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
    /// connecting there can list and call the Flirty tools – the dialog configuration graph, the dialog
    /// runtime and the database targets.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The prerequisite is a Flirty stack registered via <c>services.AddFlirty(...)</c> <b>and</b> an MCP
    /// server registered via <c>services.AddFlirtyMcp(...)</c>. Exceptions thrown by the engine are mapped
    /// uniformly onto an error result with the same status and title as the HTTP endpoints produce (404 for
    /// unknown elements, 400 for invalid requests and answers, 409 for key and state conflicts). Since the
    /// tools include write operations, securing them via <c>RequireAuthorization()</c> on the returned
    /// builder is recommended.
    /// </para>
    /// <para>
    /// <b>Selecting a database target.</b> A host that declared targets with
    /// <c>FlirtyMcpOptions.AddTarget</c> maps a second route carrying the parameter <c>{target}</c>; the
    /// client then picks a database by connecting to <c>/mcp/staging</c> rather than by passing an
    /// argument. Each mapped route is its own endpoint and needs its own <c>RequireAuthorization()</c>.
    /// The pattern may contain <b>at most one</b> route parameter and it must be called <c>target</c> –
    /// any other name would be ignored by the resolution and the client would silently work against the
    /// default database, which is why that is rejected here rather than at request time.
    /// </para>
    /// </remarks>
    /// <param name="endpoints">The endpoint router of the host app (e.g. the <see cref="WebApplication"/>).</param>
    /// <param name="pattern">
    /// The route pattern under which the MCP endpoint is mapped (default: <c>"/mcp"</c>). Either without a
    /// route parameter (serving the default target) or with exactly <c>{target}</c>.
    /// </param>
    /// <returns>
    /// The <see cref="IEndpointConventionBuilder"/> created by the SDK, to configure the endpoint further
    /// (e.g. <c>RequireAuthorization()</c>).
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="endpoints"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="pattern"/> is <see langword="null"/>, empty, only whitespace, or contains a route
    /// parameter other than a single <c>{target}</c>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// No MCP server with an HTTP transport is registered – <c>services.AddFlirtyMcp(...)</c> was not
    /// called – or database targets are declared while the transport is not stateless.
    /// </exception>
    /// <example>
    /// <code>
    /// app.MapFlirtyEndpoints("/flirty");
    /// app.MapFlirtyAdminEndpoints("/flirty/admin").RequireAuthorization();
    /// app.MapFlirtyMcp("/mcp").RequireAuthorization();           // the default target
    /// app.MapFlirtyMcp("/mcp/{target}").RequireAuthorization();  // a named target
    /// </code>
    /// </example>
    public static IEndpointConventionBuilder MapFlirtyMcp(
        this IEndpointRouteBuilder endpoints, string pattern = "/mcp")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);

        GuardRouteParameters(pattern);
        GuardStatelessTransport(endpoints);

        // Returned unchanged, so RequireAuthorization() chains – the same promise MapFlirtyAdminEndpoints
        // makes about its RouteGroupBuilder.
        return endpoints.MapMcp(pattern);
    }

    /// <summary>
    /// Rejects a pattern whose route parameter the target resolution would not read.
    /// </summary>
    /// <remarks>
    /// Compared <see cref="StringComparison.OrdinalIgnoreCase"/>, because <c>RouteValueDictionary</c> is
    /// case-insensitive too – <c>{Target}</c> genuinely works and must not be refused.
    /// </remarks>
    private static void GuardRouteParameters(string pattern)
    {
        var parameters = RoutePatternFactory.Parse(pattern).Parameters;

        if (parameters.Count > 1
            || (parameters.Count == 1
                && !string.Equals(
                    parameters[0].Name,
                    FlirtyMcpRequestTarget.RouteValueName,
                    StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException(
                $"The route pattern '{pattern}' may contain at most one route parameter, and it must be "
                + $"'{{{FlirtyMcpRequestTarget.RouteValueName}}}'. Any other parameter is ignored when the "
                + "database target is resolved, so the client would work against the default database "
                + "without noticing.",
                nameof(pattern));
        }
    }

    /// <summary>
    /// Rejects declared database targets on a stateful transport.
    /// </summary>
    /// <remarks>
    /// The target is captured in the transport's session callback, which the SDK invokes per HTTP request
    /// <b>only in stateless mode</b>. Stateful, it fires once per session on a scope that is long gone by
    /// the time a tool runs, so every target would quietly fall back to the host's database. A startup
    /// failure is the only honest answer to that.
    /// </remarks>
    private static void GuardStatelessTransport(IEndpointRouteBuilder endpoints)
    {
        // GetService: without AddFlirtyMcp there is no registry, and that case belongs to MapMcp's own
        // "you must call WithHttpTransport()" message rather than to this guard.
        if (endpoints.ServiceProvider.GetService<FlirtyMcpTargetRegistry>() is not { HasTargets: true })
        {
            return;
        }

        var transport = endpoints.ServiceProvider
            .GetRequiredService<IOptions<HttpServerTransportOptions>>().Value;

        if (!transport.Stateless)
        {
            throw new InvalidOperationException(
                "Flirty MCP database targets require the stateless Streamable HTTP transport. The target "
                + "is read from the route on every request, which a stateful transport cannot do – it "
                + "would serve the host's own database instead of the selected target.");
        }
    }
}
