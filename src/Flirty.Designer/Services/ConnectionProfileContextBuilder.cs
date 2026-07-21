using Flirty.Designer.Models;
using Flirty.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flirty.Designer.Services;

/// <summary>
/// Baut aus einem <see cref="ConnectionProfile"/> einen <see cref="FlirtyDbContext"/> – die zentrale
/// Naht zwischen Profil-Auswahl und EF Core. Nutzt das öffentliche Core-Mapping
/// <see cref="FlirtyDatabaseProviderExtensions.UseFlirtyProvider(DbContextOptionsBuilder, FlirtyDatabaseProvider, string)"/>,
/// damit Provider und passende <c>MigrationsAssembly</c> nicht dupliziert werden.
/// </summary>
internal static class ConnectionProfileContextBuilder
{
    /// <summary>Erzeugt einen frischen, nicht-geteilten <see cref="FlirtyDbContext"/> für das Profil.</summary>
    /// <param name="profile">Das zu öffnende Verbindungsprofil.</param>
    public static FlirtyDbContext Create(ConnectionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var builder = new DbContextOptionsBuilder<FlirtyDbContext>();
        builder.UseFlirtyProvider(profile.Provider, profile.ConnectionString);
        return new FlirtyDbContext(builder.Options);
    }
}
