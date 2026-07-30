namespace Flirty.Domain;

/// <summary>
/// The runtime state of a <see cref="Dialog"/> being run by a user and at the same time the
/// aggregate root of the runtime layer. Enables resuming via <see cref="CurrentQuestionId"/>. The
/// session pins the dialog version with <see cref="DialogVersion"/> and is thereby decoupled from the
/// editable configuration graph.
/// </summary>
public sealed class DialogSession
{
    /// <summary>Unique primary key of the session.</summary>
    public Guid Id { get; set; }

    /// <summary>Reference to the running <see cref="Dialog"/> (<see cref="Dialog.Id"/>).</summary>
    public Guid DialogId { get; set; }

    /// <summary>
    /// The <see cref="Dialog.Version"/> pinned at start time so that later changes to the dialog do not
    /// break this session.
    /// </summary>
    public int DialogVersion { get; set; }

    /// <summary>Business key of the user/context of the host app (e.g. user id).</summary>
    public required string ExternalUserKey { get; set; }

    /// <summary>The current lifecycle status of the session.</summary>
    public SessionStatus Status { get; set; }

    /// <summary>
    /// Reference to the currently open question (<see cref="Question.Id"/>) for resume;
    /// <see langword="null"/> once the dialog is completed.
    /// </summary>
    public Guid? CurrentQuestionId { get; set; }

    /// <summary>Timestamp of the session's start.</summary>
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>
    /// Timestamp at which the session ended - completed (<see cref="SessionStatus.Completed"/>) or
    /// abandoned (<see cref="SessionStatus.Abandoned"/>) - or <see langword="null"/> while it is running.
    /// </summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>The answers given during the session.</summary>
    public ICollection<SessionAnswer> Answers { get; set; } = [];
}
