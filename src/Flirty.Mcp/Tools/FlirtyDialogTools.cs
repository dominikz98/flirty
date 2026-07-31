using System.ComponentModel;
using Flirty.Runtime.Admin;
using Mediator;
using ModelContextProtocol.Server;

namespace Flirty.Mcp.Tools;

/// <summary>
/// The dialog-level MCP tools: create, read, change, publish, version and clean up a dialog. A thin layer
/// over the admin CRUD commands of <c>Flirty.Runtime.Admin</c>, dispatched via <see cref="ISender"/>.
/// </summary>
/// <remarks>
/// <para>
/// The class is <c>internal sealed</c> and its methods are <c>internal static</c> on purpose.
/// <c>WithTools&lt;T&gt;()</c> scans with <c>BindingFlags.NonPublic</c>, so they are discovered; being
/// internal also removes the CS1591 tax from methods whose real documentation is the
/// <see cref="DescriptionAttribute"/> that actually ships to the client. It cannot be a
/// <c>static class</c>: a static type cannot be a type argument (CS0718).
/// </para>
/// <para>
/// Every optional parameter carries an explicit <c>= null</c>. Without it, omitting the argument is an
/// argument-binding failure in the SDK's marshaller rather than a <see langword="null"/> – the marshaller
/// throws for a required parameter that has no default.
/// </para>
/// <para>
/// The tool parameters are primitives and <see cref="Guid"/> only. Any type registered in the host
/// container is silently excluded from the input schema (the SDK injects it instead), which is how
/// <see cref="ISender"/> arrives without appearing in the schema – and why a parameter must never be a
/// type a host might register.
/// </para>
/// <para>
/// The results are the <b>core</b> records, serialized directly. <see cref="DialogDetail"/> therefore keeps
/// its metadata nested under <c>dialog</c> where the HTTP DTO flattens it: that is more informative, not
/// less, because it makes visible that <c>flirty_dialog_create</c> returns the same block that sits under
/// <c>dialog</c> in <c>flirty_dialog_get</c>.
/// </para>
/// <para>
/// Every tool sets <c>UseStructuredContent = true</c>, which is <b>not</b> the SDK default: without it the
/// result is serialized into the text block only, <c>structuredContent</c> stays empty and the tool
/// advertises no <c>outputSchema</c> – so a client would have to parse prose to get at a dialog id. Because
/// every return type here is an object (that is what <c>FlirtyAck</c> and the other wrappers are for), the
/// payload also needs no protocol-version-dependent <c>{"result": …}</c> wrapping.
/// </para>
/// </remarks>
[McpServerToolType]
internal sealed class FlirtyDialogTools
{
    [McpServerTool(Name = "flirty_dialog_create", UseStructuredContent = true)]
    [Description("Creates a new, unpublished dialog with version 1. The entry question stays unset at "
        + "first and is set with flirty_dialog_update once questions exist.")]
    internal static async Task<DialogSummary> CreateDialogAsync(
        ISender sender,
        [Description("The business, stable key of the dialog. Must be unique across all dialogs.")]
        string key,
        [Description("The display name of the dialog.")]
        string name,
        CancellationToken cancellationToken,
        [Description("An optional description of the dialog.")]
        string? description = null)
        => await sender.Send(new CreateDialogCommand(key, name, description), cancellationToken);

    [McpServerTool(Name = "flirty_dialog_list", UseStructuredContent = true)]
    [Description("Lists all configured dialogs as metadata, without their graphs, sorted by key and "
        + "version. Read-only.")]
    internal static async Task<FlirtyDialogList> ListDialogsAsync(
        ISender sender, CancellationToken cancellationToken)
        => new(await sender.Send(new ListDialogsQuery(), cancellationToken));

    [McpServerTool(Name = "flirty_dialog_get", UseStructuredContent = true)]
    [Description("Reads one dialog along with its configuration graph: questions including answer "
        + "options, transitions, loop markers, triggers and the stored canvas positions. The dialog "
        + "metadata sits nested under 'dialog'. Read-only.")]
    internal static async Task<DialogDetail> GetDialogAsync(
        ISender sender,
        [Description("The id of the dialog to read.")]
        Guid dialogId,
        CancellationToken cancellationToken)
        => await sender.Send(new GetDialogQuery(dialogId), cancellationToken);

