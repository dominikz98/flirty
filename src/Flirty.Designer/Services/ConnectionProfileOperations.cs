using Flirty.Designer.Models;
using Microsoft.EntityFrameworkCore;

namespace Flirty.Designer.Services;

/// <summary>
/// Datenbankoperationen für ein <b>beliebiges</b> Connection-Profil (unabhängig vom aktiven Profil):
/// Verbindungstest und Migrations-Status/-Anwendung. Bildet die „Test-Connection"- und „Migrate"-Buttons
/// der Profil-Verwaltung ab. Verwendet dasselbe Muster wie
/// <c>Flirty.Hosting.FlirtyMigrationHostedService</c> (<c>Database.MigrateAsync()</c>), aber on-demand
/// gegen das gewählte Profil statt beim Host-Start.
/// </summary>
internal sealed class ConnectionProfileOperations
{
    /// <summary>Prüft, ob mit dem Profil eine Verbindung aufgebaut werden kann.</summary>
    /// <param name="profile">Das zu testende Profil.</param>
    /// <param name="cancellationToken">Abbruch-Token.</param>
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
                ? ConnectionTestResult.Ok("Verbindung erfolgreich.")
                : ConnectionTestResult.Fail("Verbindung fehlgeschlagen: Die Datenbank ist nicht erreichbar.");
        }
        catch (Exception ex)
        {
            return ConnectionTestResult.Fail($"Verbindung fehlgeschlagen: {ex.Message}");
        }
    }

    /// <summary>Ermittelt die noch nicht angewendeten Migrationen des Profils.</summary>
    /// <param name="profile">Das zu prüfende Profil.</param>
    /// <param name="cancellationToken">Abbruch-Token.</param>
    public async Task<IReadOnlyList<string>> GetPendingMigrationsAsync(
        ConnectionProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        await using var context = ConnectionProfileContextBuilder.Create(profile);
        var pending = await context.Database.GetPendingMigrationsAsync(cancellationToken);
        return pending.ToList();
    }

    /// <summary>Wendet alle ausstehenden Migrationen auf die Datenbank des Profils an.</summary>
    /// <param name="profile">Das zu migrierende Profil.</param>
    /// <param name="cancellationToken">Abbruch-Token.</param>
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
            return MigrationResult.Fail($"Migration fehlgeschlagen: {ex.Message}");
        }
    }
}

/// <summary>Ergebnis eines Verbindungstests.</summary>
/// <param name="Success">Ob die Verbindung erfolgreich war.</param>
/// <param name="Message">Menschlich lesbare Meldung (Erfolg oder Fehlerdetail).</param>
internal sealed record ConnectionTestResult(bool Success, string Message)
{
    /// <summary>Erzeugt ein Erfolgs-Ergebnis.</summary>
    public static ConnectionTestResult Ok(string message) => new(true, message);

    /// <summary>Erzeugt ein Fehler-Ergebnis.</summary>
    public static ConnectionTestResult Fail(string message) => new(false, message);
}

/// <summary>Ergebnis einer Migrations-Anwendung.</summary>
/// <param name="Success">Ob die Migration erfolgreich war.</param>
/// <param name="AppliedMigrations">Die (zuvor ausstehenden und nun) angewendeten Migrationen.</param>
/// <param name="Error">Fehlermeldung bei Misserfolg, sonst <c>null</c>.</param>
internal sealed record MigrationResult(bool Success, IReadOnlyList<string> AppliedMigrations, string? Error)
{
    /// <summary>Erzeugt ein Erfolgs-Ergebnis mit den angewendeten Migrationen.</summary>
    public static MigrationResult Ok(IReadOnlyList<string> applied) => new(true, applied, null);

    /// <summary>Erzeugt ein Fehler-Ergebnis.</summary>
    public static MigrationResult Fail(string error) => new(false, [], error);
}
