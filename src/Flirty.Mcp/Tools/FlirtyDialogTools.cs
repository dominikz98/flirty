using System.ComponentModel;
using Flirty.Runtime.Admin;
using Mediator;
using ModelContextProtocol.Server;

namespace Flirty.Mcp.Tools;

/// <summary>
/// The dialog-level MCP tools: create, read, change, publish, version and clean up a dialog. The MCP
/// counterpart of <c>MapDialogEndpoints</c> and one of the ten tool classes of this package – a thin
/// layer over the admin CRUD commands of <c>Flirty.Runtime.Admin</c>, dispatched via
/// <see cref="ISender"/>.
/// </summary>
/// <remarks>
/// <para>
/// This class is the <b>documentation home</b> for the tool-shape conventions of all ten: the nine
/// others state only what is specific to their area and point here for the rest.
/// </para>
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
/// The tool parameters are primitives, <see cref="Guid"/> and enums only. Any type registered in the host
/// container is silently excluded from the input schema (the SDK injects it instead), which is how
/// <see cref="ISender"/> arrives without appearing in the schema – and why a parameter must never be a
/// type a host might register. There is exactly <b>one</b> exception in the package, and it is documented
/// on <see cref="FlirtyLayoutTools"/>: <c>flirty_layout_set</c> takes a batch
/// (<see cref="DialogLayoutEntry"/><c>[]</c>), because a batch is not expressible as a scalar parameter.
/// It is admissible there because the type is the core's own input record and the generated schema was
/// verified to be inline, camelCase and name-enumerated rather than an opaque blob.
/// </para>
/// <para>
/// Every tool takes its <c>Name</c> from <see cref="FlirtyToolNames"/> and never lets the SDK derive one,
/// and every tool sets all four annotation hints explicitly. Both are load-bearing rather than tidy: a
/// derived name turns a C# rename into a client-visible breaking change, and an <i>omitted</i> hint is not
/// a neutral one – it is absent from the wire, and the protocol then lets a client assume
/// <c>destructive</c> and <c>openWorld</c>. Unset, every <c>create</c> would look like it might destroy
/// data. <c>OpenWorld = false</c> across the seven configuration classes is a fact about them: they touch
/// only their own database. It is <b>not</b> a fact about the server, and the distinction is worth keeping
/// straight - <see cref="FlirtySessionTools"/> sets <c>OpenWorld = true</c> on its four writing tools,
/// because running a dialog publishes notifications and the core delivers those as outbound webhooks.
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
    // Not idempotent: the key is unique across all dialogs, so a repeat is a conflict rather than a no-op.
    [McpServerTool(
        Name = FlirtyToolNames.DialogCreate,
        UseStructuredContent = true,
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false)]
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

    [McpServerTool(
        Name = FlirtyToolNames.DialogList,
        UseStructuredContent = true,
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description("Lists all configured dialogs as metadata, without their graphs, sorted by key and "
        + "version. Read-only.")]
    internal static async Task<FlirtyDialogList> ListDialogsAsync(
        ISender sender, CancellationToken cancellationToken)
        => new(await sender.Send(new ListDialogsQuery(), cancellationToken));

    [McpServerTool(
        Name = FlirtyToolNames.DialogGet,
        UseStructuredContent = true,
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description("Reads one dialog along with its configuration graph: questions including answer "
        + "options, transitions, loop markers, triggers and the stored canvas positions. The dialog "
        + "metadata sits nested under 'dialog'. Read-only.")]
    internal static async Task<DialogDetail> GetDialogAsync(
        ISender sender,
        [Description("The id of the dialog to read.")]
        Guid dialogId,
        CancellationToken cancellationToken)
        => await sender.Send(new GetDialogQuery(dialogId), cancellationToken);

    // Idempotent: a full overwrite to a stated target state, so a retry after a timeout is safe.
    [McpServerTool(
        Name = FlirtyToolNames.DialogUpdate,
        UseStructuredContent = true,
        ReadOnly = false,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
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

    // Destructive, and not idempotent: the repeat is a 404, so a blind retry is not safe.
    [McpServerTool(
        Name = FlirtyToolNames.DialogDelete,
        UseStructuredContent = true,
        ReadOnly = false,
        Destructive = true,
        Idempotent = false,
        OpenWorld = false)]
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

    // Idempotent (an assignment; the repeat only bumps UpdatedAt) and NOT destructive: retiring the
    // predecessor version loses no data and is reversible by publishing it again. The description has to
    // name that side effect all the same, because a boolean cannot.
    [McpServerTool(
        Name = FlirtyToolNames.DialogPublish,
        UseStructuredContent = true,
        ReadOnly = false,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description("Publishes a dialog so it can be started in production. Requires an entry question, and "
        + "retires any previously published version of the same key. A published graph is locked.")]
    internal static async Task<DialogSummary> PublishDialogAsync(
        ISender sender,
        [Description("The id of the dialog to publish.")]
        Guid dialogId,
        CancellationToken cancellationToken)
        => await sender.Send(new PublishDialogCommand(dialogId), cancellationToken);

    [McpServerTool(
        Name = FlirtyToolNames.DialogUnpublish,
        UseStructuredContent = true,
        ReadOnly = false,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description("Withdraws a dialog from production. New sessions can no longer be started on it; "
        + "sessions already running keep their pinned version.")]
    internal static async Task<DialogSummary> UnpublishDialogAsync(
        ISender sender,
        [Description("The id of the dialog to unpublish.")]
        Guid dialogId,
        CancellationToken cancellationToken)
        => await sender.Send(new UnpublishDialogCommand(dialogId), cancellationToken);

    // Genuinely not idempotent: every call produces one more version.
    [McpServerTool(
        Name = FlirtyToolNames.DialogCreateVersion,
        UseStructuredContent = true,
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false)]
    [Description("Derives a new version from an existing dialog: clones the whole graph as an unpublished "
        + "draft with the version number raised by one. The intended way to evolve a published dialog "
        + "without breaking running sessions. Note that every cloned element gets a new id.")]
    internal static async Task<DialogDetail> CreateDialogVersionAsync(
        ISender sender,
        [Description("The id of the dialog version to copy.")]
        Guid dialogId,
        CancellationToken cancellationToken)
        => await sender.Send(new CreateDialogVersionCommand(dialogId), cancellationToken);

    // Destructive although it deletes nothing: ending live user sessions is irreversible, and that is
    // exactly what a client should ask about before doing. Idempotent all the same – the repeat ends 0
    // sessions and does not error.
    [McpServerTool(
        Name = FlirtyToolNames.DialogAbandonSessions,
        UseStructuredContent = true,
        ReadOnly = false,
        Destructive = true,
        Idempotent = true,
        OpenWorld = false)]
    [Description("Ends all sessions still running on a dialog version by setting them to abandoned, and "
        + "reports how many were ended. The precondition for deleting a dialog that is still in use.")]
    internal static async Task<AbandonSessionsResult> AbandonDialogSessionsAsync(
        ISender sender,
        [Description("The id of the dialog version whose sessions are ended.")]
        Guid dialogId,
        CancellationToken cancellationToken)
        => await sender.Send(new AbandonDialogSessionsCommand(dialogId), cancellationToken);

    [McpServerTool(
        Name = FlirtyToolNames.DialogCountActiveSessions,
        UseStructuredContent = true,
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description("Counts the sessions still in progress on a dialog version. Read-only; an operating aid "
        + "before unpublishing, deleting or deriving a version.")]
    internal static async Task<FlirtyActiveSessionCount> CountActiveSessionsAsync(
        ISender sender,
        [Description("The id of the dialog version.")]
        Guid dialogId,
        CancellationToken cancellationToken)
        => new(dialogId, await sender.Send(new CountActiveSessionsQuery(dialogId), cancellationToken));
}
