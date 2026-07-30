using Flirty.Domain;
using Flirty.Persistence;

namespace Flirty.Runtime.Admin;

/// <summary>
/// Shared precondition of all commands that change the <b>configuration graph</b> of a dialog
/// (questions, answer options, transitions, loop markers, triggers): a <b>published</b> version
/// is immutable. Without this barrier, changes would immediately affect running sessions – they
/// do pin their <see cref="DialogSession.DialogVersion"/>, but load their graph via the
/// <see cref="Dialog.Id"/>, i.e. from the same row that the admin CRUD changes.
/// </summary>
/// <remarks>
/// Deliberately a helper method rather than an <c>IPipelineBehavior</c>: the check needs a
/// database access and should run only on the 16 graph commands, not on every message. It stands
/// at the start of the respective handler, <b>before</b> child elements are resolved – so that the
/// understandable conflict message wins over a not-found from a subsequent check.
/// </remarks>
internal static class DialogEditGuard
{
    /// <summary>
    /// Ensures that the dialog <paramref name="dialogId"/> exists and is <b>not</b>
    /// published.
    /// </summary>
    /// <param name="store">The writing repository for the configuration graph.</param>
    /// <param name="dialogId">The primary key of the affected dialog.</param>
    /// <param name="cancellationToken">Token to cancel the query.</param>
    /// <returns>The tracked dialog, so that the caller can continue to use it.</returns>
    /// <exception cref="ConfigurationNotFoundException">No dialog with this id exists.</exception>
    /// <exception cref="DialogPublishedException">The dialog is published and therefore locked.</exception>
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
    /// Ensures that the already loaded <paramref name="dialog"/> is not published.
    /// For handlers that hold the dialog anyway (no second database access).
    /// </summary>
    /// <param name="dialog">The loaded dialog.</param>
    /// <exception cref="DialogPublishedException">The dialog is published and therefore locked.</exception>
    public static void EnsureEditable(Dialog dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);

        if (dialog.IsPublished)
        {
            throw DialogPublishedException.ForGraphChange(dialog.Key, dialog.Version);
        }
    }
}
