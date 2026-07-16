namespace Flirty.Runtime;

/// <summary>
/// Wird geworfen, wenn zu einem fachlichen Dialog-Schlüssel kein <b>veröffentlichter</b> Dialog
/// existiert – etwa beim Start eines Dialogs über <see cref="StartDialogCommand"/> bzw.
/// <see cref="IFlirtyEngine.StartDialogAsync"/>.
/// </summary>
public sealed class DialogNotFoundException : Exception
{
    /// <summary>Erstellt die Ausnahme ohne weitere Angaben.</summary>
    public DialogNotFoundException()
    {
    }

    /// <summary>Erstellt die Ausnahme mit der angegebenen Meldung.</summary>
    /// <param name="message">Die Fehlermeldung, die die Ursache beschreibt.</param>
    public DialogNotFoundException(string message)
        : base(message)
    {
    }

    /// <summary>Erstellt die Ausnahme mit Meldung und auslösender Ausnahme.</summary>
    /// <param name="message">Die Fehlermeldung, die die Ursache beschreibt.</param>
    /// <param name="innerException">Die Ausnahme, die diese Ausnahme ausgelöst hat.</param>
    public DialogNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Der fachliche Dialog-Schlüssel, für den kein veröffentlichter Dialog gefunden wurde,
    /// oder <see langword="null"/>, wenn er nicht bekannt ist.
    /// </summary>
    public string? DialogKey { get; init; }

    /// <summary>
    /// Erstellt eine <see cref="DialogNotFoundException"/> für den angegebenen
    /// <paramref name="dialogKey"/> samt aussagekräftiger Meldung.
    /// </summary>
    /// <param name="dialogKey">Der fachliche Dialog-Schlüssel, der nicht aufgelöst werden konnte.</param>
    /// <returns>Die vorbereitete Ausnahme mit gesetztem <see cref="DialogKey"/>.</returns>
    public static DialogNotFoundException ForKey(string dialogKey)
        => new($"Kein veröffentlichter Dialog mit dem Schlüssel '{dialogKey}' gefunden.")
        {
            DialogKey = dialogKey,
        };
}
