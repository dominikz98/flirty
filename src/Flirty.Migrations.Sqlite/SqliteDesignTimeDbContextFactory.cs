using Flirty.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Flirty.Migrations.Sqlite;

/// <summary>
/// Design-Time-Factory, über die <c>dotnet ef</c> den <see cref="FlirtyDbContext"/> mit dem
/// SQLite-Provider erzeugt, um Migrationen in diesem Assembly zu generieren. Der Connection-String
/// ist ein Platzhalter – <c>migrations add</c> öffnet keine Verbindung.
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
