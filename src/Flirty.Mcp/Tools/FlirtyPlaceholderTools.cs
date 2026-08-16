using System.ComponentModel;
using Flirty.Placeholders;
using ModelContextProtocol.Server;

namespace Flirty.Mcp.Tools;

/// <summary>
/// The message-placeholder tool: lists the placeholders the host declared with <c>AddPlaceholder</c>, so a
/// client can put a valid <c>{{key}}</c> marker into a question text or option label instead of guessing.
/// </summary>
/// <remarks>
/// <para>
/// The tool-shape conventions of every class in this package are documented once on
/// <see cref="FlirtyDialogTools"/> and deliberately not repeated here.
/// </para>
/// <para>
/// <b>This class has no <c>MapXxxEndpoints</c> counterpart, and never will</b> – the exact twin of
/// <see cref="FlirtyQuestionTypeTools"/>. The source here is not a route but the registry
/// <c>AddFlirty</c> built out of the host's declarations, and that is what this tool is reviewable against.
/// </para>
/// <para>
/// It sits in the <c>Admin</c> surface because it answers an <i>authoring</i> question – "what may I put
/// inside a <c>{{ }}</c> marker?" – and is the explanation of a field on
/// <c>flirty_question_create</c>/<c>_update</c>, both of which are <c>Admin</c>. A <c>Runtime</c>-only
/// client does not get it and does not need it: it receives the already-filled question text, not the raw
/// markers.
/// </para>
/// </remarks>
[McpServerToolType]
internal sealed class FlirtyPlaceholderTools
{
    private const string EmptyNote =
        "This host declared no message placeholders. A {{key}} marker in a message is then left as "
        + "written - there is no filler to produce a value for it.";

    // Read-only host configuration, fixed at startup - the same annotation shape as
    // flirty_question_type_list, and for the same reason.
    [McpServerTool(
        Name = FlirtyToolNames.PlaceholderList,
        UseStructuredContent = true,
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description("Lists the message placeholders this host declared. A question text or an answer-option "
        + "label references one with the marker {{key}}, and at delivery time the host replaces it with a "
        + "live value; the sample shows an example. An empty list means the host declared none, so a marker "
        + "is left as written. A marker whose key is not on this list is not refused: it simply stays raw in "
        + "the delivered message.")]
    internal static FlirtyPlaceholderList ListPlaceholders(FlirtyPlaceholderRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        var placeholders = registry.Placeholders
            .Select(placeholder => new FlirtyPlaceholderInfo(
                placeholder.Key, placeholder.DisplayName, placeholder.Sample))
            .ToList();

        return new FlirtyPlaceholderList(placeholders, placeholders.Count == 0 ? EmptyNote : null);
    }
}
