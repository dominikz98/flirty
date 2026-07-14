namespace Flirty.Domain;

/// <summary>
/// Beschreibt den Lebenszyklus-Status einer <see cref="DialogSession"/>.
/// </summary>
public enum SessionStatus
{
    /// <summary>Der Dialog läuft und kann fortgesetzt (resumed) werden.</summary>
    InProgress = 0,

    /// <summary>Der Dialog wurde vollständig abgeschlossen.</summary>
    Completed = 1,

    /// <summary>Der Dialog wurde abgebrochen und nicht abgeschlossen.</summary>
    Abandoned = 2,
}
