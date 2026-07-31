using Flirty.Mcp;
using Flirty.Mcp.Tools;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Provides the extension method <see cref="AddFlirtyMcp(IServiceCollection, Action{FlirtyMcpOptions}?)"/>,
/// which registers a Model Context Protocol server exposing the Flirty engine operations as MCP tools.
/// The tools are a thin layer over the Mediator commands (the core stays ASP.NET-free and
/// protocol-agnostic) and send them directly via <c>ISender</c>. The namespace
/// <c>Microsoft.Extensions.DependencyInjection</c> is chosen deliberately so that the method is
/// discoverable without an additional <c>using</c> – the same trick as <c>AddFlirty</c>.
/// </summary>
public static class FlirtyMcpServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Flirty MCP server together with its tools and the uniform error mapping. Map it onto
    /// a route afterwards with <c>app.MapFlirtyMcp()</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method deliberately does <b>not</b> call <c>AddFlirty(...)</c>: the database provider and the
    /// connection string are the host's decision, and calling it here would silently pick defaults. A
    /// Flirty stack registered via <c>services.AddFlirty(...)</c> is therefore a prerequisite, exactly as
    /// it is for <c>MapFlirtyEndpoints</c>.
    /// </para>
    /// <para>
    /// The transport is Streamable HTTP in <b>stateless</b> mode, and that is set explicitly even though it
    /// is the SDK default (which has moved once already). Protocol revision <c>2026-07-28</c> removed the
    /// <c>initialize</c> handshake (SEP-2575) and the <c>Mcp-Session-Id</c> header (SEP-2567) from the wire
    /// format; a stateful server refuses such clients with <c>-32022 UnsupportedProtocolVersion</c>. In
    /// stateless mode the SDK resolves a tool call's scoped services from the ASP.NET request scope, so the
    /// tools resolve <c>ISender</c> and the <c>FlirtyDbContext</c> with exactly the lifetime story of a
    /// minimal-API endpoint – no scope factory of this package's own.
    /// </para>
    /// <para>
    /// The returned builder is the SDK's own, so a host can add its own tools, prompts or filters to the
    /// same server. Flirty's error mapping is registered as the first call-tool filter and therefore
    /// composes <i>outermost</i> – it also wraps whatever the host adds. An exception deriving from
    /// <c>McpException</c> is left to the SDK and is the documented way out of that mapping.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection of the host app.</param>
    /// <param name="configure">
    /// Optional configuration of the server (tool surfaces, server name). Without it,
    /// <see cref="FlirtyMcpSurface.All"/> is registered.
    /// </param>
    /// <returns>
    /// The <c>IMcpServerBuilder</c> created by the SDK, to configure the server further (e.g. add the
    /// host's own tools).
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    /// <example>
    /// <code>
    /// var builder = WebApplication.CreateBuilder(args);
    /// builder.Services.AddFlirty(o => o.UseSqlServer(conn).ApplyMigrations());
    /// builder.Services.AddFlirtyMcp();
    /// var app = builder.Build();
    /// app.MapFlirtyMcp("/mcp").RequireAuthorization();
    /// app.Run();
    /// </code>
    /// </example>
    public static IMcpServerBuilder AddFlirtyMcp(
        this IServiceCollection services, Action<FlirtyMcpOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new FlirtyMcpOptions();
        configure?.Invoke(options);

        var builder = services
            .AddMcpServer(server =>
            {
                server.ServerInfo = new()
                {
                    Name = options.ServerName,
                    Version = typeof(FlirtyMcpOptions).Assembly.GetName().Version?.ToString() ?? "1.0.0",
                };

                // Delivered in InitializeResult.Instructions. Measured, because the combination is not
                // obvious: the SDK's own client still performs the initialize handshake and negotiates
                // 2025-06-18, even against this stateless server. A client that instead speaks 2026-07-28
                // with per-request metadata gets no instructions at all - the SDK can carry them in
                // DiscoverResult.Instructions, but this server does not expose the discover method
                // (-32601). Hence the rule that everything stated here is ALSO stated in a tool or
                // parameter description: those travel with tools/list, which every client can read.
                server.ServerInstructions = FlirtyMcpInstructions.Text;
            })
            .WithHttpTransport(transport => transport.Stateless = true)
            .WithRequestFilters(filters => filters.AddCallToolFilter(FlirtyMcpExceptionFilter.Instance));

        if (options.Surface.HasFlag(FlirtyMcpSurface.Admin))
        {
            // One WithTools per tool class, and the class list is the surface: a class added under Tools/
            // and forgotten here compiles, ships and is invisible to every client. A test compares the
            // assembly's [McpServerToolType] types against what tools/list returns for exactly that reason.
            builder
                .WithTools<FlirtyDialogTools>()
                .WithTools<FlirtyQuestionTools>()
                .WithTools<FlirtyAnswerOptionTools>()
                .WithTools<FlirtyTransitionTools>()
                .WithTools<FlirtyLoopTools>()
                .WithTools<FlirtyTriggerTools>()
                .WithTools<FlirtyLayoutTools>();
        }

        // The runtime tool class follows in the next build-out stage (#128); FlirtyMcpSurface.Runtime
        // registers nothing today.
        return builder;
    }
}
