using Flirty.Domain;
using Microsoft.EntityFrameworkCore;

namespace Flirty.Persistence;

/// <summary>
/// Standardimplementierung von <see cref="IDialogAdminStore"/> über einen scoped
/// <see cref="FlirtyDbContext"/>. Ladeoperationen für Mutation/Löschung liefern <b>getrackte</b>
/// Entities (damit <see cref="SaveChangesAsync"/> greift); rein lesende Abfragen (Liste, Detailgraph)
/// laufen <b>ungetrackt</b>. Kind-Entities werden über <c>Set&lt;T&gt;()</c> adressiert.
/// </summary>
internal sealed class DialogAdminStore : IDialogAdminStore
{
    private readonly FlirtyDbContext _context;

    /// <summary>Erstellt den Store über den angegebenen <see cref="FlirtyDbContext"/>.</summary>
    /// <param name="context">Der scoped EF-Core-Kontext der Flirty-Engine.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> ist <see langword="null"/>.</exception>
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
