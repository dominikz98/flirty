using Flirty.Domain;
using Microsoft.EntityFrameworkCore;

namespace Flirty.Persistence;

/// <summary>
/// Default implementation of <see cref="IDialogAdminStore"/> over a scoped
/// <see cref="FlirtyDbContext"/>. Load operations for mutation/deletion return <b>tracked</b>
/// entities (so that <see cref="SaveChangesAsync"/> takes effect); purely reading queries (list, detail graph)
/// run <b>untracked</b>. Child entities are addressed via <c>Set&lt;T&gt;()</c>.
/// </summary>
internal sealed class DialogAdminStore : IDialogAdminStore
{
    private readonly FlirtyDbContext _context;

    /// <summary>Creates the store over the given <see cref="FlirtyDbContext"/>.</summary>
    /// <param name="context">The scoped EF Core context of the Flirty engine.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    public DialogAdminStore(FlirtyDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <inheritdoc />
    public Task<Dialog?> GetDialogAsync(Guid dialogId, CancellationToken cancellationToken = default)
        => _context.Dialogs.FirstOrDefaultAsync(dialog => dialog.Id == dialogId, cancellationToken);

    /// <inheritdoc />
    public Task<Dialog?> GetDialogGraphAsync(Guid dialogId, CancellationToken cancellationToken = default)
        => _context.Dialogs
            .AsNoTracking()
            .AsSplitQuery()
            .Include(dialog => dialog.Questions).ThenInclude(question => question.Options)
            .Include(dialog => dialog.Transitions)
            .Include(dialog => dialog.Loops)
            .Include(dialog => dialog.Triggers)
            .Include(dialog => dialog.Layout)
            .FirstOrDefaultAsync(dialog => dialog.Id == dialogId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Dialog>> ListDialogsAsync(CancellationToken cancellationToken = default)
        => await _context.Dialogs
            .AsNoTracking()
            .OrderBy(dialog => dialog.Key)
            .ThenBy(dialog => dialog.Version)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public Task<Question?> GetQuestionAsync(Guid questionId, CancellationToken cancellationToken = default)
        => _context.Set<Question>()
            .Include(question => question.Options)
            .FirstOrDefaultAsync(question => question.Id == questionId, cancellationToken);

    /// <inheritdoc />
    public Task<Transition?> GetTransitionAsync(Guid transitionId, CancellationToken cancellationToken = default)
        => _context.Set<Transition>()
            .FirstOrDefaultAsync(transition => transition.Id == transitionId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Transition>> GetTransitionsReferencingQuestionAsync(
        Guid questionId, CancellationToken cancellationToken = default)
        => await _context.Set<Transition>()
            .Where(transition => transition.FromQuestionId == questionId
                              || transition.TargetQuestionId == questionId)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public Task<LoopDefinition?> GetLoopAsync(Guid loopId, CancellationToken cancellationToken = default)
        => _context.Set<LoopDefinition>()
            .FirstOrDefaultAsync(loop => loop.Id == loopId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<LoopDefinition>> GetLoopsReferencingQuestionAsync(
        Guid questionId, CancellationToken cancellationToken = default)
        => await _context.Set<LoopDefinition>()
            .Where(loop => loop.EntryQuestionId == questionId || loop.BreakingQuestionId == questionId)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public Task<TriggerDefinition?> GetTriggerAsync(Guid triggerId, CancellationToken cancellationToken = default)
        => _context.Set<TriggerDefinition>()
            .FirstOrDefaultAsync(trigger => trigger.Id == triggerId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<TriggerDefinition>> GetTriggersReferencingQuestionAsync(
        Guid questionId, CancellationToken cancellationToken = default)
        => await _context.Set<TriggerDefinition>()
            .Where(trigger => trigger.QuestionId == questionId)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<DialogLayout>> GetLayoutAsync(
        Guid dialogId, CancellationToken cancellationToken = default)
        => await _context.Set<DialogLayout>()
            .Where(layout => layout.DialogId == dialogId)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<DialogLayout>> GetLayoutsReferencingElementAsync(
        Guid elementId, CancellationToken cancellationToken = default)
        => await _context.Set<DialogLayout>()
            .Where(layout => layout.ElementId == elementId)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<int> GetMaxDialogVersionAsync(string key, CancellationToken cancellationToken = default)
    {
        // Via a nullable intermediate type: MaxAsync would throw on an empty set.
        var versions = await _context.Dialogs
            .AsNoTracking()
            .Where(dialog => dialog.Key == key)
            .Select(dialog => (int?)dialog.Version)
            .MaxAsync(cancellationToken);

        return versions ?? 0;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Dialog>> GetPublishedVersionsAsync(
        string key, Guid excludeDialogId, CancellationToken cancellationToken = default)
        => await _context.Dialogs
            .Where(dialog => dialog.Key == key && dialog.IsPublished && dialog.Id != excludeDialogId)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public Task<int> CountActiveSessionsAsync(Guid dialogId, CancellationToken cancellationToken = default)
        => _context.DialogSessions
            .CountAsync(
                session => session.DialogId == dialogId && session.Status == SessionStatus.InProgress,
                cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<DialogSession>> GetActiveSessionsAsync(
        Guid dialogId, CancellationToken cancellationToken = default)
        => await _context.DialogSessions
            .Where(session => session.DialogId == dialogId && session.Status == SessionStatus.InProgress)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public Task<bool> DialogKeyExistsAsync(
        string key, Guid? excludeDialogId = null, CancellationToken cancellationToken = default)
        => _context.Dialogs
            .AnyAsync(
                dialog => dialog.Key == key && (excludeDialogId == null || dialog.Id != excludeDialogId),
                cancellationToken);

    /// <inheritdoc />
    public Task<bool> QuestionKeyExistsAsync(
        Guid dialogId, string key, Guid? excludeQuestionId = null, CancellationToken cancellationToken = default)
        => _context.Set<Question>()
            .AnyAsync(
                question => question.DialogId == dialogId
                         && question.Key == key
                         && (excludeQuestionId == null || question.Id != excludeQuestionId),
                cancellationToken);

    /// <inheritdoc />
    public Task<bool> LoopCollectionKeyExistsAsync(
        Guid dialogId, string collectionKey, Guid? excludeLoopId = null,
        CancellationToken cancellationToken = default)
        => _context.Set<LoopDefinition>()
            .AnyAsync(
                loop => loop.DialogId == dialogId
                     && loop.CollectionKey == collectionKey
                     && (excludeLoopId == null || loop.Id != excludeLoopId),
                cancellationToken);

    /// <inheritdoc />
    public void Add<TEntity>(TEntity entity)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(entity);
        _context.Set<TEntity>().Add(entity);
    }

    /// <inheritdoc />
    public void Remove<TEntity>(TEntity entity)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(entity);
        _context.Set<TEntity>().Remove(entity);
    }

    /// <inheritdoc />
    public void RemoveRange<TEntity>(IEnumerable<TEntity> entities)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(entities);
        _context.Set<TEntity>().RemoveRange(entities);
    }

    /// <inheritdoc />
    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => _context.SaveChangesAsync(cancellationToken);
}
