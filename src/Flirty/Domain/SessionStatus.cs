namespace Flirty.Domain;

/// <summary>
/// Describes the lifecycle status of a <see cref="DialogSession"/>.
/// </summary>
public enum SessionStatus
{
    /// <summary>The dialog is running and can be resumed.</summary>
    InProgress = 0,

    /// <summary>The dialog was fully completed.</summary>
    Completed = 1,

    /// <summary>The dialog was abandoned and not completed.</summary>
    Abandoned = 2,
}
