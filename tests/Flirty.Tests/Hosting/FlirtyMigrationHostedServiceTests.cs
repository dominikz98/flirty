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
/// Verifies issue #20: <c>AddFlirty(o =&gt; o.ApplyMigrations())</c> wires up the
/// <see cref="FlirtyMigrationHostedService"/>, which applies the provider-specific
/// <c>InitialCreate</c> migration on startup. Runs against SQLite in-memory (no external dependency);
/// the same open connection is shared across all DI scopes so the in-memory database survives.
/// </summary>
public sealed class FlirtyMigrationHostedServiceTests
{
    /// <summary>No DbContext needed: only the registration decision is checked.</summary>
    [Fact]
    public void ApplyMigrations_registers_the_hosted_service()
    {
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddFlirty(options => options.ApplyMigrations())
            .BuildServiceProvider();

        Assert.Single(provider.GetServices<IHostedService>().OfType<FlirtyMigrationHostedService>());
    }

    /// <summary>Without <c>ApplyMigrations()</c> no migration hosted service may be registered.</summary>
    [Fact]
    public void Without_ApplyMigrations_no_hosted_service_is_registered()
    {
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddFlirty(_ => { })
            .BuildServiceProvider();

        Assert.Empty(provider.GetServices<IHostedService>().OfType<FlirtyMigrationHostedService>());
    }

    /// <summary>StartAsync applies the migration and logs both start and completion.</summary>
    [Fact]
    public async Task StartAsync_applies_InitialCreate()
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

        Assert.Contains(spy.Messages, message => message.Contains("applies pending", StringComparison.Ordinal));
        Assert.Contains(spy.Messages, message => message.Contains("completed", StringComparison.Ordinal));
    }

    /// <summary>A second run finds no pending migrations and does not throw (idempotence).</summary>
    [Fact]
    public async Task StartAsync_is_idempotent()
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

    /// <summary>An already cancelled token is passed straight through to MigrateAsync.</summary>
    [Fact]
    public async Task StartAsync_passes_the_CancellationToken_through()
    {
        using var connection = OpenConnection();
        await using var provider = BuildProvider(connection, new SpyLoggerProvider());

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => SingleHostedService(provider).StartAsync(cts.Token));
    }

    /// <summary>StopAsync is a no-op and does not throw.</summary>
    [Fact]
    public async Task StopAsync_is_a_no_op()
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
