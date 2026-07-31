using System.ComponentModel;
using Flirty.Runtime.Admin;
using Mediator;
using ModelContextProtocol.Server;

namespace Flirty.Mcp.Tools;

/// <summary>
/// The answer-option tools: create, change and delete an answer option of a question. The MCP counterpart
/// of <c>MapAnswerOptionEndpoints</c>.
/// </summary>
/// <remarks>
/// <para>
/// The tool-shape conventions of every class in this package are documented once on
/// <see cref="FlirtyDialogTools"/> and deliberately not repeated here. The wire names are
/// <c>flirty_option_*</c>, not <c>flirty_answer_option_*</c> – they follow the HTTP route segment
/// <c>.../options</c>.
/// </para>
/// <para>
/// The one thing worth stating here is the distinction that has already cost this repository a bug (#47):
/// <b>the label is displayed, the value is stored</b>. An answer to a <c>SingleChoice</c> or
/// <c>MultiChoice</c> question is validated by the <c>AnswerValidator</c> against the option
/// <i>values</i>, and it is the value a branching expression compares against – so a client that submits
/// the label gets a 400, not a match.
/// </para>
/// <para>
/// Options only mean something on <c>SingleChoice</c> and <c>MultiChoice</c> questions. Creating them on
/// another type is not refused (the engine stores them and the runtime ignores them), which is why the
/// tool description says so instead of a validation rule saying it.
/// </para>
/// </remarks>
[McpServerToolType]
internal sealed class FlirtyAnswerOptionTools
{
    // Not idempotent: the key is unique within the question, so a repeat is a conflict rather than a no-op.
    [McpServerTool(
        Name = FlirtyToolNames.OptionCreate,
        UseStructuredContent = true,
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false)]
    [Description("Creates an answer option on a question. Only SingleChoice and MultiChoice questions use "
        + "options; on any other type they are stored but never offered. The key must be unique within the "
        + "question. Refused with a conflict while the dialog is published.")]
    internal static async Task<AnswerOptionDetail> CreateAnswerOptionAsync(
        ISender sender,
        [Description("The id of the dialog the question belongs to.")]
        Guid dialogId,
        [Description("The id of the question the option belongs to.")]
        Guid questionId,
        [Description("The business, stable key of the option. Must be unique within the question.")]
        string key,
        [Description("The label shown to the user. Display only - it is never what gets stored.")]
        string label,
        [Description("The value stored as the answer. This is what answer validation checks against and "
            + "what a branching expression compares, so keep it stable and machine-friendly.")]
        string value,
        [Description("The sort index within the question, ascending.")]
        int order,
        CancellationToken cancellationToken)
        => await sender.Send(
            new CreateAnswerOptionCommand(dialogId, questionId, key, label, value, order),
            cancellationToken);

    // Idempotent: a full overwrite to a stated target state.
    [McpServerTool(
        Name = FlirtyToolNames.OptionUpdate,
        UseStructuredContent = true,
        ReadOnly = false,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description("Updates an answer option in place. Every field is overwritten, so pass the current value "
        + "for everything that stays the same. Careful with value: answers already stored keep the old "
        + "value, and branching expressions comparing it are not migrated. Refused with a conflict while "
        + "the dialog is published.")]
    internal static async Task<AnswerOptionDetail> UpdateAnswerOptionAsync(
        ISender sender,
        [Description("The id of the dialog the question belongs to.")]
        Guid dialogId,
        [Description("The id of the question the option belongs to.")]
        Guid questionId,
        [Description("The id of the option to change.")]
        Guid optionId,
        [Description("The business, stable key of the option. Must stay unique within the question.")]
        string key,
        [Description("The label shown to the user. Display only.")]
        string label,
        [Description("The value stored as the answer - what validation and branching compare against.")]
        string value,
        [Description("The sort index within the question, ascending.")]
        int order,
        CancellationToken cancellationToken)
        => await sender.Send(
            new UpdateAnswerOptionCommand(dialogId, questionId, optionId, key, label, value, order),
            cancellationToken);

    // Destructive, and not idempotent: the repeat is a 404, so a blind retry is not safe.
    [McpServerTool(
        Name = FlirtyToolNames.OptionDelete,
        UseStructuredContent = true,
        ReadOnly = false,
        Destructive = true,
        Idempotent = false,
        OpenWorld = false)]
    [Description("Deletes an answer option. The question keeps its remaining options; answers already "
        + "stored are untouched, so a completed session can carry a value no option offers any more. "
        + "Refused with a conflict while the dialog is published.")]
    internal static async Task<FlirtyAck> DeleteAnswerOptionAsync(
        ISender sender,
        [Description("The id of the dialog the question belongs to.")]
        Guid dialogId,
        [Description("The id of the question the option belongs to.")]
        Guid questionId,
        [Description("The id of the option to delete.")]
        Guid optionId,
        CancellationToken cancellationToken)
    {
        await sender.Send(new DeleteAnswerOptionCommand(dialogId, questionId, optionId), cancellationToken);
        return FlirtyAck.Instance;
    }
}
