using Flirty.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Flirty.Tests.Persistence;

/// <summary>
/// Verifies issue #19 for the SQLite provider: the <c>InitialCreate</c> migration from
/// <c>Flirty.Migrations.Sqlite</c> creates the schema, and a dialog aggregate is stored and loaded
/// correctly. Runs against SQLite in-memory (no external dependency).
/// </summary>
public sealed class SqliteProviderMigrationTests : IDisposable
{
    private readonly SqliteConnection _connection;

    /// <summary>Opens a SQLite in-memory connection that has to stay open across all contexts.</summary>
    public SqliteProviderMigrationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    /// <summary>Closes the connection and discards the in-memory database.</summary>
    public void Dispose() => _connection.Dispose();

    /// <summary>Applies the SQLite migration and checks the aggregate round trip.</summary>
    [Fact]
    public void Migration_creates_the_schema_and_the_aggregate_is_round_tripped()
    {
        var options = new DbContextOptionsBuilder<FlirtyDbContext>()
            .UseSqlite(_connection, sqlite => sqlite.MigrationsAssembly("Flirty.Migrations.Sqlite"))
            .Options;

        ProviderMigrationAssertions.MigrateCreatesSchemaAndRoundTripsAggregate(options);
    }
}
