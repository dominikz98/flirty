using Flirty.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Flirty.Tests.Persistence;

/// <summary>
/// Verifiziert Issue #19 für den PostgreSQL-Provider (Npgsql): die <c>InitialCreate</c>-Migration aus
/// <c>Flirty.Migrations.PostgreSql</c> erzeugt das Schema gegen eine reale PostgreSQL-Datenbank
/// (Testcontainers), und ein Dialog-Aggregat wird korrekt gespeichert und geladen. Ohne Docker wird
/// der Test übersprungen.
/// </summary>
public sealed class PostgreSqlProviderMigrationTests
{
    /// <summary>Startet einen PostgreSQL-Container, wendet die Migration an und prüft den Round-Trip.</summary>
    [SkippableFact]
    public async Task Migration_erzeugt_Schema_und_Aggregat_wird_round_tripped()
    {
        Skip.IfNot(DockerAvailability.IsAvailable, "Docker ist nicht verfügbar – PostgreSQL-Provider-Test übersprungen.");

        await using var container = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await container.StartAsync();

        var options = new DbContextOptionsBuilder<FlirtyDbContext>()
            .UseNpgsql(container.GetConnectionString(), npgsql => npgsql.MigrationsAssembly("Flirty.Migrations.PostgreSql"))
            .Options;

        ProviderMigrationAssertions.MigrateCreatesSchemaAndRoundTripsAggregate(options);
    }
}
