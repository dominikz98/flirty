using Flirty.Designer.Models;
using Microsoft.EntityFrameworkCore;

namespace Flirty.Designer.Services;

/// <summary>
/// Database operations for an <b>arbitrary</b> connection profile (independent of the active profile):
/// connection test and migration status/application. Backs the "Test connection" and "Migrate" buttons
/// of the profile management. Uses the same pattern as
/// <c>Flirty.Hosting.FlirtyMigrationHostedService</c> (<c>Database.MigrateAsync()</c>), but on demand
/// against the chosen profile instead of at host start.
/// </summary>
internal sealed class ConnectionProfileOperations
{
    /// <summary>Checks whether a connection can be established with the profile.</summary>
    /// <param name="profile">The profile to test.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ConnectionTestResult> TestConnectionAsync(
        ConnectionProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        try
        {
            await using var context = ConnectionProfileContextBuilder.Create(profile);
            var canConnect = await context.Database.CanConnectAsync(cancellationToken);
            return canConnect
                ? ConnectionTestResult.Ok("Connection successful.")
                : ConnectionTestResult.Fail("Connection failed: the database is not reachable.");
        }
        catch (Exception ex)
        {
            return ConnectionTestResult.Fail($"Connection failed: {ex.Message}");
        }
    }

    /// <summary>Determines the migrations of the profile that have not yet been applied.</summary>
    /// <param name="profile">The profile to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<string>> GetPendingMigrationsAsync(
        ConnectionProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        await using var context = ConnectionProfileContextBuilder.Create(profile);
        var pending = await context.Database.GetPendingMigrationsAsync(cancellationToken);
        return pending.ToList();
    }

    /// <summary>Applies all pending migrations to the profile's database.</summary>
    /// <param name="profile">The profile to migrate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<MigrationResult> ApplyMigrationsAsync(
        ConnectionProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        try
        {
            await using var context = ConnectionProfileContextBuilder.Create(profile);
            var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
            await context.Database.MigrateAsync(cancellationToken);
            return MigrationResult.Ok(pending);
        }
        catch (Exception ex)
        {
            return MigrationResult.Fail($"Migration failed: {ex.Message}");
        }
    }
}

/// <summary>Result of a connection test.</summary>
/// <param name="Success">Whether the connection was successful.</param>
/// <param name="Message">Human-readable message (success or error detail).</param>
internal sealed record ConnectionTestResult(bool Success, string Message)
{
    /// <summary>Creates a success result.</summary>
    public static ConnectionTestResult Ok(string message) => new(true, message);

    /// <summary>Creates an error result.</summary>
    public static ConnectionTestResult Fail(string message) => new(false, message);
}

/// <summary>Result of applying migrations.</summary>
/// <param name="Success">Whether the migration was successful.</param>
/// <param name="AppliedMigrations">The migrations that were (previously pending and now) applied.</param>
/// <param name="Error">Error message on failure, otherwise <c>null</c>.</param>
internal sealed record MigrationResult(bool Success, IReadOnlyList<string> AppliedMigrations, string? Error)
{
    /// <summary>Creates a success result with the applied migrations.</summary>
    public static MigrationResult Ok(IReadOnlyList<string> applied) => new(true, applied, null);

    /// <summary>Creates an error result.</summary>
    public static MigrationResult Fail(string error) => new(false, [], error);
}
