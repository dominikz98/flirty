using Flirty.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flirty.Tests.Persistence;

/// <summary>
/// Gemeinsame Zusicherung für die provider-übergreifenden Migrationstests (#19): wendet die
/// provider-spezifische <c>InitialCreate</c>-Migration via <c>Database.Migrate()</c> an (erzeugt
/// also die DB gegen den jeweiligen Provider) und prüft anschließend einen vollständigen
/// Aggregat-Round-Trip. Wird von SQLite, PostgreSQL und SQL Server identisch verwendet.
/// </summary>
internal static class ProviderMigrationAssertions
{
    /// <summary>
    /// Migriert die Datenbank aus den angegebenen <paramref name="options"/>, speichert ein
    /// vollständiges Dialog-Aggregat und lädt es mit allen Navigationen erneut. Verifiziert, dass
    /// das Schema aus der Migration (nicht aus <c>EnsureCreated</c>) entstanden ist.
    /// </summary>
    /// <param name="options">Vorkonfigurierte Optionen inkl. Provider und Migrations-Assembly.</param>
    public static void MigrateCreatesSchemaAndRoundTripsAggregate(DbContextOptions<FlirtyDbContext> options)
    {
        var dialogId = Guid.NewGuid();

        using (var context = new FlirtyDbContext(options))
        {
            // Wendet die provider-spezifische InitialCreate-Migration an -> das Schema entsteht.
            context.Database.Migrate();

            context.Dialogs.Add(TestDialogFactory.BuildFullDialog(dialogId, out _));
            context.SaveChanges();
        }

        using (var context = new FlirtyDbContext(options))
        {
            var loaded = context.Dialogs
                .Include(dialog => dialog.Questions).ThenInclude(question => question.Options)
                .Include(dialog => dialog.Transitions)
                .Include(dialog => dialog.Loops)
                .Include(dialog => dialog.Triggers)
                .Include(dialog => dialog.Layout)
                .Single(dialog => dialog.Id == dialogId);

            Assert.Equal("onboarding", loaded.Key);
            var question = Assert.Single(loaded.Questions);
            Assert.Equal(2, question.Options.Count);
            Assert.Single(loaded.Transitions);
            Assert.Single(loaded.Loops);
            Assert.Single(loaded.Triggers);

            var layout = Assert.Single(loaded.Layout);
            Assert.Equal(320, layout.X);
            Assert.Equal(160, layout.Y);
        }

        using (var context = new FlirtyDbContext(options))
        {
            // Belegt, dass das Schema aus angewandten Migrationen stammt (nicht aus EnsureCreated) und
            // dass alle Sets dieses Providers vollständig sind – hier hängt das Akzeptanzkriterium
            // „Migration läuft gegen alle drei Provider" (#102).
            Assert.Empty(context.Database.GetPendingMigrations());

            var applied = context.Database.GetAppliedMigrations().ToArray();
            Assert.Contains(applied, migration => migration.EndsWith("InitialCreate", StringComparison.Ordinal));
            Assert.Contains(applied, migration => migration.EndsWith("AddDialogLayout", StringComparison.Ordinal));
        }
    }
}
