using System.Collections.Concurrent;
using Flirty.Hosting;
using Flirty.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Flirty.Tests.Hosting;

/// <summary>
/// Verifiziert Issue #20: <c>AddFlirty(o =&gt; o.ApplyMigrations())</c> verdrahtet den
/// <see cref="FlirtyMigrationHostedService"/>, der beim Start die provider-spezifische
/// <c>InitialCreate</c>-Migration anwendet. Läuft gegen SQLite in-memory (keine externe Abhängigkeit);
/// dieselbe offene Verbindung wird über alle DI-Scopes geteilt, damit die in-memory-DB erhalten bleibt.
/// </summary>
public sealed class FlirtyMigrationHostedServiceTests
{
    /// <summary>Ohne DbContext-Bedarf: nur die Registrierungsentscheidung wird geprüft.</summary>
    [Fact]
    public void ApplyMigrations_registriert_den_HostedService()
    {
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddFlirty(options => options.ApplyMigrations())
            .BuildServiceProvider();

        Assert.Single(provider.GetServices<IHostedService>().OfType<FlirtyMigrationHostedService>());
    }

    /// <summary>Ohne <c>ApplyMigrations()</c> darf kein Migrations-Hosted-Service registriert werden.</summary>
    [Fact]
    public void Ohne_ApplyMigrations_wird_kein_HostedService_registriert()
    {
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddFlirty(_ => { })
            .BuildServiceProvider();

        Assert.Empty(provider.GetServices<IHostedService>().OfType<FlirtyMigrationHostedService>());
    }

    /// <summary>StartAsync wendet die Migration an und protokolliert Beginn und Abschluss.</summary>
    [Fact]
    public async Task StartAsync_wendet_InitialCreate_an()
    {
        using var connection = OpenConnection();
        var spy = new SpyLoggerProvider();
        await using var provider = BuildProvider(connection, spy);

        await SingleHostedService(provider).StartAsync(CancellationToken.None);

        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FlirtyDbContext>();
        Assert.Empty(context.Database.GetPendingMigrations());
        Assert.Contains(
            context.Database.GetAppliedMigrations(),
            migration => migration.EndsWith("InitialCreate", StringComparison.Ordinal));

        Assert.Contains(spy.Messages, message => message.Contains("wendet ausstehende", StringComparison.Ordinal));
        Assert.Contains(spy.Messages, message => message.Contains("abgeschlossen", StringComparison.Ordinal));
    }

    /// <summary>Ein zweiter Lauf findet keine Pending-Migrationen und wirft nicht (Idempotenz).</summary>
    [Fact]
    public async Task StartAsync_ist_idempotent()
    {
        using var connection = OpenConnection();
        await using var provider = BuildProvider(connection, new SpyLoggerProvider());

        var hosted = SingleHostedService(provider);
        await hosted.StartAsync(CancellationToken.None);
        await hosted.StartAsync(CancellationToken.None);

        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FlirtyDbContext>();
        Assert.Empty(context.Database.GetPendingMigrations());
    }

    /// <summary>Ein bereits abgebrochener Token wird an MigrateAsync durchgereicht.</summary>
    [Fact]
    public async Task StartAsync_reicht_den_CancellationToken_durch()
    {
        using var connection = OpenConnection();
        await using var provider = BuildProvider(connection, new SpyLoggerProvider());

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => SingleHostedService(provider).StartAsync(cts.Token));
    }

    /// <summary>StopAsync ist ein No-op und wirft nicht.</summary>
    [Fact]
    public async Task StopAsync_ist_ein_NoOp()
    {
        using var connection = OpenConnection();
        await using var provider = BuildProvider(connection, new SpyLoggerProvider());

        await SingleHostedService(provider).StopAsync(CancellationToken.None);
    }

    private static SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        return connection;
    }

    private static ServiceProvider BuildProvider(SqliteConnection connection, SpyLoggerProvider spy)
        => new ServiceCollection()
            .AddLogging(builder => builder.AddProvider(spy))
            .AddDbContext<FlirtyDbContext>(options =>
                options.UseSqlite(connection, sqlite => sqlite.MigrationsAssembly("Flirty.Migrations.Sqlite")))
            .AddFlirty(options => options.ApplyMigrations())
            .BuildServiceProvider();

    private static FlirtyMigrationHostedService SingleHostedService(IServiceProvider provider)
        => provider.GetServices<IHostedService>().OfType<FlirtyMigrationHostedService>().Single();

    private sealed class SpyLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) => new SpyLogger(Messages);

        public void Dispose()
        {
        }

        private sealed class SpyLogger(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                messages.Enqueue(formatter(state, exception));
            }
        }
    }
}
