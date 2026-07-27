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
    /// Admin-CRUD relevanten Graphen (Fragen inkl. Optionen, Übergänge, Schleifen-Marker und Trigger) –
    /// <b>ungetrackt</b> und als Split-Query. Grundlage für die Detail-Abfrage (<c>GetDialogQuery</c>).
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
    /// Lädt den Schleifen-Marker mit der angegebenen <paramref name="loopId"/> <b>getrackt</b>.
    /// </summary>
    /// <param name="loopId">Der Primärschlüssel der Schleifen-Definition.</param>
    /// <param name="cancellationToken">Token zum Abbrechen der Abfrage.</param>
    /// <returns>Der getrackte Schleifen-Marker oder <see langword="null"/>.</returns>
    Task<LoopDefinition?> GetLoopAsync(Guid loopId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lädt alle Schleifen-Marker <b>getrackt</b>, die die Frage mit der angegebenen
    /// <paramref name="questionId"/> als Einstiegs- oder Breaking Question referenzieren. Grundlage für
    /// die Bereinigung verwaister (FK-loser) Marker beim Löschen einer Frage.
    /// </summary>
    /// <param name="questionId">Der Primärschlüssel der Frage.</param>
    /// <param name="cancellationToken">Token zum Abbrechen der Abfrage.</param>
    /// <returns>Die referenzierenden Schleifen-Marker (leere Liste, wenn keine existieren).</returns>
    Task<IReadOnlyList<LoopDefinition>> GetLoopsReferencingQuestionAsync(
        Guid questionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lädt die Trigger-Definition mit der angegebenen <paramref name="triggerId"/> <b>getrackt</b>.
    /// </summary>
    /// <param name="triggerId">Der Primärschlüssel der Trigger-Definition.</param>
    /// <param name="cancellationToken">Token zum Abbrechen der Abfrage.</param>
    /// <returns>Die getrackte Trigger-Definition oder <see langword="null"/>.</returns>
    Task<TriggerDefinition?> GetTriggerAsync(Guid triggerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lädt alle Trigger-Definitionen <b>getrackt</b>, die die Frage mit der angegebenen
    /// <paramref name="questionId"/> referenzieren (<see cref="TriggerDefinition.QuestionId"/>). Grundlage
    /// für die Bereinigung verwaister (FK-loser) Trigger beim Löschen einer Frage.
    /// </summary>
    /// <param name="questionId">Der Primärschlüssel der Frage.</param>
    /// <param name="cancellationToken">Token zum Abbrechen der Abfrage.</param>
    /// <returns>Die referenzierenden Trigger-Definitionen (leere Liste, wenn keine existieren).</returns>
    Task<IReadOnlyList<TriggerDefinition>> GetTriggersReferencingQuestionAsync(
        Guid questionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ermittelt die höchste vergebene Versionsnummer zum fachlichen <paramref name="key"/>. Grundlage
    /// für <c>CreateDialogVersionCommand</c>, das die Folgeversion anlegt.
    /// </summary>
    /// <param name="key">Der fachliche Dialog-Schlüssel.</param>
    /// <param name="cancellationToken">Token zum Abbrechen der Abfrage.</param>
    /// <returns>Die höchste vergebene Version oder <c>0</c>, wenn der Schlüssel unbekannt ist.</returns>
    Task<int> GetMaxDialogVersionAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lädt alle <b>veröffentlichten</b> Dialoge zum fachlichen <paramref name="key"/> außer
    /// <paramref name="excludeDialogId"/> – <b>getrackt</b>. Grundlage dafür, dass
    /// <c>PublishDialogCommand</c> die zuvor produktive Version zurückzieht: Je Schlüssel soll höchstens
    /// eine Version veröffentlicht sein, sonst wäre nur die höchste startbar und die übrigen führten
    /// einen irreführenden Status.
    /// </summary>
    /// <param name="key">Der fachliche Dialog-Schlüssel.</param>
    /// <param name="excludeDialogId">Die Id der Version, die veröffentlicht wird (bleibt unberührt).</param>
    /// <param name="cancellationToken">Token zum Abbrechen der Abfrage.</param>
    /// <returns>Die getrackten, bislang veröffentlichten Geschwister-Versionen.</returns>
    Task<IReadOnlyList<Dialog>> GetPublishedVersionsAsync(
        string key, Guid excludeDialogId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Zählt die Sessions des Dialogs <paramref name="dialogId"/> mit Status
    /// <see cref="SessionStatus.InProgress"/>. Grundlage für die Löschschranke in
    /// <c>DeleteDialogCommand</c> – ein gelöschter Dialog macht seine Sessions unlesbar.
    /// </summary>
    /// <param name="dialogId">Der Primärschlüssel des Dialogs.</param>
    /// <param name="cancellationToken">Token zum Abbrechen der Abfrage.</param>
    /// <returns>Die Anzahl laufender Sessions.</returns>
    Task<int> CountActiveSessionsAsync(Guid dialogId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lädt die laufenden Sessions (<see cref="SessionStatus.InProgress"/>) des Dialogs
    /// <paramref name="dialogId"/> <b>getrackt</b>. Grundlage für
    /// <c>AbandonDialogSessionsCommand</c>, das sie gesammelt auf
    /// <see cref="SessionStatus.Abandoned"/> setzt.
    /// </summary>
    /// <param name="dialogId">Der Primärschlüssel des Dialogs.</param>
    /// <param name="cancellationToken">Token zum Abbrechen der Abfrage.</param>
    /// <returns>Die getrackten laufenden Sessions (leere Liste, wenn keine existieren).</returns>
    Task<IReadOnlyList<DialogSession>> GetActiveSessionsAsync(
        Guid dialogId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Prüft, ob bereits ein <b>anderer</b> Dialog mit dem fachlichen <paramref name="key"/> existiert.
    /// Wird für Anlegen und Metadaten-Update genutzt; die <b>Versionierung</b> umgeht die Prüfung
    /// bewusst, weil mehrere Versionen denselben Schlüssel teilen (Unique-Index <c>(Key, Version)</c>).
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

    /// <summary>
    /// Prüft, ob im Dialog <paramref name="dialogId"/> bereits ein <b>anderer</b> Schleifen-Marker mit
    /// dem <paramref name="collectionKey"/> existiert. Ohne diese Prüfung würde die Laufzeit die
    /// gleichnamigen Collections still überschreiben (im Ausdruckskontext gewinnt der zuletzt
    /// aufgebaute Marker), statt die Doppelvergabe zu melden.
    /// </summary>
    /// <param name="dialogId">Die Id des Dialogs.</param>
    /// <param name="collectionKey">Der zu prüfende Collection-Schlüssel.</param>
    /// <param name="excludeLoopId">Optional die Id des Markers, der bei der Prüfung ausgeklammert wird (Update).</param>
    /// <param name="cancellationToken">Token zum Abbrechen der Abfrage.</param>
    /// <returns><see langword="true"/>, wenn der Schlüssel bereits vergeben ist, sonst <see langword="false"/>.</returns>
    Task<bool> LoopCollectionKeyExistsAsync(
        Guid dialogId, string collectionKey, Guid? excludeLoopId = null,
        CancellationToken cancellationToken = default);

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
