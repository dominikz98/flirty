using Flirty.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flirty.Designer.Services;

/// <summary>
/// <see cref="IDbContextFactory{TContext}"/> implementation of the designer that opens the
/// <see cref="FlirtyDbContext"/> against the currently <b>active</b> connection profile (multi-DB, #37).
/// With that the admin commands (via <c>ISender</c>, from #38) automatically run against the chosen database.
/// </summary>
internal sealed class FlirtyDesignerDbContextFactory : IDbContextFactory<FlirtyDbContext>
{
    private readonly ActiveConnectionProfile _active;

    /// <summary>Creates the factory.</summary>
    /// <param name="active">The access to the active connection profile.</param>
    public FlirtyDesignerDbContextFactory(ActiveConnectionProfile active)
    {
        _active = active;
    }

    /// <summary>
    /// Creates a <see cref="FlirtyDbContext"/> for the active profile.
    /// </summary>
    /// <returns>A new context to be disposed by the caller.</returns>
    /// <exception cref="InvalidOperationException">No profile is active.</exception>
    public FlirtyDbContext CreateDbContext()
    {
        var profile = _active.Current
            ?? throw new InvalidOperationException(
                "Es ist kein Connection-Profil aktiv. Bitte zuerst unter „Verbindungen“ ein Profil aktivieren.");

        return ConnectionProfileContextBuilder.Create(profile);
    }
}
