using Flirty.Designer.Models;
using Flirty.Designer.Services;
using Flirty.Persistence;

namespace Flirty.Tests.Designer;

/// <summary>
/// Tests for the <see cref="ConnectionProfileOperations"/> (#37): test-connection and migrate against
/// a real SQLite file in the temp directory. Proves the test/migrate buttons of the profile
/// management without Docker.
/// </summary>
public sealed class ConnectionProfileOperationsTests
{
    private readonly ConnectionProfileOperations _operations = new();

    [Fact]
    public async Task TestConnectionAsync_returns_success_for_a_migrated_SQLite_profile()
    {
        await RunWithTempDbAsync(async profile =>
        {
            // SQLite reports CanConnect as true only once the file exists -> create it first.
            await _operations.ApplyMigrationsAsync(profile);

            var result = await _operations.TestConnectionAsync(profile);
            Assert.True(result.Success, result.Message);
        });
    }

    [Fact]
    public async Task TestConnectionAsync_returns_an_error_for_an_invalid_connection_string()
    {
        var profile = new ConnectionProfile
        {
            Name = "Kaputt",
            Provider = FlirtyDatabaseProvider.Sqlite,
            ConnectionString = "Data Source=flirty.db;NichtUnterstuetztesSchluesselwort=1",
        };

        var result = await _operations.TestConnectionAsync(profile);

        Assert.False(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    [Fact]
    public async Task ApplyMigrationsAsync_creates_the_schema_and_reports_the_applied_migration()
    {
        await RunWithTempDbAsync(async profile =>
        {
            var pendingBefore = await _operations.GetPendingMigrationsAsync(profile);
            Assert.Contains(pendingBefore, migration => migration.EndsWith("InitialCreate", StringComparison.Ordinal));

            var result = await _operations.ApplyMigrationsAsync(profile);

            Assert.True(result.Success, result.Error);
            Assert.Contains(result.AppliedMigrations, migration => migration.EndsWith("InitialCreate", StringComparison.Ordinal));

            var pendingAfter = await _operations.GetPendingMigrationsAsync(profile);
            Assert.Empty(pendingAfter);
        });
    }

    private static async Task RunWithTempDbAsync(Func<ConnectionProfile, Task> test)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"flirty-designer-{Guid.NewGuid():N}.db");
        var profile = new ConnectionProfile
        {
            Name = "Temp",
            Provider = FlirtyDatabaseProvider.Sqlite,
            // Pooling=False: otherwise the SQLite connection pool keeps the file open and the cleanup fails.
            ConnectionString = $"Data Source={dbPath};Pooling=False",
        };

        try
        {
            await test(profile);
        }
        finally
        {
            foreach (var suffix in new[] { string.Empty, "-shm", "-wal" })
            {
                var file = dbPath + suffix;
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
        }
    }
}
