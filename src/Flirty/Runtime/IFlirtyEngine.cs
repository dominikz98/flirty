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
}
