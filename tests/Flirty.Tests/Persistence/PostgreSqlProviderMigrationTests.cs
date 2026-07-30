using Flirty.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Flirty.Tests.Persistence;

/// <summary>
/// Verifies issue #19 for the PostgreSQL provider (Npgsql): the <c>InitialCreate</c> migration from
/// <c>Flirty.Migrations.PostgreSql</c> creates the schema against a real PostgreSQL database
/// (Testcontainers), and a dialog aggregate is stored and loaded correctly. Without Docker the test
/// skips itself.
/// </summary>
public sealed class PostgreSqlProviderMigrationTests
{
    /// <summary>Starts a PostgreSQL container, applies the migration and checks the round trip.</summary>
    [SkippableFact]
    public async Task Migration_creates_the_schema_and_the_aggregate_is_round_tripped()
    {
        Skip.IfNot(DockerAvailability.IsAvailable, "Docker is not available – PostgreSQL provider test skipped.");

        await using var container = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await container.StartAsync();

        var options = new DbContextOptionsBuilder<FlirtyDbContext>()
            .UseNpgsql(container.GetConnectionString(), npgsql => npgsql.MigrationsAssembly("Flirty.Migrations.PostgreSql"))
            .Options;

        ProviderMigrationAssertions.MigrateCreatesSchemaAndRoundTripsAggregate(options);
    }
}
