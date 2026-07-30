using Flirty.Designer.Models;
using Flirty.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flirty.Designer.Services;

/// <summary>
/// Builds a <see cref="FlirtyDbContext"/> from a <see cref="ConnectionProfile"/> – the central seam
/// between profile selection and EF Core. Uses the public core mapping
/// <see cref="FlirtyDatabaseProviderExtensions.UseFlirtyProvider(DbContextOptionsBuilder, FlirtyDatabaseProvider, string)"/>,
/// so that provider and matching <c>MigrationsAssembly</c> are not duplicated.
/// </summary>
internal static class ConnectionProfileContextBuilder
{
    /// <summary>Creates a fresh, non-shared <see cref="FlirtyDbContext"/> for the profile.</summary>
    /// <param name="profile">The connection profile to open.</param>
    public static FlirtyDbContext Create(ConnectionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var builder = new DbContextOptionsBuilder<FlirtyDbContext>();
        builder.UseFlirtyProvider(profile.Provider, profile.ConnectionString);
        return new FlirtyDbContext(builder.Options);
    }
}
