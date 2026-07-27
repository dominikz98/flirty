using Flirty.Domain;

namespace Flirty.Runtime;

/// <summary>
/// Wird geworfen, wenn der Konfigurationsgraph eines <b>veröffentlichten</b> Dialogs verändert werden
/// soll (Fragen, Antwortoptionen, Übergänge, Schleifen-Marker, Trigger oder die Einstiegsfrage). Eine
/// veröffentlichte Version ist unveränderlich, damit laufende Sessions über ihre gepinnte
/// <see cref="DialogSession.DialogVersion"/> stabil bleiben – Änderungen laufen über eine <b>neue
/// Version</b> (<c>CreateDialogVersionCommand</c>) oder nach dem Zurückziehen des Dialogs
/// (<c>UnpublishDialogCommand</c>).
/// </summary>
/// <remarks>
/// Leitet von <see cref="InvalidOperationException"/> ab, damit der Endpunkt-Filter des Pakets
/// <c>Flirty.AspNetCore</c> sie – wie alle Zustands-Konflikte – auf <c>409 Conflict</c> abbildet. Rein
/// beschreibende Metadaten (Name, Beschreibung) bleiben auch an einer veröffentlichten Version
/// änderbar; nur der Graph ist gesperrt.
/// </remarks>
public sealed class DialogPublishedException : InvalidOperationException
{
    /// <summary>Erstellt die Ausnahme ohne weitere Angaben.</summary>
    public DialogPublishedException()
    {
    }

    /// <summary>Erstellt die Ausnahme mit der angegebenen Meldung.</summary>
    /// <param name="message">Die Fehlermeldung, die die Ursache beschreibt.</param>
    public DialogPublishedException(string message)
        : base(message)
    {
    }

    /// <summary>Erstellt die Ausnahme mit Meldung und auslösender Ausnahme.</summary>
    /// <param name="message">Die Fehlermeldung, die die Ursache beschreibt.</param>
    /// <param name="innerException">Die Ausnahme, die diese Ausnahme ausgelöst hat.</param>
    public DialogPublishedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Erstellt eine <see cref="DialogPublishedException"/> für den Versuch, den Graphen der
    /// angegebenen veröffentlichten Dialogversion zu ändern.
    /// </summary>
    /// <param name="dialogKey">Der fachliche Schlüssel des Dialogs.</param>
    /// <param name="version">Die Versionsnummer des veröffentlichten Dialogs.</param>
    /// <returns>Die vorbereitete Ausnahme.</returns>
    public static DialogPublishedException ForGraphChange(string dialogKey, int version)
        => new($"Der Dialog '{dialogKey}' ist in Version {version} veröffentlicht und deshalb nicht "
             + "änderbar. Lege eine neue Version an oder zieh den Dialog zurück.");
}
