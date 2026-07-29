using Flirty.Domain;
using Microsoft.EntityFrameworkCore;

namespace Flirty.Persistence;

/// <summary>
/// The EF Core <see cref="DbContext"/> of the Flirty engine. Bundles the configuration aggregate
/// (root <see cref="Dialog"/>) and the runtime aggregate (root <see cref="DialogSession"/>).
/// Deliberately provider-agnostic: the provider choice (SQLite/PostgreSQL/SQL Server) and the
/// migrations are supplied from outside via <see cref="DbContextOptions"/> or in follow-up issues.
/// </summary>
public sealed class FlirtyDbContext : DbContext
{
    /// <summary>
    /// Creates the context with the options passed in from outside (e.g. via dependency injection),
    /// which in particular set the database provider and the connection.
    /// </summary>
    /// <param name="options">The context options including provider configuration.</param>
    public FlirtyDbContext(DbContextOptions<FlirtyDbContext> options)
        : base(options)
    {
    }

    /// <summary>The configured dialogs (aggregate root of the configuration layer).</summary>
    public DbSet<Dialog> Dialogs => Set<Dialog>();

    /// <summary>The running or completed sessions (aggregate root of the runtime layer).</summary>
    public DbSet<DialogSession> DialogSessions => Set<DialogSession>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply all IEntityTypeConfiguration<T> of this assembly (Persistence/Configurations/*).
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FlirtyDbContext).Assembly);
    }
}
