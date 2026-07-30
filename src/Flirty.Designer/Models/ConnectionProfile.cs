using System.ComponentModel.DataAnnotations;
using Flirty.Persistence;

namespace Flirty.Designer.Models;

/// <summary>
/// A named database connection profile of the designer: provider + connection string.
/// Deliberately mutable (settable properties), so that the Blazor <c>EditForm</c> can bind directly to it.
/// </summary>
internal sealed class ConnectionProfile
{
    /// <summary>Stable technical identifier of the profile.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Display name of the profile (intended to be unique in the designer, but not enforced).</summary>
    [Required(ErrorMessage = "Please enter a name.")]
    public string Name { get; set; } = string.Empty;

    /// <summary>The database provider to use.</summary>
    public FlirtyDatabaseProvider Provider { get; set; } = FlirtyDatabaseProvider.Sqlite;

    /// <summary>The connection string for the chosen provider (may contain secrets).</summary>
    [Required(ErrorMessage = "Please enter a connection string.")]
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Creates an independent copy (for safe editing in the form).</summary>
    public ConnectionProfile Clone() => new()
    {
        Id = Id,
        Name = Name,
        Provider = Provider,
        ConnectionString = ConnectionString,
    };
}
