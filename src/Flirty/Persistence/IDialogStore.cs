using Flirty.Domain;

namespace Flirty.Persistence;

/// <summary>
/// Repository über den <see cref="FlirtyDbContext"/>: kapselt die Lade- und Speicheroperationen,
/// die die Runtime-Schicht (Start/Resume/Submit/Edit, Issue #25) benötigt, und hält den
/// EF-Core-Kontext aus den Mediator-Handlern heraus. Die Konfigurationsebene (Dialog-Graph) wird
/// bewusst <b>ungetrackt</b> geliefert (zur Laufzeit unveränderlich), die Runtime-Ebene
/// (<see cref="DialogSession"/>) hingegen <b>getrackt</b>, damit Mutationen über
/// <see cref="SaveChangesAsync"/> persistiert werden.
/// </summary>
internal interface IDialogStore
{
    /// <summary>
    /// Lädt die höchste <b>veröffentlichte</b> Version des Dialogs mit dem fachlichen
    /// <paramref name="key"/> samt vollständigem Konfigurationsgraphen (Fragen inkl. Optionen,
    /// Übergänge, Schleifen, Trigger). Grundlage für <c>StartDialogCommand</c>.
    /// </summary>
    /// <param name="key">Der fachliche, stabile Schlüssel des Dialogs.</param>
    /// <param name="cancellationToken">Token zum Abbrechen der Abfrage.</param>
    /// <returns>Der veröffentlichte Dialog mit der höchsten Version oder
    /// <see langword="null"/>, wenn kein veröffentlichter Dialog mit diesem Schlüssel existiert.</returns>
    Task<Dialog?> GetPublishedDialogAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lädt den Dialog mit der angegebenen <paramref name="dialogId"/> – also die exakte, von einer
    /// Session gepinnte Version – samt vollständigem Graphen, <b>unabhängig vom
    /// Veröffentlichungsstatus</b>. Grundlage für Resume/Submit/Edit (gepinnte Dialogversion).
    /// </summary>
    /// <param name="dialogId">Der Primärschlüssel der konkreten Dialogversion.</param>
    /// <param name="cancellationToken">Token zum Abbrechen der Abfrage.</param>
    /// <returns>Der Dialog samt Graph oder <see langword="null"/>, wenn keine solche Id existiert.</returns>
    Task<Dialog?> GetDialogAsync(Guid dialogId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lädt die Session mit der angegebenen <paramref name="sessionId"/> samt ihrer Antworten.
    /// Die Session wird <b>getrackt</b> zurückgegeben, damit nachfolgende Mutationen (neue Antwort,
    /// Statuswechsel, aktuelle Frage) über <see cref="SaveChangesAsync"/> gespeichert werden.
    /// </summary>
    /// <param name="sessionId">Der Primärschlüssel der Session.</param>
    /// <param name="cancellationToken">Token zum Abbrechen der Abfrage.</param>
    /// <returns>Die getrackte Session samt Antworten oder <see langword="null"/>,
    /// wenn keine solche Session existiert.</returns>
    Task<DialogSession?> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sucht die zuletzt gestartete <b>laufende</b> (<see cref="SessionStatus.InProgress"/>) Session
    /// eines Anwenders (<paramref name="externalUserKey"/>) für die angegebene
    /// <paramref name="dialogId"/> samt Antworten – <b>getrackt</b>. Grundlage für den
    /// Resume-oder-Neu-Entscheid in <c>StartDialogCommand</c>.
    /// </summary>
    /// <param name="dialogId">Die Id der konkreten Dialogversion.</param>
    /// <param name="externalUserKey">Der fachliche Anwenderschlüssel der Host-App.</param>
    /// <param name="cancellationToken">Token zum Abbrechen der Abfrage.</param>
    /// <returns>Die getrackte laufende Session oder <see langword="null"/>, wenn keine existiert.</returns>
    Task<DialogSession?> FindActiveSessionAsync(
        Guid dialogId, string externalUserKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lädt die Trigger-Definitionen des Dialogs, zu dem die Session <paramref name="sessionId"/>
    /// gehört, gefiltert auf den angegebenen <paramref name="scope"/> – <b>ungetrackt</b>. Grundlage für
    /// die Auslieferung der im Designer konfigurierten Trigger (<c>WebhookNotificationHandler</c>, #42).
    /// </summary>
    /// <remarks>
    /// Bewusst <b>eine</b> schmale Abfrage über den Fremdschlüssel-Index statt „erst Session laden, dann
    /// Dialog-Graph": Der Handler läuft synchron im Scope des auslösenden Commands, und die
    /// Notifications tragen (bis auf den Start) keine <c>DialogId</c>.
    /// </remarks>
    /// <param name="sessionId">Der Primärschlüssel der auslösenden Session.</param>
    /// <param name="scope">Der Zeitpunkt, zu dem ausgelöst wurde.</param>
    /// <param name="cancellationToken">Token zum Abbrechen der Abfrage.</param>
    /// <returns>Die passenden Trigger-Definitionen (leere Liste, wenn keine existieren).</returns>
    Task<IReadOnlyList<TriggerDefinition>> GetTriggersForSessionAsync(
        Guid sessionId, TriggerScope scope, CancellationToken cancellationToken = default);

    /// <summary>
    /// Nimmt eine neu erstellte <paramref name="session"/> (inklusive erster Antworten) in die
    /// Nachverfolgung auf. Die Persistierung erfolgt erst mit <see cref="SaveChangesAsync"/>.
    /// Bewusst synchron: alle Guid-Schlüssel werden anwendungsseitig vergeben (keine
    /// DB-Wertgenerierung), daher ist <c>AddAsync</c> nicht erforderlich.
    /// </summary>
    /// <param name="session">Die zu ergänzende Session.</param>
    void AddSession(DialogSession session);

    /// <summary>
    /// Schreibt alle in dieser Arbeitseinheit (Unit of Work) angesammelten Änderungen – neue Session
    /// bzw. mutierte, getrackte Session samt Antworten – gebündelt in die Datenbank.
    /// </summary>
    /// <param name="cancellationToken">Token zum Abbrechen des Speichervorgangs.</param>
    /// <returns>Ein Task, der abgeschlossen ist, sobald gespeichert wurde.</returns>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
