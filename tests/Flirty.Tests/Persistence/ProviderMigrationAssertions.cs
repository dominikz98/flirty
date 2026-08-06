using Flirty.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flirty.Tests.Persistence;

/// <summary>
/// Shared assertion for the cross-provider migration tests (#19): applies the provider-specific
/// <c>InitialCreate</c> migration via <c>Database.Migrate()</c> (so the database is created against
/// the respective provider) and then checks a complete aggregate round trip. Used identically by
/// SQLite, PostgreSQL and SQL Server.
/// </summary>
internal static class ProviderMigrationAssertions
{
    /// <summary>
    /// Migrates the database from the given <paramref name="options"/>, stores a complete dialog
    /// aggregate and loads it again with all navigations. Verifies that the schema came from the
    /// migration and not from <c>EnsureCreated</c>.
    /// </summary>
    /// <param name="options">Preconfigured options incl. provider and migrations assembly.</param>
    public static void MigrateCreatesSchemaAndRoundTripsAggregate(DbContextOptions<FlirtyDbContext> options)
    {
        var dialogId = Guid.NewGuid();

        using (var context = new FlirtyDbContext(options))
        {
            // Applies the provider-specific InitialCreate migration -> the schema comes into being.
            context.Database.Migrate();

            var dialog = TestDialogFactory.BuildFullDialog(dialogId, out _);

            // Added here rather than in the factory: only this test needs the column, and the factory
            // is shared by a dozen others that would then all carry an unrelated custom type.
            dialog.Questions.Add(new Flirty.Domain.Question
            {
                Id = Guid.NewGuid(),
                DialogId = dialogId,
                Key = "colour",
                Text = "Which colour?",
                Type = Flirty.Domain.QuestionType.Json,
                Order = 1,
                CustomTypeKey = "color",
            });

            context.Dialogs.Add(dialog);
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
            var question = Assert.Single(loaded.Questions, candidate => candidate.Key == "role");
            Assert.Equal(2, question.Options.Count);
            Assert.Null(question.CustomTypeKey);

            var custom = Assert.Single(loaded.Questions, candidate => candidate.Key == "colour");
            Assert.Equal("color", custom.CustomTypeKey);
            Assert.Single(loaded.Transitions);
            Assert.Single(loaded.Loops);
            Assert.Single(loaded.Triggers);

            var layout = Assert.Single(loaded.Layout);
            Assert.Equal(320, layout.X);
            Assert.Equal(160, layout.Y);
        }

        using (var context = new FlirtyDbContext(options))
        {
            // Proves that the schema comes from applied migrations (not from EnsureCreated) and that
            // this provider's migration sets are complete – the acceptance criterion "the migration
            // runs against all three providers" (#102) hangs here.
            Assert.Empty(context.Database.GetPendingMigrations());

            var applied = context.Database.GetAppliedMigrations().ToArray();
            Assert.Contains(applied, migration => migration.EndsWith("InitialCreate", StringComparison.Ordinal));
            Assert.Contains(applied, migration => migration.EndsWith("AddDialogLayout", StringComparison.Ordinal));
            Assert.Contains(
                applied,
                migration => migration.EndsWith("AddQuestionCustomTypeKey", StringComparison.Ordinal));
        }
    }
}
