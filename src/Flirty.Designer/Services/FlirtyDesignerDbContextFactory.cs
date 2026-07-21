using Flirty.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flirty.Designer.Services;

/// <summary>
/// <see cref="IDbContextFactory{TContext}"/>-Implementierung des Designers, die den
/// <see cref="FlirtyDbContext"/> gegen das jeweils <b>aktive</b> Connection-Profil öffnet (Multi-DB, #37).
/// Damit laufen die Admin-Commands (via <c>ISender</c>, ab #38) automatisch gegen die gewählte Datenbank.
/// </summary>
internal sealed class FlirtyDesignerDbContextFactory : IDbContextFactory<FlirtyDbContext>
{
    private readonly ActiveConnectionProfile _active;

    /// <summary>Erstellt die Factory.</summary>
    /// <param name="active">Der Zugriff auf das aktive Connection-Profil.</param>
    public FlirtyDesignerDbContextFactory(ActiveConnectionProfile active)
    {
        _active = active;
    }

    /// <summary>
    /// Erzeugt einen <see cref="FlirtyDbContext"/> für das aktive Profil.
    /// </summary>
    /// <returns>Ein neuer, vom Aufrufer zu entsorgender Kontext.</returns>
    /// <exception cref="InvalidOperationException">Es ist kein Profil aktiv.</exception>
    public FlirtyDbContext CreateDbContext()
    {
        var profile = _active.Current
            ?? throw new InvalidOperationException(
                "Es ist kein Connection-Profil aktiv. Bitte zuerst unter „Verbindungen“ ein Profil aktivieren.");

        return ConnectionProfileContextBuilder.Create(profile);
    }
}
