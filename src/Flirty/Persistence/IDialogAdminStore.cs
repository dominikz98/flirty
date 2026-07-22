using Flirty.Domain;

namespace Flirty.Persistence;

/// <summary>
/// Schreibendes Repository für das Konfigurations-Aggregat (Dialog-Graph), das die
/// Admin-CRUD-Handler (Issue #36) benötigen. Bewusst getrennt vom <see cref="IDialogStore"/>: jener
/// liefert die Konfiguration <b>ungetrackt</b> (zur Laufzeit unveränderlich), während CRUD
/// <b>getrackte</b> Entities zum Mutieren/Löschen braucht. Alle Guid-Schlüssel werden
/// anwendungsseitig vergeben; die Persistierung erfolgt gebündelt über <see cref="SaveChangesAsync"/>.
/// </summary>
internal interface IDialogAdminStore
{
    /// <summary>
    /// Lädt den Dialog mit der angegebenen <paramref name="dialogId"/> <b>getrackt</b> und ohne
    /// Graph (nur Metadaten). Grundlage für Metadaten-Update, Publish/Unpublish und Löschen
    /// (Kinder werden per DB-Cascade entfernt).
    /// </summary>
    /// <param name="dialogId">Der Primärschlüssel des Dialogs.</param>
    /// <param name="cancellationToken">Token zum Abbrechen der Abfrage.</param>
    /// <returns>Der getrackte Dialog oder <see langword="null"/>, wenn keine solche Id existiert.</returns>
    Task<Dialog?> GetDialogAsync(Guid dialogId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lädt den Dialog mit der angegebenen <paramref name="dialogId"/> samt seinem für das
    /// Admin-CRUD relevanten Graphen (Fragen inkl. Optionen, Übergänge und Schleifen-Marker) –
    /// <b>ungetrackt</b> und als Split-Query. Grundlage für die Detail-Abfrage (<c>GetDialogQuery</c>).
    /// Die Schleifen-Marker kommen bewusst nur lesend mit: sie machen die im Ausdruckskontext
    /// verfügbaren Loop-Collections sichtbar (Ausdrucks-Validierung im Designer).
    /// </summary>
    /// <param name="dialogId">Der Primärschlüssel des Dialogs.</param>
    /// <param name="cancellationToken">Token zum Abbrechen der Abfrage.</param>
    /// <returns>Der Dialog samt Graph oder <see langword="null"/>, wenn keine solche Id existiert.</returns>
    Task<Dialog?> GetDialogGraphAsync(Guid dialogId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lädt alle Dialoge (nur Metadaten, ohne Graph) <b>ungetrackt</b>, sortiert nach Schlüssel und
    /// Version. Grundlage für die Dialog-Liste (<c>ListDialogsQuery</c>).
    /// </summary>
    /// <param name="cancellationToken">Token zum Abbrechen der Abfrage.</param>
    /// <returns>Die Dialoge in stabiler Reihenfolge (leere Liste, wenn keine existieren).</returns>
    Task<IReadOnlyList<Dialog>> ListDialogsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lädt die Frage mit der angegebenen <paramref name="questionId"/> <b>getrackt</b> samt ihren
    /// Optionen. Grundlage für Frage-Update/-Löschen und die Options-CRUD (Eltern-Auflösung).
    /// </summary>
    /// <param name="questionId">Der Primärschlüssel der Frage.</param>
    /// <param name="cancellationToken">Token zum Abbrechen der Abfrage.</param>
    /// <returns>Die getrackte Frage samt Optionen oder <see langword="null"/>.</returns>
    Task<Question?> GetQuestionAsync(Guid questionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lädt den Übergang mit der angegebenen <paramref name="transitionId"/> <b>getrackt</b>.
    /// </summary>
    /// <param name="transitionId">Der Primärschlüssel des Übergangs.</param>
    /// <param name="cancellationToken">Token zum Abbrechen der Abfrage.</param>
    /// <returns>Der getrackte Übergang oder <see langword="null"/>.</returns>
    Task<Transition?> GetTransitionAsync(Guid transitionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lädt alle Übergänge <b>getrackt</b>, die die Frage mit der angegebenen
    /// <paramref name="questionId"/> als Ausgangs- oder Zielfrage referenzieren. Grundlage für die
    /// Bereinigung verwaister (FK-loser) Übergänge beim Löschen einer Frage.
    /// </summary>
    /// <param name="questionId">Der Primärschlüssel der Frage.</param>
    /// <param name="cancellationToken">Token zum Abbrechen der Abfrage.</param>
    /// <returns>Die referenzierenden Übergänge (leere Liste, wenn keine existieren).</returns>
    Task<IReadOnlyList<Transition>> GetTransitionsReferencingQuestionAsync(
        Guid questionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Prüft, ob bereits ein <b>anderer</b> Dialog mit dem fachlichen <paramref name="key"/> existiert
    /// (Eindeutigkeit des Schlüssels; Versionierung ist in #36 nicht im Umfang).
    /// </summary>
    /// <param name="key">Der zu prüfende fachliche Dialog-Schlüssel.</param>
    /// <param name="excludeDialogId">Optional die Id des Dialogs, der bei der Prüfung ausgeklammert wird (Update).</param>
    /// <param name="cancellationToken">Token zum Abbrechen der Abfrage.</param>
    /// <returns><see langword="true"/>, wenn der Schlüssel bereits vergeben ist, sonst <see langword="false"/>.</returns>
    Task<bool> DialogKeyExistsAsync(
        string key, Guid? excludeDialogId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Prüft, ob im Dialog <paramref name="dialogId"/> bereits eine <b>andere</b> Frage mit dem
    /// fachlichen <paramref name="key"/> existiert (Unique-Constraint <c>(DialogId, Key)</c>).
    /// </summary>
    /// <param name="dialogId">Die Id des Dialogs.</param>
    /// <param name="key">Der zu prüfende fachliche Frage-Schlüssel.</param>
    /// <param name="excludeQuestionId">Optional die Id der Frage, die bei der Prüfung ausgeklammert wird (Update).</param>
    /// <param name="cancellationToken">Token zum Abbrechen der Abfrage.</param>
    /// <returns><see langword="true"/>, wenn der Schlüssel bereits vergeben ist, sonst <see langword="false"/>.</returns>
    Task<bool> QuestionKeyExistsAsync(
        Guid dialogId, string key, Guid? excludeQuestionId = null, CancellationToken cancellationToken = default);

    /// <summary>Nimmt eine neu erstellte Entity in die Nachverfolgung auf (Persistierung erst via <see cref="SaveChangesAsync"/>).</summary>
    /// <typeparam name="TEntity">Der Entity-Typ.</typeparam>
    /// <param name="entity">Die zu ergänzende Entity.</param>
    void Add<TEntity>(TEntity entity)
        where TEntity : class;

    /// <summary>Markiert eine getrackte Entity zum Löschen (Persistierung erst via <see cref="SaveChangesAsync"/>).</summary>
    /// <typeparam name="TEntity">Der Entity-Typ.</typeparam>
    /// <param name="entity">Die zu löschende Entity.</param>
    void Remove<TEntity>(TEntity entity)
        where TEntity : class;

    /// <summary>Markiert mehrere getrackte Entities zum Löschen (Persistierung erst via <see cref="SaveChangesAsync"/>).</summary>
    /// <typeparam name="TEntity">Der Entity-Typ.</typeparam>
    /// <param name="entities">Die zu löschenden Entities.</param>
    void RemoveRange<TEntity>(IEnumerable<TEntity> entities)
        where TEntity : class;

    /// <summary>
    /// Schreibt alle in dieser Arbeitseinheit (Unit of Work) angesammelten Änderungen gebündelt in die Datenbank.
    /// </summary>
    /// <param name="cancellationToken">Token zum Abbrechen des Speichervorgangs.</param>
    /// <returns>Ein Task, der abgeschlossen ist, sobald gespeichert wurde.</returns>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
