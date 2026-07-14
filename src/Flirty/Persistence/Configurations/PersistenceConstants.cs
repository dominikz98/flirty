namespace Flirty.Persistence.Configurations;

/// <summary>
/// Gemeinsame Konstanten der EF-Core-Konfigurationen.
/// </summary>
internal static class PersistenceConstants
{
    /// <summary>
    /// Maximale Länge indizierter fachlicher Schlüssel-Spalten (z. B. <c>Key</c>,
    /// <c>ExternalUserKey</c>). Begrenzt, damit die Spalten über alle Provider indizierbar bleiben.
    /// </summary>
    internal const int KeyMaxLength = 256;
}
