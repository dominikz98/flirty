using Flirty.Designer.Models;

namespace Flirty.Designer.Services;

/// <summary>
/// Holds the currently active connection profile of the designer. Registered as <c>Scoped</c> this corresponds
/// in server-interactive Blazor to a lifetime per circuit. The active profile determines against which
/// database the <see cref="FlirtyDesignerDbContextFactory"/> (and thus the admin commands since #38) work.
/// </summary>
internal sealed class ActiveConnectionProfile
{
    private readonly IConnectionProfileStore _store;
    private ConnectionProfile? _current;
    private bool _initialized;

    /// <summary>Creates the state; the start profile is read lazily from the store default.</summary>
    /// <param name="store">The profile store from which the default profile stems.</param>
    public ActiveConnectionProfile(IConnectionProfileStore store)
    {
        _store = store;
    }

    /// <summary>The active profile or <c>null</c> if none has (yet) been activated.</summary>
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

    /// <summary>Activates the given profile and remembers it as the store default.</summary>
    /// <param name="profile">The profile to activate.</param>
    public void Activate(ConnectionProfile profile)
    {
        Adopt(profile);
        _store.SetDefault(profile.Id);
    }

    /// <summary>
    /// Takes over the given profile into <b>this</b> scope, <b>without</b> changing the store default.
    /// Intended for the <see cref="FlirtyAdminGateway"/>, which runs every admin operation in a fresh
    /// DI scope and must pass the profile of the calling circuit through into it (the default
    /// in the store is not suitable for that: several circuits can have different profiles active).
    /// </summary>
    /// <param name="profile">The profile to take over.</param>
    public void Adopt(ConnectionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        _current = profile;
        _initialized = true;
    }

    /// <summary>
    /// Releases the active profile in <b>this</b> scope – intended for deleting the active profile.
    /// Without this step the circuit would keep the deleted profile and the admin operations
    /// would run against a connection that no longer exists in the management.
    /// </summary>
    /// <remarks>
    /// Deliberately sets <c>_initialized</c>, so that <see cref="Current"/> does not read the (by now
    /// removed) store default again, but stays <see langword="null"/>.
    /// </remarks>
    public void Clear()
    {
        _current = null;
        _initialized = true;
    }
}
