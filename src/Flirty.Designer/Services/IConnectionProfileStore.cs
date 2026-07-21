using Flirty.Designer.Models;

namespace Flirty.Designer.Services;

/// <summary>
/// Persistente Verwaltung der Connection-Profile des Designers (CRUD + Merken des zuletzt aktivierten
/// Profils). Die Ablage ist bewusst außerhalb der Flirty-Datenbank, weil die Profile ja erst die
/// Verbindung zu dieser Datenbank herstellen (Henne/Ei).
/// </summary>
internal interface IConnectionProfileStore
{
    /// <summary>Liefert alle gespeicherten Profile (Kopien) in Einfügereihenfolge.</summary>
    IReadOnlyList<ConnectionProfile> GetAll();

    /// <summary>Liefert das Profil mit der angegebenen <paramref name="id"/> (Kopie) oder <c>null</c>.</summary>
    ConnectionProfile? Get(string id);

    /// <summary>Legt das Profil an oder aktualisiert es (Abgleich über <see cref="ConnectionProfile.Id"/>).</summary>
    void Save(ConnectionProfile profile);

    /// <summary>Entfernt das Profil mit der angegebenen <paramref name="id"/>, falls vorhanden.</summary>
    void Delete(string id);

    /// <summary>Kennung des zuletzt aktivierten Standardprofils oder <c>null</c>.</summary>
    string? DefaultProfileId { get; }

    /// <summary>Merkt sich das angegebene Profil als Standard (oder löscht die Markierung bei <c>null</c>).</summary>
    void SetDefault(string? id);
}
