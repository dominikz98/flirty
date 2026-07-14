using Flirty.Domain;
using Microsoft.EntityFrameworkCore;

namespace Flirty.Persistence;

/// <summary>
/// Der EF-Core-<see cref="DbContext"/> der Flirty-Engine. Bündelt das Konfigurations-Aggregat
/// (Wurzel <see cref="Dialog"/>) und das Runtime-Aggregat (Wurzel <see cref="DialogSession"/>).
/// Bewusst provider-agnostisch: die Provider-Wahl (SQLite/PostgreSQL/SQL Server) und die
/// Migrationen erfolgen von außen über <see cref="DbContextOptions"/> bzw. in Folge-Issues.
/// </summary>
public sealed class FlirtyDbContext : DbContext
{
    /// <summary>
    /// Erstellt den Context mit den von außen (z. B. per Dependency Injection) übergebenen
    /// Optionen, die insbesondere den Datenbank-Provider und die Verbindung festlegen.
    /// </summary>
    /// <param name="options">Die Context-Optionen inklusive Provider-Konfiguration.</param>
    public FlirtyDbContext(DbContextOptions<FlirtyDbContext> options)
        : base(options)
    {
    }

    /// <summary>Die konfigurierten Dialoge (Aggregat-Root der Konfigurationsebene).</summary>
    public DbSet<Dialog> Dialogs => Set<Dialog>();

    /// <summary>Die laufenden bzw. abgeschlossenen Sessions (Aggregat-Root der Runtime-Ebene).</summary>
    public DbSet<DialogSession> DialogSessions => Set<DialogSession>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Alle IEntityTypeConfiguration<T> dieser Assembly anwenden (Persistence/Configurations/*).
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FlirtyDbContext).Assembly);
    }
}
