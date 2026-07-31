using System.ComponentModel;
using Flirty.Domain;
using Flirty.Runtime.Admin;
using Mediator;
using ModelContextProtocol.Server;

namespace Flirty.Mcp.Tools;

/// <summary>
/// The trigger tools: create, change and delete a trigger definition – the back channel out of a running
/// dialog. The MCP counterpart of <c>MapTriggerEndpoints</c>.
/// </summary>
/// <remarks>
/// <para>
/// The tool-shape conventions of every class in this package are documented once on
/// <see cref="FlirtyDialogTools"/> and deliberately not repeated here.
/// </para>
/// <para>
/// Three rules are enforced across fields by the commands themselves
/// (<c>IValidatableObject</c> → 400, no validation of this layer's own): scope
/// <c>AfterQuestion</c> needs a <c>questionId</c>, every other scope needs it <i>absent</i>, and kind
/// <c>Webhook</c> needs a <c>config</c> carrying an absolute http or https <c>url</c>. The last one exists
/// because a webhook without a URL would save fine and then silently never deliver.
/// </para>
/// <para>
/// Delivery is <b>best-effort</b> by design: a configuration, expression or delivery error is logged and
/// never thrown, because a trigger must not break a start, a submit or an edit. A client therefore cannot
/// tell from a successful create that anything will actually arrive. And <c>InProcess</c> deliberately
/// delivers nothing on its own – it raises a Mediator notification the <i>host application</i> handles, so
/// over MCP it is configuration for someone else's code.
/// </para>
/// </remarks>
[McpServerToolType]
internal sealed class FlirtyTriggerTools
{
    // Not idempotent, and like the transitions for the sharper reason: there is no unique key, so a repeat
    // adds a second trigger - and with it a second webhook delivery per event.
    [McpServerTool(
        Name = FlirtyToolNames.TriggerCreate,
        UseStructuredContent = true,
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false)]
    [Description("Creates a trigger on a dialog: a notification or an outbound webhook fired at a point in "
        + "the dialog. There is no unique key, so calling this twice fires twice. Refused with a conflict "
        + "while the dialog is published.")]
    internal static async Task<TriggerDetail> CreateTriggerAsync(
        ISender sender,
        [Description("The id of the dialog the trigger belongs to.")]
        Guid dialogId,
        [Description("When the trigger fires: OnDialogStarted, AfterAnswer (after every answer), "
            + "AfterQuestion (after one specific question) or OnDialogCompleted.")]
        TriggerScope scope,
        [Description("The channel. Webhook posts to the url in config. InProcess raises a notification "
            + "inside the host application and delivers nothing by itself - it is only useful if the host "
            + "registered a handler for it.")]
        TriggerKind kind,
        [Description("The channel configuration as a JSON object, camelCase: "
            + "{\"url\":\"https://host.example/hook\",\"name\":\"order-created\"}. url is required for kind "
            + "Webhook and must be an absolute http or https address; name is optional and is delivered as "
            + "the X-Flirty-Trigger header. For kind InProcess pass {} - an empty string is rejected. Only "
            + "these two fields survive a write; unknown fields are dropped.")]
        string config,
        CancellationToken cancellationToken,
        [Description("Required for scope AfterQuestion and must be absent for every other scope: the id of "
            + "the question after which the trigger fires.")]
        Guid? questionId = null,
        [Description("An optional condition, a sandboxed expression over the answers given so far, e.g. "
            + "amount > 100. The target is skipped when it evaluates to false - and also when it cannot be "
            + "evaluated at all, which is what happens if it names an answer that does not exist yet at "
            + "this scope (an answer key at OnDialogStarted, for instance). Null means always fire.")]
        string? expression = null)
        => await sender.Send(
            new CreateTriggerCommand(dialogId, scope, questionId, kind, config, expression),
            cancellationToken);

    // Idempotent: a full overwrite to a stated target state.
    [McpServerTool(
        Name = FlirtyToolNames.TriggerUpdate,
        UseStructuredContent = true,
        ReadOnly = false,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description("Updates a trigger in place. Every field is overwritten, so pass the current value for "
        + "everything that stays the same - in particular, omitting expression clears the condition and "
        + "makes the trigger fire unconditionally. Refused with a conflict while the dialog is published.")]
    internal static async Task<TriggerDetail> UpdateTriggerAsync(
        ISender sender,
        [Description("The id of the dialog the trigger belongs to.")]
        Guid dialogId,
        [Description("The id of the trigger to change.")]
        Guid triggerId,
        [Description("When the trigger fires: OnDialogStarted, AfterAnswer, AfterQuestion or "
            + "OnDialogCompleted.")]
        TriggerScope scope,
        [Description("The channel: Webhook or InProcess.")]
        TriggerKind kind,
        [Description("The channel configuration as a JSON object, camelCase: "
            + "{\"url\":\"https://host.example/hook\",\"name\":\"order-created\"}. Note that a read/write "
            + "cycle drops any field other than url and name, so re-sending what a read returned is "
            + "lossless but re-sending hand-added fields is not.")]
        string config,
        CancellationToken cancellationToken,
        [Description("Required for scope AfterQuestion and must be absent for every other scope: the id of "
            + "the question after which the trigger fires.")]
        Guid? questionId = null,
        [Description("The condition as a sandboxed expression over the answers given so far. Omitting this "
            + "argument clears it and the trigger fires unconditionally.")]
        string? expression = null)
        => await sender.Send(
            new UpdateTriggerCommand(dialogId, triggerId, scope, questionId, kind, config, expression),
            cancellationToken);

    // Destructive, and not idempotent: the repeat is a 404, so a blind retry is not safe.
    [McpServerTool(
        Name = FlirtyToolNames.TriggerDelete,
        UseStructuredContent = true,
        ReadOnly = false,
        Destructive = true,
        Idempotent = false,
        OpenWorld = false)]
    [Description("Deletes a trigger, so the event it was configured for stops being delivered. Refused "
        + "with a conflict while the dialog is published.")]
    internal static async Task<FlirtyAck> DeleteTriggerAsync(
        ISender sender,
        [Description("The id of the dialog the trigger belongs to.")]
        Guid dialogId,
        [Description("The id of the trigger to delete.")]
        Guid triggerId,
        CancellationToken cancellationToken)
    {
        await sender.Send(new DeleteTriggerCommand(dialogId, triggerId), cancellationToken);
        return FlirtyAck.Instance;
    }
}
