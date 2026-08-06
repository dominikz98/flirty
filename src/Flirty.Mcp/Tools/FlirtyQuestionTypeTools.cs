using System.ComponentModel;
using Flirty.Validation;
using ModelContextProtocol.Server;

namespace Flirty.Mcp.Tools;

/// <summary>
/// The custom question type tool: lists the question types the host declared with
/// <c>AddQuestionType</c>, so a client can author a question against one instead of guessing its key.
/// </summary>
/// <remarks>
/// <para>
/// The tool-shape conventions of every class in this package are documented once on
/// <see cref="FlirtyDialogTools"/> and deliberately not repeated here.
/// </para>
/// <para>
/// <b>This class has no <c>MapXxxEndpoints</c> counterpart, and never will</b> – which is why it is its
/// own class rather than a fourth tool on <see cref="FlirtyQuestionTools"/>, whose stated invariant is
/// that it mirrors <c>MapQuestionEndpoints</c> file against file. The source here is not a route but the
/// registry <c>AddFlirty</c> built out of the host's declarations, and that is what this tool is
/// reviewable against. <see cref="FlirtyDatabaseTools"/> set the same precedent for the same reason.
/// </para>
/// <para>
/// It sits in the <c>Admin</c> surface because it answers an <i>authoring</i> question – "what may I put
/// in <c>customTypeKey</c>?" – and is the explanation of a parameter on
/// <c>flirty_question_create</c>/<c>_update</c>, both of which are <c>Admin</c>. A <c>Runtime</c>-only
/// client does not get it and does not need it: the question view it receives already carries the key,
/// and the sample here is a convenience for composing an answer rather than a precondition.
/// </para>
/// </remarks>
[McpServerToolType]
internal sealed class FlirtyQuestionTypeTools
{
    private const string EmptyNote =
        "This host declared no custom question types. That is not an error: a question of type Json "
        + "works on its own and accepts any well-formed JSON document.";

    // Read-only host configuration, fixed at startup - the same annotation shape as
    // flirty_db_list_targets, and for the same reason.
    [McpServerTool(
        Name = FlirtyToolNames.QuestionTypeList,
        UseStructuredContent = true,
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description("Lists the custom question types this host declared. Such a type is authored as a "
        + "question of type Json carrying its key in customTypeKey, and the sample shows the answer shape "
        + "it expects. An empty list means the host declared none - Json still works on its own. A "
        + "customTypeKey that is not on this list is not refused: the answer is then validated as "
        + "well-formed JSON only, so the question stays usable but loses the host's own check.")]
    internal static FlirtyQuestionTypeList ListQuestionTypes(FlirtyQuestionTypeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        var types = registry.Types
            .Select(type => new FlirtyQuestionTypeInfo(type.Key, type.DisplayName, type.SampleValue))
            .ToList();

        return new FlirtyQuestionTypeList(types, types.Count == 0 ? EmptyNote : null);
    }
}
