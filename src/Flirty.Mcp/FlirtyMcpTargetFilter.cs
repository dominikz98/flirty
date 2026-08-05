using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Flirty.Mcp;

/// <summary>
/// Call-tool filter that resolves the request's database target once, before the tool runs, so a route
/// naming an undeclared target fails the same way for <b>every</b> tool.
/// </summary>
/// <remarks>
/// <para>
/// Registered as the <b>second</b> call-tool filter, so the SDK composes it <i>inside</i>
/// <see cref="FlirtyMcpExceptionFilter"/> and the <c>ValidationException</c> it lets through is mapped by
/// that filter's existing <c>400 Invalid request</c> branch. No new branch, no change to the load-bearing
/// catch order.
/// </para>
/// <para>
/// It exists because resolving lazily – on the first <c>FlirtyDbContext</c> – would leave a hole:
/// <c>flirty_db_list_targets</c> never touches a context, so <c>/mcp/typo</c> plus that tool would answer
/// happily while the client believes it is on <c>typo</c>. And in the single-database case the context
/// factory is not registered at all, so there would be nothing to resolve in – yet naming a target must
/// still be an error there, or a client would believe it switched database when it did not.
/// </para>
/// <para>
/// The consequence is intended: a bad target blocks every tool, <c>flirty_db_list_targets</c> included.
/// That is affordable precisely because the error message enumerates the declared names.
/// </para>
/// </remarks>
internal static class FlirtyMcpTargetFilter
{
    /// <summary>The single filter delegate.</summary>
    internal static McpRequestFilter<CallToolRequestParams, CallToolResult> Instance { get; } =
        next => (request, cancellationToken) =>
        {
            // GetService, not GetRequiredService: a stdio host or a hand-built server may have no services
            // at all, and the holder is registered only alongside the HTTP transport.
            request.Services?.GetService<FlirtyMcpRequestTarget>()?.Resolve();

            return next(request, cancellationToken);
        };
}
