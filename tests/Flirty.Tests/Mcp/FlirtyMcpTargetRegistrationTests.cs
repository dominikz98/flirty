using System.Net.Http.Json;
using Flirty.AspNetCore.Dtos.Admin;
using Flirty.Mcp;
using Flirty.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Flirty.Tests.Mcp;

/// <summary>
/// What declaring an MCP database target does to the host's service collection and routing (#129) – the
/// half of the stage that never touches an MCP client.
/// </summary>
/// <remarks>
/// The claim under test is a negative one, and negatives are what silently stop being true: declaring a
/// target must replace the <c>FlirtyDbContext</c> registration and <b>nothing else</b>. Get that wrong and
/// <c>MapFlirtyEndpoints</c>, <c>MapFlirtyAdminEndpoints</c> and <c>FlirtyMigrationHostedService</c> follow
/// the MCP target along without a single failing test elsewhere – every one of them would keep working,
/// just against the wrong database.
/// </remarks>
public sealed class FlirtyMcpTargetRegistrationTests
{
    /// <summary>
    /// A write over the HTTP admin surface lands in the host's own database even though MCP targets are
    /// declared and one of them is the default.
    /// </summary>
    [Fact]
    public async Task Declaring_a_target_does_not_change_what_MapFlirtyEndpoints_talks_to()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync(
            configureMcp: options => options.UseDefaultTarget("primary"),
            targets: ["primary", "secondary"]);

        var response = await host.Client.PostAsJsonAsync(
            "/flirty/admin/dialogs", new CreateDialogRequest("over-http", "Over HTTP", null));

        response.EnsureSuccessStatusCode();

        await using (var hostDatabase = host.OpenHostContext())
        {
            Assert.Equal("over-http", (await hostDatabase.Dialogs.SingleAsync()).Key);
        }

