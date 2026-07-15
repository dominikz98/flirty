using Flirty.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Flirty.Tests.Persistence;

/// <summary>
/// Verifiziert Issue #19 für den SQLite-Provider: die <c>InitialCreate</c>-Migration aus
/// <c>Flirty.Migrations.Sqlite</c> erzeugt das Schema, und ein Dialog-Aggregat wird korrekt
/// gespeichert und geladen. Läuft gegen SQLite in-memory (keine externe Abhängigkeit).
/// </summary>
public sealed class SqliteProviderMigrationTests : IDisposable
{
    private readonly SqliteConnection _connection;

    /// <summary>Öffnet eine SQLite-in-memory-Verbindung, die über alle Kontexte offen bleiben muss.</summary>
    public SqliteProviderMigrationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    /// <summary>Schließt die Verbindung und verwirft die in-memory-Datenbank.</summary>
    public void Dispose() => _connection.Dispose();

    /// <summary>Wendet die SQLite-Migration an und prüft den Aggregat-Round-Trip.</summary>
    [Fact]
    public void Migration_erzeugt_Schema_und_Aggregat_wird_round_tripped()
    {
        var options = new DbContextOptionsBuilder<FlirtyDbContext>()
            .UseSqlite(_connection, sqlite => sqlite.MigrationsAssembly("Flirty.Migrations.Sqlite"))
            .Options;

        ProviderMigrationAssertions.MigrateCreatesSchemaAndRoundTripsAggregate(options);
    }
}
