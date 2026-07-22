namespace Flirty.Runtime;

/// <summary>
/// Öffentliche Facade über die Dialog-Runtime der Flirty-Engine. Kapselt das Senden der
/// Mediator-Commands, sodass Host-Apps die Engine bequem nutzen können, ohne selbst
/// <see cref="Mediator.ISender"/> zu verwenden. Wer die volle Pipeline (inkl. eigener
/// Behaviors/Notifications) benötigt, kann die Commands weiterhin direkt per
/// <see cref="Mediator.ISender"/> senden.
/// </summary>
public interface IFlirtyEngine
{
    /// <summary>
    /// Startet den veröffentlichten Dialog mit dem angegebenen Schlüssel für den Anwender oder setzt
    /// eine bereits laufende Session fort (Resume) und liefert die aktuell offene Frage.
    /// </summary>
    /// <param name="dialogKey">Der fachliche, stabile Schlüssel des zu startenden Dialogs.</param>
    /// <param name="externalUserKey">Der fachliche Anwenderschlüssel der Host-App (z. B. Benutzer-Id).</param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns>Die (neue oder fortgesetzte) Session samt aktueller Frage.</returns>
    /// <exception cref="DialogNotFoundException">
    /// Kein veröffentlichter Dialog mit dem angegebenen Schlüssel existiert.
    /// </exception>
    Task<StartDialogResult> StartDialogAsync(
        string dialogKey, string externalUserKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Startet die <b>konkrete Dialogversion</b> mit der angegebenen Id für den Anwender – <b>unabhängig
    /// vom Veröffentlichungsstatus</b> – oder setzt eine bereits laufende Session dieser Version fort
    /// (Resume) und liefert die aktuell offene Frage. Gedacht für Vorschau-/Testszenarien, in denen ein
    /// Entwurf durchgespielt werden soll, bevor er veröffentlicht wird (Designer-Test-Runner, #43).
    /// </summary>
    /// <remarks>
    /// Für den produktiven Start ist <see cref="StartDialogAsync"/> vorgesehen: Es löst über den fachlichen
    /// Schlüssel auf und startet ausschließlich veröffentlichte Dialoge.
    /// </remarks>
    /// <param name="dialogId">Der Primärschlüssel der zu startenden Dialogversion.</param>
    /// <param name="externalUserKey">Der fachliche Anwenderschlüssel der Host-App (z. B. Benutzer-Id).</param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns>Die (neue oder fortgesetzte) Session samt aktueller Frage.</returns>
    /// <exception cref="ConfigurationNotFoundException">
    /// Keine Dialogversion mit der angegebenen <paramref name="dialogId"/> existiert.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Der Dialog besitzt keine Einstiegsfrage bzw. die aktuelle Frage kann nicht aufgelöst werden.
    /// </exception>
    Task<StartDialogResult> StartDialogVersionAsync(
        Guid dialogId, string externalUserKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reicht eine Antwort auf die aktuell offene Frage einer laufenden Session ein: persistiert die
    /// Antwort, wertet die Übergänge (Branching) aus und liefert die nächste Frage bzw. signalisiert den
    /// Abschluss des Dialogs.
    /// </summary>
    /// <param name="sessionId">Der Primärschlüssel der laufenden Session.</param>
    /// <param name="questionId">
    /// Die Id der zu beantwortenden Frage; muss der aktuell offenen Frage der Session entsprechen.
    /// </param>
    /// <param name="value">Der abgegebene Antwortwert als roher JSON-Text (Format abhängig vom Fragetyp).</param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns>Das Ergebnis mit der nächsten Frage oder dem Abschluss-Signal.</returns>
    /// <exception cref="SessionNotFoundException">
    /// Keine Session mit der angegebenen <paramref name="sessionId"/> existiert.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Die Session ist nicht mehr offen, die angegebene Frage ist nicht die aktuell offene, oder das
    /// Branching ist fehlkonfiguriert.
    /// </exception>
    Task<SubmitAnswerResult> SubmitAnswerAsync(
        Guid sessionId, Guid questionId, string value, CancellationToken cancellationToken = default);

    /// <summary>
    /// Liest den aktuellen Zustand einer Session – Status, die (ggf.) aktuell offene Frage und die bisher
    /// gegebenen Antworten – rein lesend, um eine Befragung z. B. nach einem Reload der Host-App
    /// wiederherzustellen.
    /// </summary>
    /// <param name="sessionId">Der Primärschlüssel der abzufragenden Session.</param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns>Der Zustand der Session samt aktueller Frage und bisheriger Antworten.</returns>
    /// <exception cref="SessionNotFoundException">
    /// Keine Session mit der angegebenen <paramref name="sessionId"/> existiert.
    /// </exception>
    Task<ResumeDialogResult> ResumeDialogAsync(
        Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Editiert die bereits gegebene Antwort auf eine frühere Frage einer Session: überschreibt den Wert,
    /// verwirft (invalidiert) alle nachgelagerten Antworten und berechnet den Pfad ab der editierten Frage
    /// über das Branching neu. Eine bereits abgeschlossene Session wird wieder geöffnet, sofern die
    /// Neuberechnung auf eine nicht-terminale Folgefrage führt.
    /// </summary>
    /// <param name="sessionId">Der Primärschlüssel der Session, deren Antwort editiert wird.</param>
    /// <param name="questionId">
    /// Die Id der Frage, deren Antwort überschrieben werden soll; muss zum Dialog gehören und in dieser
    /// Session bereits beantwortet worden sein (nicht notwendigerweise die aktuell offene Frage).
    /// </param>
    /// <param name="value">Der neue Antwortwert als roher JSON-Text (Format abhängig vom Fragetyp).</param>
    /// <param name="iterationIndex">
    /// Optionaler nullbasierter Iterationsindex, um innerhalb einer Schleife gezielt die Antwort einer
    /// bestimmten Iteration zu editieren; <see langword="null"/> editiert die früheste Antwort der Frage.
    /// </param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns>
    /// Das Ergebnis mit der neu berechneten Folgefrage bzw. dem Abschluss-Signal und der Anzahl verworfener
    /// nachgelagerter Antworten.
    /// </returns>
    /// <exception cref="SessionNotFoundException">
    /// Keine Session mit der angegebenen <paramref name="sessionId"/> existiert.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Die Session ist abgebrochen, die Frage gehört nicht zum Dialog, die Frage (bzw. die angegebene
    /// Iteration) wurde noch nicht beantwortet, oder das Branching ist fehlkonfiguriert.
    /// </exception>
    Task<EditAnswerResult> EditAnswerAsync(
        Guid sessionId, Guid questionId, string value, int? iterationIndex = null,
        CancellationToken cancellationToken = default);
}