    [McpServerTool(Name = "flirty_dialog_update", UseStructuredContent = true)]
    [Description("Updates the metadata of a dialog and optionally sets its entry question. On a published "
        + "dialog, name and description stay editable, but changing the entry question is refused - "
        + "derive a new version with flirty_dialog_create_version instead.")]
    internal static async Task<DialogSummary> UpdateDialogAsync(
        ISender sender,
        [Description("The id of the dialog to change.")]
        Guid dialogId,
        [Description("The business, stable key of the dialog. Must stay unique.")]
        string key,
        [Description("The display name of the dialog.")]
        string name,
        CancellationToken cancellationToken,
        [Description("An optional description of the dialog.")]
        string? description = null,
        [Description("The id of the entry question. Must reference a question of this dialog.")]
        Guid? startQuestionId = null)
        => await sender.Send(
            new UpdateDialogCommand(dialogId, key, name, description, startQuestionId), cancellationToken);

    [McpServerTool(Name = "flirty_dialog_delete", UseStructuredContent = true)]
    [Description("Deletes a dialog together with its entire configuration graph. Refused while sessions "
        + "are still running on this version - end them with flirty_dialog_abandon_sessions first.")]
    internal static async Task<FlirtyAck> DeleteDialogAsync(
        ISender sender,
        [Description("The id of the dialog to delete.")]
        Guid dialogId,
        CancellationToken cancellationToken)
    {
        await sender.Send(new DeleteDialogCommand(dialogId), cancellationToken);
        return FlirtyAck.Instance;
    }

    [McpServerTool(Name = "flirty_dialog_publish", UseStructuredContent = true)]
    [Description("Publishes a dialog so it can be started in production. Requires an entry question, and "
        + "retires any previously published version of the same key. A published graph is locked.")]
    internal static async Task<DialogSummary> PublishDialogAsync(
        ISender sender,
        [Description("The id of the dialog to publish.")]
        Guid dialogId,
        CancellationToken cancellationToken)
        => await sender.Send(new PublishDialogCommand(dialogId), cancellationToken);

    [McpServerTool(Name = "flirty_dialog_unpublish", UseStructuredContent = true)]
    [Description("Withdraws a dialog from production. New sessions can no longer be started on it; "
        + "sessions already running keep their pinned version.")]
    internal static async Task<DialogSummary> UnpublishDialogAsync(
        ISender sender,
        [Description("The id of the dialog to unpublish.")]
        Guid dialogId,
        CancellationToken cancellationToken)
        => await sender.Send(new UnpublishDialogCommand(dialogId), cancellationToken);

    [McpServerTool(Name = "flirty_dialog_create_version", UseStructuredContent = true)]
    [Description("Derives a new version from an existing dialog: clones the whole graph as an unpublished "
        + "draft with the version number raised by one. The intended way to evolve a published dialog "
        + "without breaking running sessions. Note that every cloned element gets a new id.")]
    internal static async Task<DialogDetail> CreateDialogVersionAsync(
        ISender sender,
        [Description("The id of the dialog version to copy.")]
        Guid dialogId,
        CancellationToken cancellationToken)
        => await sender.Send(new CreateDialogVersionCommand(dialogId), cancellationToken);

    [McpServerTool(Name = "flirty_dialog_abandon_sessions", UseStructuredContent = true)]
    [Description("Ends all sessions still running on a dialog version by setting them to abandoned, and "
        + "reports how many were ended. The precondition for deleting a dialog that is still in use.")]
    internal static async Task<AbandonSessionsResult> AbandonDialogSessionsAsync(
        ISender sender,
        [Description("The id of the dialog version whose sessions are ended.")]
        Guid dialogId,
        CancellationToken cancellationToken)
        => await sender.Send(new AbandonDialogSessionsCommand(dialogId), cancellationToken);

    [McpServerTool(Name = "flirty_dialog_count_active_sessions", UseStructuredContent = true)]
    [Description("Counts the sessions still in progress on a dialog version. Read-only; an operating aid "
        + "before unpublishing, deleting or deriving a version.")]
    internal static async Task<FlirtyActiveSessionCount> CountActiveSessionsAsync(
        ISender sender,
        [Description("The id of the dialog version.")]
        Guid dialogId,
        CancellationToken cancellationToken)
        => new(dialogId, await sender.Send(new CountActiveSessionsQuery(dialogId), cancellationToken));
}
