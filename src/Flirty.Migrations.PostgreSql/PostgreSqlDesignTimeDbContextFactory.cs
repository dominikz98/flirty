using Flirty.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Flirty.Migrations.PostgreSql;

/// <summary>
/// Design-Time-Factory, über die <c>dotnet ef</c> den <see cref="FlirtyDbContext"/> mit dem
/// PostgreSQL-Provider (Npgsql) erzeugt, um Migrationen in diesem Assembly zu generieren. Der
/// Connection-String ist ein Platzhalter – <c>migrations add</c> öffnet keine Verbindung.
/// </summary>
internal sealed class PostgreSqlDesignTimeDbContextFactory : IDesignTimeDbContextFactory<FlirtyDbContext>
{
    /// <inheritdoc />
    public FlirtyDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<FlirtyDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=flirty_design;Username=flirty;Password=flirty",
                npgsql => npgsql.MigrationsAssembly(typeof(PostgreSqlDesignTimeDbContextFactory).Assembly.GetName().Name))
            .Options;

        return new FlirtyDbContext(options);
    }
}
