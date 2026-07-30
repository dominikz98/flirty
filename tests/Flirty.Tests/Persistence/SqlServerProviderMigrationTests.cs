using Flirty.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;

namespace Flirty.Tests.Persistence;

/// <summary>
/// Verifies issue #19 for the SQL Server provider: the <c>InitialCreate</c> migration from
/// <c>Flirty.Migrations.SqlServer</c> creates the schema against a real SQL Server database
/// (Testcontainers), and a dialog aggregate is stored and loaded correctly. Without Docker the test
/// skips itself.
/// </summary>
public sealed class SqlServerProviderMigrationTests
{
    /// <summary>Starts a SQL Server container, applies the migration and checks the round trip.</summary>
    [SkippableFact]
    public async Task Migration_creates_the_schema_and_the_aggregate_is_round_tripped()
    {
        Skip.IfNot(DockerAvailability.IsAvailable, "Docker is not available – SQL Server provider test skipped.");

        await using var container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await container.StartAsync();

        var options = new DbContextOptionsBuilder<FlirtyDbContext>()
            .UseSqlServer(container.GetConnectionString(), sqlServer => sqlServer.MigrationsAssembly("Flirty.Migrations.SqlServer"))
            .Options;

        ProviderMigrationAssertions.MigrateCreatesSchemaAndRoundTripsAggregate(options);
    }
}
