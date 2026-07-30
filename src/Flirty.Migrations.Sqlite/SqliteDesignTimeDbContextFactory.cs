using Flirty.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Flirty.Migrations.Sqlite;

/// <summary>
/// Design-time factory via which <c>dotnet ef</c> creates the <see cref="FlirtyDbContext"/> with the
/// SQLite provider, in order to generate migrations in this assembly. The connection string
/// is a placeholder – <c>migrations add</c> opens no connection.
/// </summary>
internal sealed class SqliteDesignTimeDbContextFactory : IDesignTimeDbContextFactory<FlirtyDbContext>
{
    /// <inheritdoc />
    public FlirtyDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<FlirtyDbContext>()
            .UseSqlite(
                "Data Source=flirty.design.db",
                sqlite => sqlite.MigrationsAssembly(typeof(SqliteDesignTimeDbContextFactory).Assembly.GetName().Name))
            .Options;

        return new FlirtyDbContext(options);
    }
}
