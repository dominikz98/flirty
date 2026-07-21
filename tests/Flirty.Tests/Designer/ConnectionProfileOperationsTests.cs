using Flirty.Designer.Models;
using Flirty.Designer.Services;
using Flirty.Persistence;

namespace Flirty.Tests.Designer;

/// <summary>
/// Tests für die <see cref="ConnectionProfileOperations"/> (#37): Test-Connection und Migrate gegen eine
/// echte SQLite-Datei im Temp-Verzeichnis. Belegt Test-/Migrate-Buttons der Profil-Verwaltung ohne Docker.
/// </summary>
public sealed class ConnectionProfileOperationsTests
{
    private readonly ConnectionProfileOperations _operations = new();

    [Fact]
    public async Task TestConnectionAsync_liefert_Erfolg_fuer_migriertes_SQLite_Profil()
    {
        await RunWithTempDbAsync(async profile =>
        {
            // SQLite meldet CanConnect erst dann true, wenn die Datei existiert -> zuerst anlegen.
            await _operations.ApplyMigrationsAsync(profile);

            var result = await _operations.TestConnectionAsync(profile);
            Assert.True(result.Success, result.Message);
        });
    }

    [Fact]
    public async Task TestConnectionAsync_liefert_Fehler_bei_ungueltiger_Verbindungszeichenfolge()
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
    public async Task ApplyMigrationsAsync_legt_Schema_an_und_meldet_angewendete_Migration()
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
            // Pooling=False: sonst hält der SQLite-Connection-Pool die Datei offen und der Cleanup scheitert.
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
