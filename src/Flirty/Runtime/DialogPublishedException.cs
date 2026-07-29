using Flirty.Domain;

namespace Flirty.Runtime;

/// <summary>
/// Thrown when the configuration graph of a <b>published</b> dialog is about to be changed (questions,
/// answer options, transitions, loop markers, triggers or the entry question). A published version is
/// immutable so that running sessions stay stable via their pinned
/// <see cref="DialogSession.DialogVersion"/> – changes go through a <b>new version</b>
/// (<c>CreateDialogVersionCommand</c>) or after unpublishing the dialog (<c>UnpublishDialogCommand</c>).
/// </summary>
/// <remarks>
/// Derives from <see cref="InvalidOperationException"/> so that the endpoint filter of the
/// <c>Flirty.AspNetCore</c> package maps it – like all state conflicts – to <c>409 Conflict</c>. Purely
/// descriptive metadata (name, description) stays editable even on a published version; only the graph
/// is locked.
/// </remarks>
public sealed class DialogPublishedException : InvalidOperationException
{
    /// <summary>Creates the exception without further details.</summary>
    public DialogPublishedException()
    {
    }

    /// <summary>Creates the exception with the given message.</summary>
    /// <param name="message">The error message describing the cause.</param>
    public DialogPublishedException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and a triggering exception.</summary>
    /// <param name="message">The error message describing the cause.</param>
    /// <param name="innerException">The exception that triggered this exception.</param>
    public DialogPublishedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Creates a <see cref="DialogPublishedException"/> for the attempt to change the graph of the
    /// given published dialog version.
    /// </summary>
    /// <param name="dialogKey">The business key of the dialog.</param>
    /// <param name="version">The version number of the published dialog.</param>
    /// <returns>The prepared exception.</returns>
    public static DialogPublishedException ForGraphChange(string dialogKey, int version)
        => new($"The dialog '{dialogKey}' is published in version {version} and therefore cannot be "
             + "changed. Create a new version or unpublish the dialog.");
}
