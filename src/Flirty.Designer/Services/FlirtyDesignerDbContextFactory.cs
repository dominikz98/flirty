using Flirty.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flirty.Designer.Services;

/// <summary>
/// The designer's <see cref="IDbContextFactory{TContext}"/> implementation, which opens the
/// <see cref="FlirtyDbContext"/> against the currently <b>active</b> connection profile (multi-DB, #37).
/// This makes the admin commands (via <c>ISender</c>, since #38) run automatically against the chosen database.
/// </summary>
internal sealed class FlirtyDesignerDbContextFactory : IDbContextFactory<FlirtyDbContext>
{
    private readonly ActiveConnectionProfile _active;

    /// <summary>Creates the factory.</summary>
    /// <param name="active">Access to the active connection profile.</param>
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
                "No connection profile is active. Please activate a profile under \"Connections\" first.");

        return ConnectionProfileContextBuilder.Create(profile);
    }
}
