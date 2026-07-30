using Flirty.Domain;
using Microsoft.EntityFrameworkCore;

namespace Flirty.Persistence;

/// <summary>
/// Default implementation of <see cref="IDialogStore"/> over a scoped
/// <see cref="FlirtyDbContext"/>. The dialog graph is loaded untracked and via a split query
/// (four sibling collections would otherwise produce a cartesian product), the session
/// tracked (only one collection), so that submit/edit mutations take effect via
/// <see cref="SaveChangesAsync"/>.
/// </summary>
internal sealed class DialogStore : IDialogStore
{
    private readonly FlirtyDbContext _context;

    /// <summary>Creates the store over the given <see cref="FlirtyDbContext"/>.</summary>
    /// <param name="context">The scoped EF Core context of the Flirty engine.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    public DialogStore(FlirtyDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <inheritdoc />
    public Task<Dialog?> GetPublishedDialogAsync(string key, CancellationToken cancellationToken = default)
        => DialogGraph()
            .Where(dialog => dialog.Key == key && dialog.IsPublished)
            .OrderByDescending(dialog => dialog.Version)
            .ThenByDescending(dialog => dialog.Id)
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public Task<Dialog?> GetDialogAsync(Guid dialogId, CancellationToken cancellationToken = default)
        => DialogGraph().FirstOrDefaultAsync(dialog => dialog.Id == dialogId, cancellationToken);

    /// <inheritdoc />
    public Task<DialogSession?> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
        => _context.DialogSessions
            .Include(session => session.Answers)
            .FirstOrDefaultAsync(session => session.Id == sessionId, cancellationToken);

    /// <inheritdoc />
    public async Task<DialogSession?> FindActiveSessionAsync(
        Guid dialogId, string externalUserKey, CancellationToken cancellationToken = default)
    {
        // A user has, as expected, at most one running session per dialog; the candidates
        // are loaded filtered and the newest chosen client-side. Deliberately not sorted in SQL:
        // SQLite cannot translate DateTimeOffset (stored as TEXT) into ORDER BY - client-side
        // sorting stays portable across all three providers.
        var candidates = await _context.DialogSessions
            .Include(session => session.Answers)
            .Where(session => session.DialogId == dialogId
                           && session.ExternalUserKey == externalUserKey
                           && session.Status == SessionStatus.InProgress)
            .ToListAsync(cancellationToken);

        return candidates
            .OrderByDescending(session => session.StartedAt)
            .ThenByDescending(session => session.Id)
            .FirstOrDefault();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TriggerDefinition>> GetTriggersForSessionAsync(
        Guid sessionId, TriggerScope scope, CancellationToken cancellationToken = default)
        => await _context.Set<TriggerDefinition>()
            .AsNoTracking()
            .Where(trigger => trigger.Scope == scope
                && _context.DialogSessions.Any(
                    session => session.Id == sessionId && session.DialogId == trigger.DialogId))
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public void AddSession(DialogSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _context.DialogSessions.Add(session);
    }

    /// <inheritdoc />
    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => _context.SaveChangesAsync(cancellationToken);

    /// <summary>
    /// Base query for the full dialog graph: untracked (immutable configuration)
    /// and as a split query, to avoid the cartesian product over the four sibling collections
    /// (questions/options, transitions, loops, triggers).
    /// </summary>
    private IQueryable<Dialog> DialogGraph()
        => _context.Dialogs
            .AsNoTracking()
            .AsSplitQuery()
            .Include(dialog => dialog.Questions).ThenInclude(question => question.Options)
            .Include(dialog => dialog.Transitions)
            .Include(dialog => dialog.Loops)
            .Include(dialog => dialog.Triggers);
}
