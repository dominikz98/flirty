using Flirty.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Flirty.Mcp;

/// <summary>
/// Builds the <see cref="FlirtyDbContext"/> of the current scope: the selected MCP target's database, or
/// the one the host registered with <c>AddFlirty(...)</c>.
/// </summary>
/// <remarks>
/// <para>
/// Registered by <c>AddFlirtyMcp</c> – but <b>only</b> when the host declared at least one target – as
/// <c>services.Replace(ServiceDescriptor.Scoped(FlirtyMcpDbContextFactory.Create))</c>. Replacing
/// exactly <see cref="FlirtyDbContext"/> and nothing else is the point: the
/// <see cref="DbContextOptions{TContext}"/> that <c>AddDbContext</c> registered stay untouched, so they
/// remain both the fallback here and the answer for every consumer that resolves them itself.
/// </para>
/// <para>
/// It deliberately does <b>not</b> implement <c>IDbContextFactory&lt;FlirtyDbContext&gt;</c>. Nothing in
/// this package consumes that interface, and <c>Flirty.Designer</c> registers its own implementation of
/// it – claiming the slot would repoint the designer in a process that hosts both.
/// </para>
/// </remarks>
internal static class FlirtyMcpDbContextFactory
{
    /// <summary>Creates the context for the current scope.</summary>
    /// <param name="services">The scoped service provider of the current request.</param>
    /// <returns>A context bound to the resolved target, or to the host's own database.</returns>
    internal static FlirtyDbContext Create(IServiceProvider services)
    {
        var target = services.GetRequiredService<FlirtyMcpRequestTarget>().Resolve();

        // GetRequiredService on the fallback path deliberately: a host with no database registered gets
        // exactly the failure it got before this stage, no new message and no new failure mode.
        return target is null
            ? new FlirtyDbContext(services.GetRequiredService<DbContextOptions<FlirtyDbContext>>())
            : new FlirtyDbContext(services.GetRequiredService<FlirtyMcpTargetRegistry>().GetOptions(target));
    }
}
