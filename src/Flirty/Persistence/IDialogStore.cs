using Flirty.Domain;

namespace Flirty.Persistence;

/// <summary>
/// Repository over the <see cref="FlirtyDbContext"/>: encapsulates the load and save operations
/// that the runtime layer (start/resume/submit/edit, issue #25) needs, and keeps the
/// EF Core context out of the Mediator handlers. The configuration layer (dialog graph) is
/// deliberately delivered <b>untracked</b> (immutable at runtime), the runtime layer
/// (<see cref="DialogSession"/>) however <b>tracked</b>, so that mutations are persisted via
/// <see cref="SaveChangesAsync"/>.
/// </summary>
internal interface IDialogStore
{
    /// <summary>
    /// Loads the highest <b>published</b> version of the dialog with the business
    /// <paramref name="key"/> along with the full configuration graph (questions incl. options,
    /// transitions, loops, triggers). Basis for <c>StartDialogCommand</c>.
    /// </summary>
    /// <param name="key">The business, stable key of the dialog.</param>
    /// <param name="cancellationToken">Token to cancel the query.</param>
    /// <returns>The published dialog with the highest version or
    /// <see langword="null"/> if no published dialog with this key exists.</returns>
    Task<Dialog?> GetPublishedDialogAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the dialog with the given <paramref name="dialogId"/> - that is, the exact version pinned by
    /// a session - along with the full graph, <b>regardless of the
    /// publication status</b>. Basis for resume/submit/edit (pinned dialog version).
    /// </summary>
    /// <param name="dialogId">The primary key of the concrete dialog version.</param>
    /// <param name="cancellationToken">Token to cancel the query.</param>
    /// <returns>The dialog along with its graph or <see langword="null"/> if no such id exists.</returns>
    Task<Dialog?> GetDialogAsync(Guid dialogId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the session with the given <paramref name="sessionId"/> along with its answers.
    /// The session is returned <b>tracked</b> so that subsequent mutations (new answer,
    /// status change, current question) are saved via <see cref="SaveChangesAsync"/>.
    /// </summary>
    /// <param name="sessionId">The primary key of the session.</param>
    /// <param name="cancellationToken">Token to cancel the query.</param>
    /// <returns>The tracked session along with its answers or <see langword="null"/>
    /// if no such session exists.</returns>
    Task<DialogSession?> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the most recently started <b>running</b> (<see cref="SessionStatus.InProgress"/>) session
    /// of a user (<paramref name="externalUserKey"/>) for the given
    /// <paramref name="dialogId"/> along with its answers - <b>tracked</b>. Basis for the
    /// resume-or-new decision in <c>StartDialogCommand</c>.
    /// </summary>
    /// <param name="dialogId">The id of the concrete dialog version.</param>
    /// <param name="externalUserKey">The business user key of the host app.</param>
    /// <param name="cancellationToken">Token to cancel the query.</param>
    /// <returns>The tracked running session or <see langword="null"/> if none exists.</returns>
    Task<DialogSession?> FindActiveSessionAsync(
        Guid dialogId, string externalUserKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the trigger definitions of the dialog the session <paramref name="sessionId"/>
    /// belongs to, filtered on the given <paramref name="scope"/> - <b>untracked</b>. Basis for
    /// the delivery of the triggers configured in the designer (<c>WebhookNotificationHandler</c>, #42).
    /// </summary>
    /// <remarks>
    /// Deliberately <b>one</b> slim query over the foreign-key index instead of "first load the session, then
    /// the dialog graph": the handler runs synchronously in the scope of the triggering command, and the
    /// notifications (except the start) carry no <c>DialogId</c>.
    /// </remarks>
    /// <param name="sessionId">The primary key of the triggering session.</param>
    /// <param name="scope">The point in time at which the trigger fired.</param>
    /// <param name="cancellationToken">Token to cancel the query.</param>
    /// <returns>The matching trigger definitions (empty list if none exist).</returns>
    Task<IReadOnlyList<TriggerDefinition>> GetTriggersForSessionAsync(
        Guid sessionId, TriggerScope scope, CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes a newly created <paramref name="session"/> (including its first answers) into
    /// tracking. Persistence only happens with <see cref="SaveChangesAsync"/>.
    /// Deliberately synchronous: all GUID keys are assigned application-side (no
    /// DB value generation), so <c>AddAsync</c> is not required.
    /// </summary>
    /// <param name="session">The session to add.</param>
    void AddSession(DialogSession session);

    /// <summary>
    /// Writes all changes accumulated in this unit of work - new session
    /// or mutated, tracked session along with answers - in a single bundle to the database.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the save operation.</param>
    /// <returns>A task that completes once the save has happened.</returns>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
