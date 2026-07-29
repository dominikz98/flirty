using Flirty.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Flirty.Hosting;

/// <summary>
/// <see cref="IHostedService"/> that, on host start, applies all pending EF Core migrations to the
/// registered <see cref="FlirtyDbContext"/> (<c>Database.MigrateAsync()</c>).
/// </summary>
/// <remarks>
/// <para>
/// The service is enabled via <c>services.AddFlirty(o =&gt; o.ApplyMigrations())</c> (issue #20).
/// It requires that a <see cref="FlirtyDbContext"/> including provider and
/// <c>MigrationsAssembly</c> is already registered in the container (the convenient provider choice
/// <c>o.UseSqlite/UsePostgreSql/UseSqlServer</c> follows in #34).
/// </para>
/// <para>
/// Deliberately <see cref="IHostedService"/> and not <c>BackgroundService</c>: the host awaits all
/// <see cref="StartAsync"/> calls before it counts as started (with ASP.NET Core, before requests
/// are accepted). This guarantees the schema is migrated before the app takes on work, and a
/// migration error aborts the start fail-fast. If the service is registered first, its
/// migration runs before the <see cref="StartAsync"/> calls of the other hosted services.
/// </para>
/// </remarks>
public sealed class FlirtyMigrationHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FlirtyMigrationHostedService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FlirtyMigrationHostedService"/> class.
    /// </summary>
    /// <param name="scopeFactory">
    /// Factory for a DI scope. Required because this service runs as a singleton, while the
    /// <see cref="FlirtyDbContext"/> is registered scoped (no captive dependency).
    /// </param>
    /// <param name="logger">The logger for start and completion of the migration.</param>
    public FlirtyMigrationHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<FlirtyMigrationHostedService> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Opens its own DI scope, resolves the <see cref="FlirtyDbContext"/> and applies all
    /// pending migrations. <c>MigrateAsync</c> is idempotent (only pending migrations are
    /// applied) and honors the <paramref name="cancellationToken"/>.
    /// </summary>
    /// <param name="cancellationToken">Token that aborts the host start operation.</param>
    /// <returns>A task that completes once the migration has been applied.</returns>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<FlirtyDbContext>();

        _logger.LogInformation("Flirty applies pending EF Core migrations");
        await context.Database.MigrateAsync(cancellationToken);
        _logger.LogInformation("Flirty migrations completed");
    }

    /// <summary>No cleanup needed on shutdown – returns a completed task.</summary>
    /// <param name="cancellationToken">Not used.</param>
    /// <returns>An already completed task.</returns>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
