using Flirty.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Flirty.Hosting;

/// <summary>
/// <see cref="IHostedService"/>, das beim Host-Start alle ausstehenden EF-Core-Migrationen auf den
/// registrierten <see cref="FlirtyDbContext"/> anwendet (<c>Database.MigrateAsync()</c>).
/// </summary>
/// <remarks>
/// <para>
/// Aktiviert wird der Service über <c>services.AddFlirty(o =&gt; o.ApplyMigrations())</c> (Issue #20).
/// Er setzt voraus, dass ein <see cref="FlirtyDbContext"/> inklusive Provider und
/// <c>MigrationsAssembly</c> bereits im Container registriert ist (die komfortable Provider-Wahl
/// <c>o.UseSqlite/UsePostgreSql/UseSqlServer</c> folgt in #34).
/// </para>
/// <para>
/// Bewusst <see cref="IHostedService"/> und nicht <c>BackgroundService</c>: der Host awaited alle
/// <see cref="StartAsync"/>-Aufrufe, bevor er als gestartet gilt (bei ASP.NET Core, bevor Requests
/// angenommen werden). So ist das Schema garantiert migriert, bevor die App Arbeit annimmt, und ein
/// Migrationsfehler bricht den Start fail-fast ab. Wird der Service als Erstes registriert, läuft seine
/// Migration vor den <see cref="StartAsync"/>-Aufrufen der übrigen Hosted Services.
/// </para>
/// </remarks>
public sealed class FlirtyMigrationHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FlirtyMigrationHostedService> _logger;

    /// <summary>
    /// Initialisiert eine neue Instanz der <see cref="FlirtyMigrationHostedService"/>-Klasse.
    /// </summary>
    /// <param name="scopeFactory">
    /// Factory für einen DI-Scope. Erforderlich, weil dieser Service als Singleton läuft, der
    /// <see cref="FlirtyDbContext"/> aber scoped registriert ist (keine Captive Dependency).
    /// </param>
    /// <param name="logger">Der Logger für Start und Abschluss der Migration.</param>
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
    /// Öffnet einen eigenen DI-Scope, löst den <see cref="FlirtyDbContext"/> auf und wendet alle
    /// ausstehenden Migrationen an. <c>MigrateAsync</c> ist idempotent (nur Pending-Migrationen werden
    /// angewandt) und honoriert den <paramref name="cancellationToken"/>.
    /// </summary>
    /// <param name="cancellationToken">Token, das den Host-Start-Vorgang abbricht.</param>
    /// <returns>Ein Task, der abgeschlossen ist, sobald die Migration angewandt wurde.</returns>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<FlirtyDbContext>();

        _logger.LogInformation("Flirty wendet ausstehende EF-Core-Migrationen an");
        await context.Database.MigrateAsync(cancellationToken);
        _logger.LogInformation("Flirty-Migrationen abgeschlossen");
    }

    /// <summary>Kein Aufräumbedarf beim Herunterfahren – gibt einen abgeschlossenen Task zurück.</summary>
    /// <param name="cancellationToken">Wird nicht verwendet.</param>
    /// <returns>Ein bereits abgeschlossener Task.</returns>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
