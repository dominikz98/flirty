using Flirty.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;

namespace Flirty.Tests.Persistence;

/// <summary>
/// Verifiziert Issue #19 für den SQL-Server-Provider: die <c>InitialCreate</c>-Migration aus
/// <c>Flirty.Migrations.SqlServer</c> erzeugt das Schema gegen eine reale SQL-Server-Datenbank
/// (Testcontainers), und ein Dialog-Aggregat wird korrekt gespeichert und geladen. Ohne Docker wird
/// der Test übersprungen.
/// </summary>
public sealed class SqlServerProviderMigrationTests
{
    /// <summary>Startet einen SQL-Server-Container, wendet die Migration an und prüft den Round-Trip.</summary>
    [SkippableFact]
    public async Task Migration_erzeugt_Schema_und_Aggregat_wird_round_tripped()
    {
        Skip.IfNot(DockerAvailability.IsAvailable, "Docker ist nicht verfügbar – SQL-Server-Provider-Test übersprungen.");

        await using var container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await container.StartAsync();

        var options = new DbContextOptionsBuilder<FlirtyDbContext>()
            .UseSqlServer(container.GetConnectionString(), sqlServer => sqlServer.MigrationsAssembly("Flirty.Migrations.SqlServer"))
            .Options;

        ProviderMigrationAssertions.MigrateCreatesSchemaAndRoundTripsAggregate(options);
    }
}