        await using var primary = host.OpenTargetContext("primary");
        await using var secondary = host.OpenTargetContext("secondary");
        Assert.Empty(await primary.Dialogs.ToListAsync());
        Assert.Empty(await secondary.Dialogs.ToListAsync());
    }

    /// <summary>
    /// A scope opened outside any request resolves the host's database too – the migration hosted service
    /// and any background work of the host see no target at all.
    /// </summary>
    [Fact]
    public async Task A_scope_outside_a_request_resolves_the_host_database()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync(
            configureMcp: options => options.UseDefaultTarget("primary"),
            targets: ["primary"]);

        using var scope = host.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FlirtyDbContext>();

        Assert.Equal(host.HostConnectionString, context.Database.GetConnectionString());
    }

    /// <summary>
    /// The context registration is replaced whichever order the two <c>Add</c> calls come in.
    /// </summary>
    /// <remarks>
    /// Both directions matter and they work for different reasons. <c>AddFlirty</c> first: EF has
    /// <c>TryAdd</c>ed the descriptor and <c>Replace</c> swaps it. <c>AddFlirtyMcp</c> first:
    /// <c>Replace</c> on an absent service type is a plain add, and EF's <c>TryAdd</c> then finds the type
    /// registered and skips. Only the second one would break silently, since nothing about it looks
    /// order-dependent at the call site.
    /// </remarks>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_context_registration_is_replaced_in_either_registration_order(bool flirtyFirst)
    {
        var services = new ServiceCollection();

        if (flirtyFirst)
        {
            services.AddFlirty(options => options.UseSqlite("Data Source=:memory:"));
            services.AddFlirtyMcp(WithOneTarget);
        }
        else
        {
            services.AddFlirtyMcp(WithOneTarget);
            services.AddFlirty(options => options.UseSqlite("Data Source=:memory:"));
        }

        var descriptor = Assert.Single(
            services, service => service.ServiceType == typeof(FlirtyDbContext));

        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        Assert.NotNull(descriptor.ImplementationFactory);

        // The fallback the replacement reads: never touched, in either order.
        Assert.Single(services, service => service.ServiceType == typeof(DbContextOptions<FlirtyDbContext>));
    }

    /// <summary>
    /// Without declared targets nothing of the target machinery reaches the context registration: EF's own
    /// descriptor survives untouched, type-based as it always was.
    /// </summary>
    [Fact]
    public void Without_declared_targets_the_ef_context_registration_is_untouched()
    {
        var services = new ServiceCollection();
        services.AddFlirty(options => options.UseSqlite("Data Source=:memory:"));
        services.AddFlirtyMcp();

        var descriptor = Assert.Single(
            services, service => service.ServiceType == typeof(FlirtyDbContext));

        Assert.Null(descriptor.ImplementationFactory);
        Assert.Equal(typeof(FlirtyDbContext), descriptor.ImplementationType);
    }

    /// <summary>A default target that was never declared fails at registration, not at the first call.</summary>
    [Fact]
    public void UseDefaultTarget_naming_an_undeclared_target_throws()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<ArgumentException>(() => services.AddFlirtyMcp(options => options
            .AddTarget("staging", FlirtyDatabaseProvider.Sqlite, "Data Source=:memory:")
            .UseDefaultTarget("production")));

        Assert.Contains("production", exception.Message, StringComparison.Ordinal);
        Assert.Contains("staging", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>A target name that no route segment could carry is refused where it is declared.</summary>
    [Theory]
    [InlineData("with space")]
    [InlineData("with/slash")]
    [InlineData("with?query")]
    public void AddTarget_rejects_a_name_that_cannot_appear_in_a_route(string name)
    {
        var options = new FlirtyMcpOptions();

        Assert.Throws<ArgumentException>(() =>
            options.AddTarget(name, FlirtyDatabaseProvider.Sqlite, "Data Source=:memory:"));
    }

    /// <summary>
    /// A duplicate target is refused, and case-insensitively so – otherwise <c>staging</c> and
    /// <c>Staging</c> would both be declared and the route could reach only one of them.
    /// </summary>
    [Fact]
    public void AddTarget_rejects_a_duplicate_name_case_insensitively()
    {
        var options = new FlirtyMcpOptions()
            .AddTarget("staging", FlirtyDatabaseProvider.Sqlite, "Data Source=:memory:");

        Assert.Throws<ArgumentException>(() =>
            options.AddTarget("STAGING", FlirtyDatabaseProvider.Sqlite, "Data Source=:memory:"));
    }

    /// <summary>
    /// A route parameter the resolution would not read is refused at mapping time.
    /// </summary>
    /// <remarks>
    /// It has to be refused there, because it has no runtime symptom at all: <c>{db}</c> simply never
    /// matches the <c>target</c> route value, so every request would quietly work against the default
    /// database while the client believed it had selected one.
    /// </remarks>
    [Fact]
    public async Task MapFlirtyMcp_rejects_a_route_parameter_that_is_not_target()
    {
        await using var app = BuildApp();

        var exception = Assert.Throws<ArgumentException>(() => app.MapFlirtyMcp("/mcp/{db}"));

        Assert.Contains("{target}", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>More than one route parameter is refused for the same reason.</summary>
    [Fact]
    public async Task MapFlirtyMcp_rejects_more_than_one_route_parameter()
    {
        await using var app = BuildApp();

        Assert.Throws<ArgumentException>(() => app.MapFlirtyMcp("/mcp/{tenant}/{target}"));
    }

    /// <summary>
    /// Both patterns map side by side in one application – the literal route and the parameterised one are
    /// two independent endpoints, so each can carry its own authorization.
    /// </summary>
    [Fact]
    public async Task Both_route_patterns_map_side_by_side()
    {
        await using var app = BuildApp();

        app.MapFlirtyMcp("/mcp");
        app.MapFlirtyMcp("/mcp/{target}");
        await app.StartAsync();

        var routes = app.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .ToList();

        // Trailing slashes because MapMcp is a MapGroup(pattern) with a MapPost("") inside it – the raw
        // text is the concatenation, and both routes still match the slashless request path.
        Assert.Equal(["/mcp/", "/mcp/{target}/"], routes.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// Declared targets on a stateful transport fail at startup rather than silently serving the host's
    /// database.
    /// </summary>
    /// <remarks>
    /// Stateful, the SDK invokes the session callback once per session on a scope that is long gone by the
    /// time a tool runs – so the target would never be captured and every call would fall through to the
    /// fallback. Nothing about that is observable from the outside, which is what makes a startup failure
    /// the only honest answer.
    /// </remarks>
    [Fact]
    public async Task MapFlirtyMcp_refuses_declared_targets_on_a_stateful_transport()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddFlirty(options => options.UseSqlite("Data Source=:memory:"));
        builder.Services
            .AddFlirtyMcp(WithOneTarget)
            .WithHttpTransport(transport => transport.Stateless = false);

        await using var app = builder.Build();

        var exception = Assert.Throws<InvalidOperationException>(() => app.MapFlirtyMcp("/mcp/{target}"));

        Assert.Contains("stateless", exception.Message, StringComparison.Ordinal);
    }

    private static void WithOneTarget(FlirtyMcpOptions options) =>
        options.AddTarget("staging", FlirtyDatabaseProvider.Sqlite, "Data Source=:memory:");

    private static WebApplication BuildApp()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddFlirty(options => options.UseSqlite("Data Source=:memory:"));
        builder.Services.AddFlirtyMcp();
        return builder.Build();
    }
}
