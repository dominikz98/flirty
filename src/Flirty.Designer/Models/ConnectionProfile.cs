using System.ComponentModel.DataAnnotations;
using Flirty.Persistence;

namespace Flirty.Designer.Models;

/// <summary>
/// Ein benanntes Datenbank-Verbindungsprofil des Designers: Provider + Verbindungszeichenfolge.
/// Bewusst veränderbar (settable Properties), damit die Blazor-<c>EditForm</c> direkt daran binden kann.
/// </summary>
internal sealed class ConnectionProfile
{
    /// <summary>Stabile technische Kennung des Profils.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Anzeigename des Profils (im Designer eindeutig gedacht, aber nicht erzwungen).</summary>
    [Required(ErrorMessage = "Bitte einen Namen angeben.")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Der zu verwendende Datenbank-Provider.</summary>
    public FlirtyDatabaseProvider Provider { get; set; } = FlirtyDatabaseProvider.Sqlite;

    /// <summary>Die Verbindungszeichenfolge für den gewählten Provider (kann Secrets enthalten).</summary>
    [Required(ErrorMessage = "Bitte eine Verbindungszeichenfolge angeben.")]
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Erzeugt eine unabhängige Kopie (zum gefahrlosen Bearbeiten im Formular).</summary>
    public ConnectionProfile Clone() => new()
    {
        Id = Id,
        Name = Name,
        Provider = Provider,
        ConnectionString = ConnectionString,
    };
}
