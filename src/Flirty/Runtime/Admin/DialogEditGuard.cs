using Flirty.Domain;
using Flirty.Persistence;

namespace Flirty.Runtime.Admin;

/// <summary>
/// Gemeinsame Vorbedingung aller Commands, die den <b>Konfigurationsgraphen</b> eines Dialogs ändern
/// (Fragen, Antwortoptionen, Übergänge, Schleifen-Marker, Trigger): Eine <b>veröffentlichte</b> Version
/// ist unveränderlich. Ohne diese Schranke schlagen Änderungen sofort in laufende Sessions durch – die
/// pinnen zwar ihre <see cref="DialogSession.DialogVersion"/>, laden ihren Graphen aber über die
/// <see cref="Dialog.Id"/>, also aus derselben Zeile, die das Admin-CRUD verändert.
/// </summary>
/// <remarks>
/// Bewusst eine Hilfsmethode statt eines <c>IPipelineBehavior</c>: die Prüfung braucht einen
/// Datenbankzugriff und soll nur an den 16 Graph-Commands laufen, nicht an jeder Nachricht. Sie steht
/// am Anfang des jeweiligen Handlers, <b>bevor</b> Kind-Elemente aufgelöst werden – so gewinnt die
/// verständliche Konflikt-Meldung gegenüber einem Not-Found aus einer Folgeprüfung.
/// </remarks>
internal static class DialogEditGuard
{
    /// <summary>
    /// Stellt sicher, dass der Dialog <paramref name="dialogId"/> existiert und <b>nicht</b>
    /// veröffentlicht ist.
    /// </summary>
    /// <param name="store">Das schreibende Repository für den Konfigurationsgraphen.</param>
    /// <param name="dialogId">Der Primärschlüssel des betroffenen Dialogs.</param>
    /// <param name="cancellationToken">Token zum Abbrechen der Abfrage.</param>
    /// <returns>Der getrackte Dialog, damit der Aufrufer ihn weiterverwenden kann.</returns>
    /// <exception cref="ConfigurationNotFoundException">Kein Dialog mit dieser Id existiert.</exception>
    /// <exception cref="DialogPublishedException">Der Dialog ist veröffentlicht und damit gesperrt.</exception>
    public static async Task<Dialog> EnsureEditableAsync(
        IDialogAdminStore store, Guid dialogId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);

        var dialog = await store.GetDialogAsync(dialogId, cancellationToken)
            ?? throw ConfigurationNotFoundException.ForDialog(dialogId);

        EnsureEditable(dialog);

        return dialog;
    }

    /// <summary>
    /// Stellt sicher, dass der bereits geladene <paramref name="dialog"/> nicht veröffentlicht ist.
    /// Für Handler, die den Dialog ohnehin in der Hand haben (kein zweiter Datenbankzugriff).
    /// </summary>
    /// <param name="dialog">Der geladene Dialog.</param>
    /// <exception cref="DialogPublishedException">Der Dialog ist veröffentlicht und damit gesperrt.</exception>
    public static void EnsureEditable(Dialog dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);

        if (dialog.IsPublished)
        {
            throw DialogPublishedException.ForGraphChange(dialog.Key, dialog.Version);
        }
    }
}
