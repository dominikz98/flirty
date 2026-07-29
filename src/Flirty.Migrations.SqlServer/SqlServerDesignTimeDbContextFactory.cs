using Flirty.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Flirty.Migrations.SqlServer;

/// <summary>
/// Design-time factory via which <c>dotnet ef</c> creates the <see cref="FlirtyDbContext"/> with the
/// SQL Server provider, in order to generate migrations in this assembly. The
/// connection string is a placeholder – <c>migrations add</c> opens no connection.
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
