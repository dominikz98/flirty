using Flirty.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flirty.Mcp;

/// <summary>
/// The three database-level operations behind the <c>flirty_db_*</c> tools, in one place so their error
/// handling is reviewable against the designer's <c>ConnectionProfileOperations</c> side by side.
/// </summary>
/// <remarks>
/// <para>
/// These are the only operations in the package that do not go through <c>ISender</c>: the engine has no
/// command for "is this database reachable?", and the designer reaches for
/// <c>DatabaseFacade</c> here just the same.
/// </para>
/// <para>
/// The error handling deliberately differs per operation, and the split is the interesting part.
/// <see cref="TestConnectionAsync"/> mirrors the designer including its try/catch and <b>never</b>
/// throws – "not reachable" is the answer it was asked for. The other two <i>cannot</i> answer when the
/// database is silent, so they raise a <see cref="FlirtyMcpDatabaseException"/> and the client sees an
/// error result. That closes the one designer operation whose error handling was never exercised:
/// <c>ConnectionProfileOperations.GetPendingMigrationsAsync</c> has no try/catch and no UI caller, so
/// over MCP an unreachable database would have been an unhandled exception and reached the client as the
/// filter's generic 500.
/// </para>
/// <para>
/// The <c>when</c> clauses matter: an <see cref="OperationCanceledException"/> is a client disconnect,
/// which the SDK owns. Swallowing it here would report a cancelled request as a database failure.
/// </para>
/// </remarks>
internal static class FlirtyMcpDatabaseOperations
{
    /// <summary>Checks whether the database of the current target answers.</summary>
    /// <param name="context">The context of the resolved target.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>The outcome; a failure is a result, not an exception.</returns>
    internal static async Task<FlirtyConnectionTest> TestConnectionAsync(
        FlirtyDbContext context, CancellationToken cancellationToken)
    {
        try
        {
            return await context.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false)
                ? new FlirtyConnectionTest(true, "Connection successful.")
                : new FlirtyConnectionTest(false, "Connection failed: the database is not reachable.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new FlirtyConnectionTest(
                false, $"Connection failed: {FlirtyMcpDatabaseException.Describe(exception)}");
        }
    }

    /// <summary>Reads the EF Core migrations not yet applied to the current target.</summary>
    /// <param name="context">The context of the resolved target.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The pending migration ids, in the order EF Core would apply them.</returns>
    /// <exception cref="FlirtyMcpDatabaseException">The database did not answer.</exception>
    internal static async Task<FlirtyPendingMigrations> GetPendingMigrationsAsync(
        FlirtyDbContext context, CancellationToken cancellationToken)
    {
        try
        {
            var pending = await context.Database
                .GetPendingMigrationsAsync(cancellationToken)
                .ConfigureAwait(false);

            return new FlirtyPendingMigrations([.. pending]);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw FlirtyMcpDatabaseException.For(exception);
        }
    }

    /// <summary>Applies the pending EF Core migrations to the current target.</summary>
    /// <remarks>
    /// The pending list is captured <b>before</b> migrating, because afterwards there is nothing left to
    /// report – <c>MigrateAsync</c> itself returns nothing.
    /// </remarks>
    /// <param name="context">The context of the resolved target.</param>
    /// <param name="cancellationToken">Cancels the migration.</param>
    /// <returns>The migration ids that were applied; empty when the database was already up to date.</returns>
    /// <exception cref="FlirtyMcpDatabaseException">The database did not answer, or a migration failed.</exception>
    internal static async Task<FlirtyMigrationsApplied> MigrateAsync(
        FlirtyDbContext context, CancellationToken cancellationToken)
    {
        try
        {
            var pending = await context.Database
                .GetPendingMigrationsAsync(cancellationToken)
                .ConfigureAwait(false);
            var applied = pending.ToList();

            await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);

            return new FlirtyMigrationsApplied(applied);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw FlirtyMcpDatabaseException.For(exception);
        }
    }
}
