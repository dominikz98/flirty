using System.ComponentModel;
using Flirty.Runtime.Admin;
using Mediator;
using ModelContextProtocol.Server;

namespace Flirty.Mcp.Tools;

/// <summary>
/// The transition tools: create, change and delete a transition – the branching of the dialog graph. The
/// MCP counterpart of <c>MapTransitionEndpoints</c>.
/// </summary>
/// <remarks>
/// <para>
/// The tool-shape conventions of every class in this package are documented once on
/// <see cref="FlirtyDialogTools"/> and deliberately not repeated here.
/// </para>
/// <para>
/// What is specific to this area is how the runtime <i>picks</i> a transition, because that is what makes
/// <c>priority</c> and <c>isDefault</c> mean something. The outgoing transitions of an answered question
/// are ordered by ascending <c>priority</c>; the first non-default whose condition holds wins, and only if
/// none does the first default is taken. A question with no outgoing transition ends the dialog. A question
/// whose transitions all fail to apply and that has no default makes the dialog <b>misconfigured at
/// runtime</b> – a running session errors there, which is why the designer warns about it and why a client
/// should give every branching question a default.
/// </para>
/// <para>
/// Unlike the other areas there is <b>no unique key</b> on a transition: calling create twice with the
/// same arguments produces a second edge between the same two questions rather than a conflict. That is
/// also why these tools are the one place where a repeated create is genuinely visible in the graph.
/// </para>
/// </remarks>
[McpServerToolType]
internal sealed class FlirtyTransitionTools
{
    // Not idempotent, and for a different reason than the other creates: there is no unique key, so a
    // repeat silently adds a second edge instead of reporting a conflict.
    [McpServerTool(
        Name = FlirtyToolNames.TransitionCreate,
        UseStructuredContent = true,
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false)]
    [Description("Creates a transition from one question to another. Both questions must belong to the "
        + "dialog. There is no unique key, so calling this twice creates two edges. Refused with a conflict "
        + "while the dialog is published.")]
    internal static async Task<TransitionDetail> CreateTransitionAsync(
        ISender sender,
        [Description("The id of the dialog the transition belongs to.")]
        Guid dialogId,
        [Description("The id of the source question - the transition is evaluated once it is answered.")]
        Guid fromQuestionId,
        [Description("The id of the question to go to when this transition applies.")]
        Guid targetQuestionId,
        [Description("The evaluation order within the source question, ascending: the first non-default "
            + "transition whose condition holds wins. Use a gap-free ascending sequence per source "
            + "question.")]
        int priority,
        [Description("Whether this is the fallback of the source question, taken when no conditional "
            + "transition applies. A branching question without a default makes the dialog fail at runtime "
            + "once none of its conditions holds. At most one default per source question is useful; a "
            + "default ignores its expression.")]
        bool isDefault,
        CancellationToken cancellationToken,
        [Description("The condition, a sandboxed expression over the answers given so far, e.g. "
            + "role == \"dev\" or amount > 10. Answers are addressed by their question key and carry the "
            + "runtime type of the answer type (a date is a string). Null or empty means the transition "
            + "always applies - which for a non-default shadows every transition of lower priority.")]
        string? expression = null)
        => await sender.Send(
            new CreateTransitionCommand(
                dialogId, fromQuestionId, targetQuestionId, expression, priority, isDefault),
            cancellationToken);

    // Idempotent: a full overwrite to a stated target state.
    [McpServerTool(
        Name = FlirtyToolNames.TransitionUpdate,
        UseStructuredContent = true,
        ReadOnly = false,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description("Updates a transition in place, including its source and target question. Every field is "
        + "overwritten, so pass the current value for everything that stays the same - in particular, "
        + "omitting expression clears the condition and makes the transition always apply. Refused with a "
        + "conflict while the dialog is published.")]
    internal static async Task<TransitionDetail> UpdateTransitionAsync(
        ISender sender,
        [Description("The id of the dialog the transition belongs to.")]
        Guid dialogId,
        [Description("The id of the transition to change.")]
        Guid transitionId,
        [Description("The id of the source question.")]
        Guid fromQuestionId,
        [Description("The id of the target question.")]
        Guid targetQuestionId,
        [Description("The evaluation order within the source question, ascending.")]
        int priority,
        [Description("Whether this is the fallback of the source question.")]
        bool isDefault,
        CancellationToken cancellationToken,
        [Description("The condition, a sandboxed expression over the answers given so far. Omitting this "
            + "argument clears the stored condition.")]
        string? expression = null)
        => await sender.Send(
            new UpdateTransitionCommand(
                dialogId, transitionId, fromQuestionId, targetQuestionId, expression, priority, isDefault),
            cancellationToken);

    // Destructive, and not idempotent: the repeat is a 404, so a blind retry is not safe.
    [McpServerTool(
        Name = FlirtyToolNames.TransitionDelete,
        UseStructuredContent = true,
        ReadOnly = false,
        Destructive = true,
        Idempotent = false,
        OpenWorld = false)]
    [Description("Deletes a transition. Careful: removing the last outgoing transition of a question turns "
        + "it into an end of the dialog, and removing the only default of a branching question makes that "
        + "question fail at runtime once none of its conditions holds. Refused with a conflict while the "
        + "dialog is published.")]
    internal static async Task<FlirtyAck> DeleteTransitionAsync(
        ISender sender,
        [Description("The id of the dialog the transition belongs to.")]
        Guid dialogId,
        [Description("The id of the transition to delete.")]
        Guid transitionId,
        CancellationToken cancellationToken)
    {
        await sender.Send(new DeleteTransitionCommand(dialogId, transitionId), cancellationToken);
        return FlirtyAck.Instance;
    }
}
