using System.ComponentModel;
using Flirty.Runtime.Admin;
using Mediator;
using ModelContextProtocol.Server;

namespace Flirty.Mcp.Tools;

/// <summary>
/// The layout tools: set and discard the canvas positions of a dialog's elements. The MCP counterpart of
/// <c>MapLayoutEndpoints</c>.
/// </summary>
/// <remarks>
/// <para>
/// The tool-shape conventions of every class in this package are documented once on
/// <see cref="FlirtyDialogTools"/>. These two tools break one of them, and they are the only place in the
/// package that does: <c>flirty_layout_set</c> takes a <b>complex</b> parameter
/// (<see cref="DialogLayoutEntry"/><c>[]</c>) rather than primitives. A batch is not expressible as a
/// scalar parameter, and the alternative – one call per element – would turn arranging a graph into one
/// round trip and one transaction per node, each answering with the <i>whole</i> layout. It is admissible
/// here because the SDK generates the schema inline (camelCase properties, the element kind as a
/// name-constrained string, all four fields required), so a model sees the entry shape rather than an
/// opaque blob; and the core record is used directly for the same reason the tools return the core records
/// – <c>Flirty.AspNetCore</c>'s <c>DialogLayoutEntryRequest</c> is a field-for-field copy that exists only
/// because HTTP needs a body wrapper.
/// </para>
/// <para>
/// <b>These are the two tools the publish lock does not reach.</b>
/// <c>Set</c>/<c>ResetDialogLayoutCommand</c> deliberately run without <c>DialogEditGuard</c>: canvas
/// positions live in their own table and do not touch session semantics, so a published dialog must stay
/// arrangeable (ADR 0007) – and a published dialog is the one opened most often. That is the edge of the
/// lock, not a gap in it, and it is stated in both tool descriptions, because otherwise it reads later like
/// a missing guard.
/// </para>
/// <para>
/// Neither tool guards its input. An empty or missing batch, a duplicate element and a negative coordinate
/// are all rejected by <c>SetDialogLayoutCommand</c>'s own validation as a 400 – catching them here would
/// produce the same 400 by a longer road and duplicate a rule that has one home.
/// </para>
/// </remarks>
[McpServerToolType]
internal sealed class FlirtyLayoutTools
{
    // Idempotent: an upsert keyed by (elementKind, elementId), so re-sending the same batch changes nothing.
    [McpServerTool(
        Name = FlirtyToolNames.LayoutSet,
        UseStructuredContent = true,
        ReadOnly = false,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description("Sets canvas positions of a dialog's elements - a batch upsert: an element named in "
        + "entries is placed or moved, an element not named keeps whatever position it had. Works on a "
        + "PUBLISHED dialog too and never reports a conflict, because canvas positions are not part of the "
        + "graph and the publish lock does not apply to them. Returns the complete layout of the dialog "
        + "afterwards, not only the rows that were set, so a client can replace its state with the result. "
        + "Discard everything with flirty_layout_reset.")]
    internal static async Task<FlirtyDialogLayout> SetDialogLayoutAsync(
        ISender sender,
        [Description("The id of the dialog whose layout is set.")]
        Guid dialogId,
        [Description("The positions to set: at least one entry, at most one entry per element. An entry is "
            + "{\"elementKind\":\"Question\",\"elementId\":\"<question id>\",\"x\":120,\"y\":40}; "
            + "coordinates are pixels from the top left and must not be negative. Question is the only "
            + "element kind that has a position today. The element id is not checked for existence.")]
        DialogLayoutEntry[] entries,
        CancellationToken cancellationToken)
        => new(await sender.Send(new SetDialogLayoutCommand(dialogId, entries), cancellationToken));

    // Destructive, but unlike the deletes it is idempotent: discarding an already empty layout succeeds
    // rather than reporting a 404, so the repeat is safe.
    [McpServerTool(
        Name = FlirtyToolNames.LayoutReset,
        UseStructuredContent = true,
        ReadOnly = false,
        Destructive = true,
        Idempotent = true,
        OpenWorld = false)]
    [Description("Discards all stored canvas positions of a dialog, so the designer's auto-layout arranges "
        + "it again. The graph itself is untouched. Works on a PUBLISHED dialog too and never reports a "
        + "conflict, for the same reason flirty_layout_set does not. Discarding an already empty layout "
        + "succeeds.")]
    internal static async Task<FlirtyAck> ResetDialogLayoutAsync(
        ISender sender,
        [Description("The id of the dialog whose layout is discarded.")]
        Guid dialogId,
        CancellationToken cancellationToken)
    {
        await sender.Send(new ResetDialogLayoutCommand(dialogId), cancellationToken);
        return FlirtyAck.Instance;
    }
}
