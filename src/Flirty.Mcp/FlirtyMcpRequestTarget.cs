using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace Flirty.Mcp;

/// <summary>
/// The database target of the current request – scoped, and the single place the route value
/// <c>{target}</c> is turned into a <see cref="FlirtyMcpTarget"/>.
/// </summary>
/// <remarks>
/// <para>
/// It is filled by the <c>ConfigureSessionOptions</c> callback that <c>AddFlirtyMcp</c> installs, which
/// in stateless mode the SDK invokes on <b>every HTTP request</b> from the ASP.NET request scope. That
/// is what makes the whole arrangement safe for the rest of the host: the callback fires only on an MCP
/// request, so a <c>MapFlirtyEndpoints</c> request never captures, <see cref="Resolve"/> answers
/// <see langword="null"/>, and the host's own <c>DbContextOptions</c> win. Declaring an MCP target
/// therefore cannot repoint the HTTP endpoints, the migration hosted service or any background work –
/// structurally, not by a check someone has to remember.
/// </para>
/// <para>
/// The captured value is the route <i>string</i>, not the <see cref="HttpContext"/>: nothing here
/// outlives the request, and a stored context would be a trap for anything that later runs after the
/// response has completed.
/// </para>
/// </remarks>
internal sealed class FlirtyMcpRequestTarget
{
    /// <summary>
    /// The route parameter that names the target. <c>MapFlirtyMcp</c> rejects a pattern whose parameter
    /// is called anything else, because the mismatch has no runtime symptom – it would silently serve the
    /// default database while the client believes it selected a target.
    /// </summary>
    internal const string RouteValueName = "target";

    private readonly FlirtyMcpTargetRegistry _registry;

    private bool _captured;
    private string? _requestedName;
    private bool _resolved;
    private FlirtyMcpTarget? _target;

    /// <summary>Creates the per-request holder.</summary>
    /// <param name="registry">The host's declared targets.</param>
    internal FlirtyMcpRequestTarget(FlirtyMcpTargetRegistry registry)
    {
        _registry = registry;
    }

    /// <summary>
    /// Records that this request is an MCP request and which target it names. Called once per HTTP
    /// request from the transport's session callback.
    /// </summary>
    /// <param name="context">The request that initiated the MCP server context.</param>
    internal void Capture(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _requestedName = context.Request.RouteValues.TryGetValue(RouteValueName, out var value)
            ? value as string
            : null;
        _captured = true;
        _resolved = false;
    }

    /// <summary>
    /// The target this request works against, or <see langword="null"/> for the database the host
    /// registered with <c>AddFlirty(...)</c>.
    /// </summary>
    /// <returns>The selected target, or <see langword="null"/> for the host's own database.</returns>
    /// <exception cref="ValidationException">
    /// The route named a target the host did not declare. Mapped to <c>400 Invalid request</c> by
    /// <see cref="FlirtyMcpExceptionFilter"/>, and the message enumerates the declared names – the error
    /// <i>is</i> the list, so a client that guessed wrong is never stranded.
    /// </exception>
    internal FlirtyMcpTarget? Resolve()
    {
        if (_resolved)
        {
            return _target;
        }

        _target = ResolveCore();
        _resolved = true;
        return _target;
    }

    private FlirtyMcpTarget? ResolveCore()
    {
        // Not an MCP request at all: an HTTP endpoint, the migration hosted service, a background job.
        if (!_captured)
        {
            return null;
        }

        // An MCP request on a route without a {target} segment. Without a declared default that is the
        // host's own database, which is a perfectly good arrangement: /mcp is the host's, /mcp/{target}
        // the declared extras.
        if (string.IsNullOrWhiteSpace(_requestedName))
        {
            return _registry.DefaultTarget;
        }

        if (_registry.TryGet(_requestedName, out var target))
        {
            return target;
        }

        throw new ValidationException(_registry.HasTargets
            ? $"The MCP database target '{_requestedName}' is unknown. Declared targets: "
                + $"{string.Join(", ", _registry.Names)}."
            : $"The MCP database target '{_requestedName}' cannot be served: this server declares no "
                + "database targets and works on the database its host registered with AddFlirty(...). "
                + "Connect to the route without a target segment.");
    }
}
