using Flirty.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Flirty.Migrations.SqlServer;

/// <summary>
/// Design-Time-Factory, über die <c>dotnet ef</c> den <see cref="FlirtyDbContext"/> mit dem
/// SQL-Server-Provider erzeugt, um Migrationen in diesem Assembly zu generieren. Der
/// Connection-String ist ein Platzhalter – <c>migrations add</c> öffnet keine Verbindung.
/// </summary>
internal sealed class SqlServerDesignTimeDbContextFactory : IDesignTimeDbContextFactory<FlirtyDbContext>
{
    /// <inheritdoc />
    public FlirtyDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<FlirtyDbContext>()
            .UseSqlServer(
                "Server=localhost;Database=flirty_design;User Id=sa;Password=flirty_Design#1;TrustServerCertificate=true",
                sqlServer => sqlServer.MigrationsAssembly(typeof(SqlServerDesignTimeDbContextFactory).Assembly.GetName().Name))
            .Options;

        return new FlirtyDbContext(options);
    }
}
