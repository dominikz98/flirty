using Flirty.Designer.Models;

namespace Flirty.Designer.Services;

/// <summary>
/// Persistent management of the designer's connection profiles (CRUD + remembering the last activated
/// profile). The storage is deliberately outside the Flirty database, because the profiles are what first
/// establish the connection to that database (chicken/egg).
/// </summary>
internal interface IConnectionProfileStore
{
    /// <summary>Returns all stored profiles (copies) in insertion order.</summary>
    IReadOnlyList<ConnectionProfile> GetAll();

    /// <summary>Returns the profile with the given <paramref name="id"/> (copy) or <c>null</c>.</summary>
    ConnectionProfile? Get(string id);

    /// <summary>Creates or updates the profile (matched via <see cref="ConnectionProfile.Id"/>).</summary>
    void Save(ConnectionProfile profile);

    /// <summary>Removes the profile with the given <paramref name="id"/>, if present.</summary>
    void Delete(string id);

    /// <summary>Identifier of the last activated default profile or <c>null</c>.</summary>
    string? DefaultProfileId { get; }

    /// <summary>Remembers the given profile as the default (or clears the marking on <c>null</c>).</summary>
    void SetDefault(string? id);
}
