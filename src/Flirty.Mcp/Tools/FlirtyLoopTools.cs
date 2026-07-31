using System.ComponentModel;
using Flirty.Runtime.Admin;
using Mediator;
using ModelContextProtocol.Server;

namespace Flirty.Mcp.Tools;

/// <summary>
/// The loop tools: create, change and delete a loop marker. The MCP counterpart of
/// <c>MapLoopEndpoints</c>.
/// </summary>
/// <remarks>
/// <para>
/// The tool-shape conventions of every class in this package are documented once on
/// <see cref="FlirtyDialogTools"/> and deliberately not repeated here.
/// </para>
/// <para>
/// What a client has to understand about this area is that <b>a loop marker does not create the loop</b>.
/// The cycle is made of ordinary transitions – a back jump from a later question to an earlier one – and
/// the runtime has no special path for loops at all. The marker adds two things on top: the answers of the
/// questions inside the cycle are <i>collected per iteration</i> under <c>collectionKey</c> instead of
/// being overwritten, so an expression can say <c>positions.Count &gt; 0</c>, and the designer draws the
/// range as a block. So the order is: build the cycle with flirty_transition_create, then mark it here.
/// </para>
/// <para>
/// The two question references are stored <b>unchecked</b> – they are deliberately not foreign keys, so a
/// marker over a nonsensical range is accepted and simply describes nothing. Only <c>collectionKey</c> is
/// enforced, and only for uniqueness within the dialog.
/// </para>
/// </remarks>
[McpServerToolType]
internal sealed class FlirtyLoopTools
{
    // Not idempotent: the collection key is unique per dialog, so a repeat is a conflict rather than a
    // no-op.
    [McpServerTool(
        Name = FlirtyToolNames.LoopCreate,
        UseStructuredContent = true,
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false)]
    [Description("Marks an existing cycle in the graph as a loop, so its answers are collected per "
        + "iteration instead of overwritten. Create the cycle first with flirty_transition_create - this "
        + "tool adds no transition. The collection key must be unique within the dialog. Refused with a "
        + "conflict while the dialog is published.")]
    internal static async Task<LoopDetail> CreateLoopAsync(
        ISender sender,
        [Description("The id of the dialog the loop belongs to.")]
        Guid dialogId,
        [Description("The key under which the collected answers of each iteration are available in "
            + "branching expressions, e.g. positions for positions.Count > 0. Must be unique within the "
            + "dialog, and a plural noun reads best.")]
        string collectionKey,
        [Description("The id of the question the loop starts at - the target of the back jump.")]
        Guid entryQuestionId,
        [Description("The id of the breaking question - the question whose exit transition leaves the "
            + "cycle. It needs a transition out of the loop, otherwise the loop never ends.")]
        Guid breakingQuestionId,
        CancellationToken cancellationToken)
        => await sender.Send(
            new CreateLoopCommand(dialogId, collectionKey, entryQuestionId, breakingQuestionId),
            cancellationToken);

    // Idempotent: a full overwrite to a stated target state.
    [McpServerTool(
        Name = FlirtyToolNames.LoopUpdate,
        UseStructuredContent = true,
        ReadOnly = false,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description("Updates a loop marker in place. Every field is overwritten, so pass the current value for "
        + "everything that stays the same. Careful with collectionKey: branching expressions referring to "
        + "the old key are not migrated and stop resolving. Refused with a conflict while the dialog is "
        + "published.")]
    internal static async Task<LoopDetail> UpdateLoopAsync(
        ISender sender,
        [Description("The id of the dialog the loop belongs to.")]
        Guid dialogId,
        [Description("The id of the loop marker to change.")]
        Guid loopId,
        [Description("The key under which the collected answers are available in expressions. Must stay "
            + "unique within the dialog.")]
        string collectionKey,
        [Description("The id of the entry question of the loop.")]
        Guid entryQuestionId,
        [Description("The id of the breaking question of the loop.")]
        Guid breakingQuestionId,
        CancellationToken cancellationToken)
        => await sender.Send(
            new UpdateLoopCommand(dialogId, loopId, collectionKey, entryQuestionId, breakingQuestionId),
            cancellationToken);

    // Destructive, and not idempotent: the repeat is a 404, so a blind retry is not safe.
    [McpServerTool(
        Name = FlirtyToolNames.LoopDelete,
        UseStructuredContent = true,
        ReadOnly = false,
        Destructive = true,
        Idempotent = false,
        OpenWorld = false)]
    [Description("Deletes a loop marker. The cycle in the graph stays exactly as it was - only the "
        + "collecting of answers per iteration stops, so expressions using the collection key no longer "
        + "resolve. Refused with a conflict while the dialog is published.")]
    internal static async Task<FlirtyAck> DeleteLoopAsync(
        ISender sender,
        [Description("The id of the dialog the loop belongs to.")]
        Guid dialogId,
        [Description("The id of the loop marker to delete.")]
        Guid loopId,
        CancellationToken cancellationToken)
    {
        await sender.Send(new DeleteLoopCommand(dialogId, loopId), cancellationToken);
        return FlirtyAck.Instance;
    }
}
