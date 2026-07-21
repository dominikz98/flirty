using Flirty.Designer.Models;

namespace Flirty.Designer.Services;

/// <summary>
/// Hält das aktuell aktive Connection-Profil des Designers. Als <c>Scoped</c> registriert entspricht das
/// im server-interaktiven Blazor einer Lebensdauer pro Circuit. Das aktive Profil bestimmt, gegen welche
/// Datenbank die <see cref="FlirtyDesignerDbContextFactory"/> (und damit die Admin-Commands ab #38) arbeiten.
/// </summary>
internal sealed class ActiveConnectionProfile
{
    private readonly IConnectionProfileStore _store;
    private ConnectionProfile? _current;
    private bool _initialized;

    /// <summary>Erstellt den Zustand; das Startprofil wird verzögert aus dem Store-Default gelesen.</summary>
    /// <param name="store">Der Profil-Store, aus dem das Standardprofil stammt.</param>
    public ActiveConnectionProfile(IConnectionProfileStore store)
    {
        _store = store;
    }

    /// <summary>Das aktive Profil oder <c>null</c>, wenn (noch) keins aktiviert wurde.</summary>
    public ConnectionProfile? Current
    {
        get
        {
            if (!_initialized)
            {
                var defaultId = _store.DefaultProfileId;
                _current = defaultId is null ? null : _store.Get(defaultId);
                _initialized = true;
            }

            return _current;
        }
    }

    /// <summary>Aktiviert das angegebene Profil und merkt es als Store-Default.</summary>
    /// <param name="profile">Das zu aktivierende Profil.</param>
    public void Activate(ConnectionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        _current = profile;
        _initialized = true;
        _store.SetDefault(profile.Id);
    }
}
