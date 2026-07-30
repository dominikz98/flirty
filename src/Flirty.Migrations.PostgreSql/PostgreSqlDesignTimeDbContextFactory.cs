using Flirty.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Flirty.Migrations.PostgreSql;

/// <summary>
/// Design-time factory via which <c>dotnet ef</c> creates the <see cref="FlirtyDbContext"/> with the
/// PostgreSQL provider (Npgsql), in order to generate migrations in this assembly. The
/// connection string is a placeholder – <c>migrations add</c> opens no connection.
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
