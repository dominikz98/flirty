using Flirty.Domain;

namespace Flirty.Persistence;

/// <summary>
/// Writing repository for the configuration aggregate (dialog graph) that the
/// admin CRUD handlers (issue #36) need. Deliberately separate from <see cref="IDialogStore"/>: that one
/// delivers the configuration <b>untracked</b> (immutable at runtime), whereas CRUD
/// needs <b>tracked</b> entities to mutate/delete. All GUID keys are assigned
/// application-side; persistence happens bundled via <see cref="SaveChangesAsync"/>.
/// </summary>
internal interface IDialogAdminStore
{
    /// <summary>
    /// Loads the dialog with the given <paramref name="dialogId"/> <b>tracked</b> and without
    /// graph (metadata only). Basis for metadata update, publish/unpublish and deletion
    /// (children are removed via DB cascade).
    /// </summary>
    /// <param name="dialogId">The primary key of the dialog.</param>
    /// <param name="cancellationToken">Token to cancel the query.</param>
    /// <returns>The tracked dialog or <see langword="null"/> if no such id exists.</returns>
    Task<Dialog?> GetDialogAsync(Guid dialogId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the dialog with the given <paramref name="dialogId"/> along with its graph relevant to the
    /// admin CRUD (questions incl. options, transitions, loop markers, triggers and
    /// canvas layout) - <b>untracked</b> and as a split query. Basis for the detail query
    /// (<c>GetDialogQuery</c>).
    /// </summary>
    /// <param name="dialogId">The primary key of the dialog.</param>
    /// <param name="cancellationToken">Token to cancel the query.</param>
    /// <returns>The dialog along with its graph or <see langword="null"/> if no such id exists.</returns>
    Task<Dialog?> GetDialogGraphAsync(Guid dialogId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads all dialogs (metadata only, without graph) <b>untracked</b>, sorted by key and
    /// version. Basis for the dialog list (<c>ListDialogsQuery</c>).
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the query.</param>
    /// <returns>The dialogs in stable order (empty list if none exist).</returns>
    Task<IReadOnlyList<Dialog>> ListDialogsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the question with the given <paramref name="questionId"/> <b>tracked</b> along with its
    /// options. Basis for question update/deletion and the options CRUD (parent resolution).
    /// </summary>
    /// <param name="questionId">The primary key of the question.</param>
    /// <param name="cancellationToken">Token to cancel the query.</param>
    /// <returns>The tracked question along with its options or <see langword="null"/>.</returns>
    Task<Question?> GetQuestionAsync(Guid questionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the transition with the given <paramref name="transitionId"/> <b>tracked</b>.
    /// </summary>
    /// <param name="transitionId">The primary key of the transition.</param>
    /// <param name="cancellationToken">Token to cancel the query.</param>
    /// <returns>The tracked transition or <see langword="null"/>.</returns>
    Task<Transition?> GetTransitionAsync(Guid transitionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads all transitions <b>tracked</b> that reference the question with the given
    /// <paramref name="questionId"/> as source or target question. Basis for the
    /// cleanup of orphaned (FK-less) transitions when a question is deleted.
    /// </summary>
    /// <param name="questionId">The primary key of the question.</param>
    /// <param name="cancellationToken">Token to cancel the query.</param>
    /// <returns>The referencing transitions (empty list if none exist).</returns>
    Task<IReadOnlyList<Transition>> GetTransitionsReferencingQuestionAsync(
        Guid questionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the loop marker with the given <paramref name="loopId"/> <b>tracked</b>.
    /// </summary>
    /// <param name="loopId">The primary key of the loop definition.</param>
    /// <param name="cancellationToken">Token to cancel the query.</param>
    /// <returns>The tracked loop marker or <see langword="null"/>.</returns>
    Task<LoopDefinition?> GetLoopAsync(Guid loopId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads all loop markers <b>tracked</b> that reference the question with the given
    /// <paramref name="questionId"/> as entry or breaking question. Basis for
    /// the cleanup of orphaned (FK-less) markers when a question is deleted.
    /// </summary>
    /// <param name="questionId">The primary key of the question.</param>
    /// <param name="cancellationToken">Token to cancel the query.</param>
    /// <returns>The referencing loop markers (empty list if none exist).</returns>
    Task<IReadOnlyList<LoopDefinition>> GetLoopsReferencingQuestionAsync(
        Guid questionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the trigger definition with the given <paramref name="triggerId"/> <b>tracked</b>.
    /// </summary>
    /// <param name="triggerId">The primary key of the trigger definition.</param>
    /// <param name="cancellationToken">Token to cancel the query.</param>
    /// <returns>The tracked trigger definition or <see langword="null"/>.</returns>
    Task<TriggerDefinition?> GetTriggerAsync(Guid triggerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads all trigger definitions <b>tracked</b> that reference the question with the given
    /// <paramref name="questionId"/> (<see cref="TriggerDefinition.QuestionId"/>). Basis
    /// for the cleanup of orphaned (FK-less) triggers when a question is deleted.
    /// </summary>
    /// <param name="questionId">The primary key of the question.</param>
    /// <param name="cancellationToken">Token to cancel the query.</param>
    /// <returns>The referencing trigger definitions (empty list if none exist).</returns>
    Task<IReadOnlyList<TriggerDefinition>> GetTriggersReferencingQuestionAsync(
        Guid questionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads all layout rows of the dialog with the given <paramref name="dialogId"/>
    /// <b>tracked</b>. Basis for the batch upsert (<c>SetDialogLayoutCommand</c>) and the
    /// reset (<c>ResetDialogLayoutCommand</c>).
    /// </summary>
    /// <param name="dialogId">The primary key of the dialog.</param>
    /// <param name="cancellationToken">Token to cancel the query.</param>
    /// <returns>The layout rows of the dialog (empty list if none are stored).</returns>
    Task<IReadOnlyList<DialogLayout>> GetLayoutAsync(
        Guid dialogId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads all layout rows <b>tracked</b> that reference the element with the given
    /// <paramref name="elementId"/>. Basis for the cleanup of orphaned (FK-less)
    /// positions when a question is deleted.
    /// </summary>
    /// <param name="elementId">The primary key of the element (today always a question).</param>
    /// <param name="cancellationToken">Token to cancel the query.</param>
    /// <returns>The referencing layout rows (empty list if none exist).</returns>
    Task<IReadOnlyList<DialogLayout>> GetLayoutsReferencingElementAsync(
        Guid elementId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Determines the highest assigned version number for the business <paramref name="key"/>. Basis
    /// for <c>CreateDialogVersionCommand</c>, which creates the follow-up version.
    /// </summary>
    /// <param name="key">The business dialog key.</param>
    /// <param name="cancellationToken">Token to cancel the query.</param>
    /// <returns>The highest assigned version or <c>0</c> if the key is unknown.</returns>
    Task<int> GetMaxDialogVersionAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads all <b>published</b> dialogs for the business <paramref name="key"/> except
    /// <paramref name="excludeDialogId"/> - <b>tracked</b>. Basis for
    /// <c>PublishDialogCommand</c> retiring the previously productive version: per key at most
    /// one version should be published, otherwise only the highest would be startable and the rest would
    /// carry a misleading status.
    /// </summary>
    /// <param name="key">The business dialog key.</param>
    /// <param name="excludeDialogId">The id of the version being published (stays untouched).</param>
    /// <param name="cancellationToken">Token to cancel the query.</param>
    /// <returns>The tracked, so-far published sibling versions.</returns>
    Task<IReadOnlyList<Dialog>> GetPublishedVersionsAsync(
        string key, Guid excludeDialogId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts the sessions of the dialog <paramref name="dialogId"/> with status
    /// <see cref="SessionStatus.InProgress"/>. Basis for the deletion guard in
    /// <c>DeleteDialogCommand</c> - a deleted dialog makes its sessions unreadable.
    /// </summary>
    /// <param name="dialogId">The primary key of the dialog.</param>
    /// <param name="cancellationToken">Token to cancel the query.</param>
    /// <returns>The number of running sessions.</returns>
    Task<int> CountActiveSessionsAsync(Guid dialogId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the running sessions (<see cref="SessionStatus.InProgress"/>) of the dialog
    /// <paramref name="dialogId"/> <b>tracked</b>. Basis for
    /// <c>AbandonDialogSessionsCommand</c>, which sets them collectively to
    /// <see cref="SessionStatus.Abandoned"/>.
    /// </summary>
    /// <param name="dialogId">The primary key of the dialog.</param>
    /// <param name="cancellationToken">Token to cancel the query.</param>
    /// <returns>The tracked running sessions (empty list if none exist).</returns>
    Task<IReadOnlyList<DialogSession>> GetActiveSessionsAsync(
        Guid dialogId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether <b>another</b> dialog with the business <paramref name="key"/> already exists.
    /// Used for creation and metadata update; the <b>versioning</b> deliberately bypasses the check,
    /// because multiple versions share the same key (unique index <c>(Key, Version)</c>).
    /// </summary>
    /// <param name="key">The business dialog key to check.</param>
    /// <param name="excludeDialogId">Optionally the id of the dialog excluded from the check (update).</param>
    /// <param name="cancellationToken">Token to cancel the query.</param>
    /// <returns><see langword="true"/> if the key is already taken, otherwise <see langword="false"/>.</returns>
    Task<bool> DialogKeyExistsAsync(
        string key, Guid? excludeDialogId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether in the dialog <paramref name="dialogId"/> <b>another</b> question with the
    /// business <paramref name="key"/> already exists (unique constraint <c>(DialogId, Key)</c>).
    /// </summary>
    /// <param name="dialogId">The id of the dialog.</param>
    /// <param name="key">The business question key to check.</param>
    /// <param name="excludeQuestionId">Optionally the id of the question excluded from the check (update).</param>
    /// <param name="cancellationToken">Token to cancel the query.</param>
    /// <returns><see langword="true"/> if the key is already taken, otherwise <see langword="false"/>.</returns>
    Task<bool> QuestionKeyExistsAsync(
        Guid dialogId, string key, Guid? excludeQuestionId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether in the dialog <paramref name="dialogId"/> <b>another</b> loop marker with
    /// the <paramref name="collectionKey"/> already exists. Without this check the runtime would silently
    /// overwrite the equally named collections (in the expression context the last-built
    /// marker wins) instead of reporting the duplicate assignment.
    /// </summary>
    /// <param name="dialogId">The id of the dialog.</param>
    /// <param name="collectionKey">The collection key to check.</param>
    /// <param name="excludeLoopId">Optionally the id of the marker excluded from the check (update).</param>
    /// <param name="cancellationToken">Token to cancel the query.</param>
    /// <returns><see langword="true"/> if the key is already taken, otherwise <see langword="false"/>.</returns>
    Task<bool> LoopCollectionKeyExistsAsync(
        Guid dialogId, string collectionKey, Guid? excludeLoopId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Takes a newly created entity into tracking (persistence only via <see cref="SaveChangesAsync"/>).</summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="entity">The entity to add.</param>
    void Add<TEntity>(TEntity entity)
        where TEntity : class;

    /// <summary>Marks a tracked entity for deletion (persistence only via <see cref="SaveChangesAsync"/>).</summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="entity">The entity to delete.</param>
    void Remove<TEntity>(TEntity entity)
        where TEntity : class;

    /// <summary>Marks multiple tracked entities for deletion (persistence only via <see cref="SaveChangesAsync"/>).</summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="entities">The entities to delete.</param>
    void RemoveRange<TEntity>(IEnumerable<TEntity> entities)
        where TEntity : class;

    /// <summary>
    /// Writes all changes accumulated in this unit of work in a single bundle to the database.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the save operation.</param>
    /// <returns>A task that completes once the save has happened.</returns>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
