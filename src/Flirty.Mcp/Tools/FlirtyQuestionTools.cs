using System.ComponentModel;
using Flirty.Domain;
using Flirty.Runtime.Admin;
using Mediator;
using ModelContextProtocol.Server;

namespace Flirty.Mcp.Tools;

/// <summary>
/// The question tools: create, change and delete a question of a dialog. The MCP counterpart of
/// <c>MapQuestionEndpoints</c>, a thin layer over the admin CRUD commands of <c>Flirty.Runtime.Admin</c>,
/// dispatched via <see cref="ISender"/>.
/// </summary>
/// <remarks>
/// <para>
/// The tool-shape conventions of every class in this package – <c>internal sealed</c> over <c>static</c>,
/// the explicit <c>= null</c> on every optional parameter, the injected <see cref="ISender"/>,
/// <c>UseStructuredContent = true</c>, the names from <see cref="FlirtyToolNames"/>, the four explicit
/// annotation hints and the return of the core records – are documented once on
/// <see cref="FlirtyDialogTools"/> and deliberately not repeated here.
/// </para>
/// <para>
/// Two things are specific to this area. <b>An update overwrites every field</b>, exactly as the HTTP
/// <c>PUT</c> does: an omitted <c>validationRules</c> therefore <i>clears</i> the stored rules rather than
/// leaving them alone – the parameter description says so, because a model would otherwise learn it by
/// losing a rule. And <b>a delete cascades</b>: <c>DeleteQuestionCommand</c> removes the answer options,
/// the transitions where the question is source or target, the loop markers whose entry or breaking
/// question it is, the <c>AfterQuestion</c> triggers on it and its canvas position, and it clears the
/// dialog's entry question if that pointed there. None of it is re-implemented here; naming it in the tool
/// description is this layer's whole contribution.
/// </para>
/// </remarks>
[McpServerToolType]
internal sealed class FlirtyQuestionTools
{
    // Not idempotent: the key is unique within the dialog, so a repeat is a conflict rather than a no-op.
    [McpServerTool(
        Name = FlirtyToolNames.QuestionCreate,
        UseStructuredContent = true,
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false)]
    [Description("Creates a question in a dialog. The key must be unique within the dialog. Refused with a "
        + "conflict while the dialog is published - derive a draft with flirty_dialog_create_version "
        + "first. Answer options are added afterwards with flirty_option_create.")]
    internal static async Task<QuestionDetail> CreateQuestionAsync(
        ISender sender,
        [Description("The id of the dialog the question belongs to.")]
        Guid dialogId,
        [Description("The business, stable key of the question. Must be unique within the dialog; it is "
            + "the name branching expressions use to refer to this answer.")]
        string key,
        [Description("The question text shown to the user.")]
        string text,
        [Description("The answer type. It decides which validation rules apply and whether answer options "
            + "are needed: SingleChoice and MultiChoice need them, FreeText, Number, Date, Boolean and "
            + "Json do not. Json accepts any well-formed JSON document and is also the type a "
            + "host-declared custom question type is authored on - see customTypeKey.")]
        QuestionType type,
        [Description("The sort index within the dialog, ascending. Duplicates are allowed but leave the "
            + "order of the tied questions arbitrary - use a gap-free ascending sequence.")]
        int order,
        [Description("Whether an answer is required.")]
        bool isRequired,
        CancellationToken cancellationToken,
        [Description("Optional validation rules as a JSON object, camelCase and type-scoped: "
            + "{\"minLength\":3,\"maxLength\":50,\"pattern\":\"^[a-z]+$\"} for FreeText (pattern is a .NET "
            + "regex matched partially - anchor it for a full match), {\"min\":0,\"max\":10} for Number. "
            + "Every field is optional; rules that do not apply to the type are ignored. Null means no "
            + "restriction.")]
        string? validationRules = null,
        [Description("Optional key of a host-declared custom question type, e.g. \"color\". Only allowed "
            + "when type is Json - on any other type the call is refused with 400. Call "
            + "flirty_question_type_list for the declared keys and their sample answers. An unknown key "
            + "is stored rather than refused, but the answer is then validated as plain JSON only.")]
        string? customTypeKey = null)
        => await sender.Send(
            new CreateQuestionCommand(
                dialogId, key, text, type, order, isRequired, validationRules, customTypeKey),
            cancellationToken);

    // Idempotent: a full overwrite to a stated target state, so a retry after a timeout is safe.
    [McpServerTool(
        Name = FlirtyToolNames.QuestionUpdate,
        UseStructuredContent = true,
        ReadOnly = false,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description("Updates a question in place. Every field is overwritten, so pass the current value for "
        + "everything that stays the same - in particular, omitting validationRules clears the stored "
        + "rules. Refused with a conflict while the dialog is published.")]
    internal static async Task<QuestionDetail> UpdateQuestionAsync(
        ISender sender,
        [Description("The id of the dialog the question belongs to.")]
        Guid dialogId,
        [Description("The id of the question to change.")]
        Guid questionId,
        [Description("The business, stable key of the question. Must stay unique within the dialog.")]
        string key,
        [Description("The question text shown to the user.")]
        string text,
        [Description("The answer type. Changing it can invalidate the answer options and the validation "
            + "rules of the question - neither is migrated.")]
        QuestionType type,
        [Description("The sort index within the dialog, ascending.")]
        int order,
        [Description("Whether an answer is required.")]
        bool isRequired,
        CancellationToken cancellationToken,
        [Description("The validation rules as a JSON object, camelCase and type-scoped: "
            + "{\"minLength\":3,\"maxLength\":50,\"pattern\":\"^[a-z]+$\"} for FreeText, "
            + "{\"min\":0,\"max\":10} for Number. Omitting this argument clears the stored rules.")]
        string? validationRules = null,
        [Description("The key of a host-declared custom question type, e.g. \"color\". Only allowed when "
            + "type is Json - on any other type the call is refused with 400. Omitting this argument "
            + "clears the stored key, which turns the question back into a plain JSON question. Call "
            + "flirty_question_type_list for the declared keys.")]
        string? customTypeKey = null)
        => await sender.Send(
            new UpdateQuestionCommand(
                dialogId, questionId, key, text, type, order, isRequired, validationRules, customTypeKey),
            cancellationToken);

    // Destructive, and not idempotent: the repeat is a 404, so a blind retry is not safe.
    [McpServerTool(
        Name = FlirtyToolNames.QuestionDelete,
        UseStructuredContent = true,
        ReadOnly = false,
        Destructive = true,
        Idempotent = false,
        OpenWorld = false)]
    [Description("Deletes a question together with its answer options, and cleans up everything that "
        + "referenced it: transitions where it is the source or the target, loop markers whose entry or "
        + "breaking question it is, AfterQuestion triggers on it, and its stored canvas position. If it "
        + "was the dialog's entry question, that reference is cleared. Refused with a conflict while the "
        + "dialog is published. Read the dialog with flirty_dialog_get afterwards to see the graph that is "
        + "left.")]
    internal static async Task<FlirtyAck> DeleteQuestionAsync(
        ISender sender,
        [Description("The id of the dialog the question belongs to.")]
        Guid dialogId,
        [Description("The id of the question to delete.")]
        Guid questionId,
        CancellationToken cancellationToken)
    {
        await sender.Send(new DeleteQuestionCommand(dialogId, questionId), cancellationToken);
        return FlirtyAck.Instance;
    }
}
