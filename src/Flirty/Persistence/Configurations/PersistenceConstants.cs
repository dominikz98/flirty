namespace Flirty.Persistence.Configurations;

/// <summary>
/// Shared constants of the EF Core configurations.
/// </summary>
internal static class PersistenceConstants
{
    /// <summary>
    /// Maximum length of indexed business key columns (e.g. <c>Key</c>,
    /// <c>ExternalUserKey</c>). Bounded so the columns stay indexable across all providers.
    /// </summary>
    internal const int KeyMaxLength = 256;
}
