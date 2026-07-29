namespace Flirty.Runtime;

/// <summary>
/// Thrown when no <b>published</b> dialog exists for a business dialog key – for example when starting
/// a dialog via <see cref="StartDialogCommand"/> or <see cref="IFlirtyEngine.StartDialogAsync"/>.
/// </summary>
public sealed class DialogNotFoundException : Exception
{
    /// <summary>Creates the exception without further details.</summary>
    public DialogNotFoundException()
    {
    }

    /// <summary>Creates the exception with the given message.</summary>
    /// <param name="message">The error message describing the cause.</param>
    public DialogNotFoundException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and a triggering exception.</summary>
    /// <param name="message">The error message describing the cause.</param>
    /// <param name="innerException">The exception that caused this exception.</param>
    public DialogNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// The business dialog key for which no published dialog was found,
    /// or <see langword="null"/> if it is not known.
    /// </summary>
    public string? DialogKey { get; init; }

    /// <summary>
    /// Creates a <see cref="DialogNotFoundException"/> for the given
    /// <paramref name="dialogKey"/> along with a meaningful message.
    /// </summary>
    /// <param name="dialogKey">The business dialog key that could not be resolved.</param>
    /// <returns>The prepared exception with <see cref="DialogKey"/> set.</returns>
    public static DialogNotFoundException ForKey(string dialogKey)
        => new($"No published dialog with the key '{dialogKey}' found.")
        {
            DialogKey = dialogKey,
        };
}
