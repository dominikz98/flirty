using Flirty.Domain;
using Microsoft.EntityFrameworkCore;

namespace Flirty.Persistence;

/// <summary>
/// Standardimplementierung von <see cref="IDialogStore"/> über einen scoped
/// <see cref="FlirtyDbContext"/>. Der Dialog-Graph wird ungetrackt und per Split-Query geladen
/// (vier Geschwister-Collections würden sonst ein kartesisches Produkt erzeugen), die Session
/// getrackt (nur eine Collection), damit Submit/Edit-Mutationen über
/// <see cref="SaveChangesAsync"/> greifen.
/// </summary>
internal sealed class DialogStore : IDialogStore
{
    private readonly FlirtyDbContext _context;

    /// <summary>Erstellt den Store über den angegebenen <see cref="FlirtyDbContext"/>.</summary>
    /// <param name="context">Der scoped EF-Core-Kontext der Flirty-Engine.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> ist <see langword="null"/>.</exception>
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
        // Ein Anwender hat pro Dialog erwartungsgemäß höchstens eine laufende Session; die Kandidaten
        // werden gefiltert geladen und die neueste client-seitig gewählt. Bewusst nicht in SQL sortiert:
        // SQLite kann DateTimeOffset (als TEXT gespeichert) nicht in ORDER BY übersetzen – client-seitige
        // Sortierung bleibt über alle drei Provider portabel.
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
    public void AddSession(DialogSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _context.DialogSessions.Add(session);
    }

    /// <inheritdoc />
    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => _context.SaveChangesAsync(cancellationToken);

    /// <summary>
    /// Basis-Query für den vollständigen Dialog-Graphen: ungetrackt (unveränderliche Konfiguration)
    /// und als Split-Query, um das kartesische Produkt über die vier Geschwister-Collections
    /// (Fragen/Optionen, Übergänge, Schleifen, Trigger) zu vermeiden.
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
