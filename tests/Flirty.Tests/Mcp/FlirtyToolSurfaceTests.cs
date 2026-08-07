using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Flirty.Mcp.Tools;
using ModelContextProtocol.Server;

namespace Flirty.Tests.Mcp;

/// <summary>
/// The tool-surface contract of <c>Flirty.Mcp</c> (#127): which tools exist, and what each of them
/// advertises to a client. Separate from <see cref="MapFlirtyMcpTests"/> because this surface grows with
/// every build-out stage of the EPIC while the host wiring does not.
/// </summary>
/// <remarks>
/// <para>
/// The golden list is deliberately <b>literal</b> on one side. Deriving both the expectation and the actual
/// value from <see cref="FlirtyToolNames"/> would make a renamed const change both sides at once and stay
/// green – exactly the failure the checklist exists to prevent. With literals, a rename forces a visible
/// three-place edit: the attribute, the const and this list.
/// </para>
/// <para>
/// What these tests cannot see, and it is worth saying rather than leaving a reader to assume more coverage
/// than exists: a tool that writes its name as a string literal instead of referencing the const emits an
/// identical wire name, so no assertion can distinguish it. That one is a review concern. What the tests do
/// close is the completeness of the checklist in both directions, plus the case nothing else would catch –
/// a tool class added under <c>Tools/</c> and forgotten in the <c>WithTools</c> chain of
/// <c>AddFlirtyMcp</c>, which compiles, ships and is invisible to every client.
/// </para>
/// <para>
/// Every test here runs against <see cref="StartFullSurfaceAsync"/> rather than the default host, because
/// since #129 one tool is registered conditionally. A default host would serve 37 of the 38 and the golden
/// list would have to subtract one – which is precisely the shape of hiding this checklist exists to
/// prevent. The gate itself is asserted where it belongs, in <see cref="FlirtyDatabaseToolsTests"/>.
/// </para>
/// </remarks>
public sealed class FlirtyToolSurfaceTests
{
    /// <summary>
    /// A host serving the whole surface, migrate tool included. <c>AllowMigrations()</c> is the only way to
    /// see <c>flirty_db_migrate</c> at all – gating by absence means the tool is not registered without it.
    /// </summary>
    private static Task<FlirtyMcpTestHost> StartFullSurfaceAsync() =>
        FlirtyMcpTestHost.StartAsync(configureMcp: options => options.AllowMigrations());

    /// <summary>
    /// The 27 admin tools of EPIC 13 stages 1 and 2, the 5 session tools of stage 3, the 4 database tools
    /// of stage 4, <c>flirty_question_type_list</c> from #136 and <c>flirty_placeholder_list</c> from #140
    /// are registered, and nothing else. The list is ordinal-sorted so a diff on it reads by area.
    /// </summary>
    [Fact]
    public async Task ListTools_returns_the_thirty_eight_tools()
    {
        await using var host = await StartFullSurfaceAsync();

        var tools = await host.Mcp.ListToolsAsync();

        Assert.Equal(
            ExpectedToolNames,
            tools.Select(tool => tool.Name).OrderBy(name => name, StringComparer.Ordinal));
    }

    /// <summary>
    /// Every const in <see cref="FlirtyToolNames"/> names a tool that exists, and every tool has a const –
    /// the checklist is complete in both directions. Reflection over the literal fields, so a new const
    /// joins the checklist by itself instead of waiting for someone to remember a second list.
    /// </summary>
    [Fact]
    public void FlirtyToolNames_holds_exactly_the_expected_tool_names()
    {
        var declared = typeof(FlirtyToolNames)
            .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .OrderBy(name => name, StringComparer.Ordinal);

        Assert.Equal(ExpectedToolNames, declared);
    }

    /// <summary>
    /// Every <c>[McpServerToolType]</c> class of the package is really registered with the server, and every
    /// one of its tools sets its name explicitly.
    /// </summary>
    /// <remarks>
    /// This is the only test that sees a forgotten <c>WithTools&lt;T&gt;()</c>: such a class compiles and
    /// simply never reaches a client, the same silent-registration family as the places a new packable
    /// project has to be listed in. The <c>Name</c> check is the second half – without it the SDK derives a
    /// name from the method name (stripping <c>Async</c>, snake_casing the rest), which turns a C# rename
    /// into a breaking change for every client.
    /// </remarks>
    [Fact]
    public async Task Every_tool_type_of_the_package_is_registered_with_the_server()
    {
        await using var host = await StartFullSurfaceAsync();
        var served = (await host.Mcp.ListToolsAsync()).Select(tool => tool.Name).ToHashSet(StringComparer.Ordinal);

        var declared = typeof(FlirtyToolNames).Assembly
            .GetTypes()
            .Where(type => type.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            .Select(method => method.GetCustomAttribute<McpServerToolAttribute>())
            .Where(attribute => attribute is not null)
            .ToList();

        Assert.NotEmpty(declared);
        Assert.All(declared, attribute => Assert.NotNull(attribute!.Name));
        Assert.All(
            declared,
            attribute => Assert.True(
                served.Contains(attribute!.Name!),
                $"The tool '{attribute.Name}' is declared in the assembly but not served - is its tool "
                + "class missing from the WithTools chain of AddFlirtyMcp?"));
    }

    /// <summary>Every tool name follows the <c>flirty_&lt;area&gt;_&lt;action&gt;</c> shape.</summary>
    /// <remarks>
    /// Redundant for the 37 names the golden list pins, and kept anyway: it is the only guard left when a
    /// later stage adds a tool and updates the literal list to match a badly shaped name.
    /// </remarks>
    [Fact]
    public async Task Every_tool_name_follows_the_flirty_area_action_shape()
    {
        await using var host = await StartFullSurfaceAsync();

        var tools = await host.Mcp.ListToolsAsync();

        Assert.All(
            tools,
            tool => Assert.Matches(new Regex("^flirty_[a-z]+(_[a-z]+)+$"), tool.Name));
    }

    /// <summary>
    /// Every tool advertises an output schema and a non-empty description – the two halves of AC 2 of #127.
    /// </summary>
    /// <remarks>
    /// The output schema is what <c>UseStructuredContent = true</c> buys, and forgetting it on a new tool has
    /// no other symptom: the call still succeeds, the result is just prose the client has to parse.
    /// <c>Assert.All</c> aggregates, so one run answers "which ones did I forget" rather than the first one.
    /// </remarks>
    [Fact]
    public async Task Every_tool_advertises_an_output_schema_and_a_non_empty_description()
    {
        await using var host = await StartFullSurfaceAsync();

        var tools = await host.Mcp.ListToolsAsync();

        Assert.All(
            tools,
            tool =>
            {
                Assert.True(
                    tool.ProtocolTool.OutputSchema.HasValue,
                    $"The tool '{tool.Name}' advertises no outputSchema - UseStructuredContent missing?");
                Assert.Equal(JsonValueKind.Object, tool.ProtocolTool.OutputSchema!.Value.ValueKind);
                Assert.False(
                    string.IsNullOrWhiteSpace(tool.ProtocolTool.Description),
                    $"The tool '{tool.Name}' has no description.");
            });
    }

    /// <summary>
    /// Every tool declares all four annotation hints a client gates its confirmation prompts on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// All four hints are <c>bool?</c> from the attribute to the wire, and an <i>unset</i> one is simply
    /// absent – whereupon the protocol lets a client assume <c>destructive</c> and <c>openWorld</c>. So
    /// omitting is not neutral: unset, every create would look like it might destroy data. Hence "set, not
    /// defaulted", and hence this test asserts a total matrix rather than a partial one.
    /// </para>
    /// <para>
    /// The comparison is <c>Assert.Equal&lt;bool?&gt;</c> on purpose. <c>Assert.False</c> accepts a
    /// <c>bool?</c> and reads <see langword="null"/> as <see langword="false"/>, so it would pass on exactly
    /// the bug this test exists to catch.
    /// </para>
    /// <para>
    /// <c>openWorld</c> became a column in #128 and was a hard-coded <c>false</c> before it. That was
    /// right while the surface was configuration only – the tools touched nothing but their own database –
    /// but the session tools run dialogs, and a run delivers the engine's notifications as outbound
    /// webhooks. A constant in the assertion would now be pinning the wrong answer for five tools.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(ToolAnnotations))]
    public async Task Every_tool_declares_the_annotations_a_client_gates_its_prompts_on(
        string tool, bool readOnly, bool destructive, bool idempotent, bool openWorld)
    {
        await using var host = await StartFullSurfaceAsync();

        var annotations = Assert.Single(await host.Mcp.ListToolsAsync(), t => t.Name == tool)
            .ProtocolTool.Annotations;

        Assert.NotNull(annotations);
        Assert.Equal<bool?>(readOnly, annotations.ReadOnlyHint);
        Assert.Equal<bool?>(destructive, annotations.DestructiveHint);
        Assert.Equal<bool?>(idempotent, annotations.IdempotentHint);
        Assert.Equal<bool?>(openWorld, annotations.OpenWorldHint);
    }

    /// <summary>
    /// The batch parameter of <c>flirty_layout_set</c> – the package's one complex parameter – is advertised
    /// as a real array of objects rather than as an opaque blob.
    /// </summary>
    /// <remarks>
    /// This is what makes the exception to the "primitives, Guid and enums only" rule admissible: the schema
    /// is generated inline (no <c>$defs</c> a client has to resolve), the properties are camelCase, the
    /// element kind is a name-constrained string like every other enum on this surface, and all four fields
    /// are required. If the SDK ever stopped doing that, the exception would stop being defensible.
    /// </remarks>
    [Fact]
    public async Task The_layout_batch_is_advertised_as_an_array_of_entry_objects()
    {
        await using var host = await StartFullSurfaceAsync();

        var tool = Assert.Single(await host.Mcp.ListToolsAsync(), t => t.Name == FlirtyToolNames.LayoutSet);

        var entries = tool.ProtocolTool.InputSchema.GetProperty("properties").GetProperty("entries");
        Assert.Equal("array", entries.GetProperty("type").GetString());
        var items = entries.GetProperty("items");
        Assert.Equal(
            ["elementKind", "elementId", "x", "y"],
            items.GetProperty("required").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal(
            ["Question"],
            items.GetProperty("properties").GetProperty("elementKind").GetProperty("enum")
                .EnumerateArray().Select(value => value.GetString()));
    }

    /// <summary>
    /// The server instructions reach the client and name all three arguments that are JSON inside a string.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Worth pinning because the property's own documentation says instructions travel "during the
    /// initialization handshake" – and revision <c>2026-07-28</c>, which this stateless server serves, has no
    /// handshake. They arrive all the same, but <b>not</b> over <c>discover</c>: this server answers that
    /// method with <c>-32601</c>, and the SDK's own client still handshakes, negotiating <c>2025-06-18</c>.
    /// Stateless removed the session header, not the handshake.
    /// </para>
    /// <para>
    /// All three payloads are asserted, not two. The redundancy rule these instructions rest on is that
    /// every fact here is <i>also</i> in a tool or parameter description – which only matters because a
    /// client that skips the handshake receives none of this text. A payload that quietly stopped being
    /// mentioned would take the guidance with it. Asserted by keyword rather than as a whole text: editing
    /// the wording of a documentation string must not turn a test red.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Server_instructions_reach_the_client_and_name_the_three_json_payloads()
    {
        await using var host = await StartFullSurfaceAsync();

        var instructions = host.Mcp.ServerInstructions;

        Assert.False(string.IsNullOrWhiteSpace(instructions));
        Assert.Contains("validationRules", instructions, StringComparison.Ordinal);
        Assert.Contains("pattern", instructions, StringComparison.Ordinal);
        Assert.Contains("X-Flirty-Trigger", instructions, StringComparison.Ordinal);
        Assert.Contains("url", instructions, StringComparison.Ordinal);
        Assert.Contains("MultiChoice", instructions, StringComparison.Ordinal);
        Assert.Contains("decimal separator", instructions, StringComparison.Ordinal);

        // The route-selected target is the one fact with no parameter of its own to live in, so its
        // fallback channel is the description of flirty_db_list_targets. Both halves are pinned.
        Assert.Contains("/mcp/staging", instructions, StringComparison.Ordinal);
        Assert.Contains(
            "path segment",
            Assert.Single(await host.Mcp.ListToolsAsync(), t => t.Name == FlirtyToolNames.DatabaseListTargets)
                .ProtocolTool.Description,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The annotation matrix, tool by tool. <c>openWorld</c> separates the two halves of the surface: it is
    /// <see langword="false"/> for the 29 configuration and 4 database tools, which touch only their own
    /// database, and <see langword="true"/> for the four session tools that write – running a dialog
    /// publishes engine notifications, and the core delivers those to whatever url a webhook trigger names.
    /// </summary>
    /// <remarks>
    /// <c>flirty_db_migrate</c> is the second entry after <c>flirty_dialog_abandon_sessions</c> to be
    /// <c>destructive</c> while deleting nothing: an applied migration cannot be taken back through this
    /// surface. It stays <c>idempotent</c>, because a second call finds nothing pending.
    /// </remarks>
    public static TheoryData<string, bool, bool, bool, bool> ToolAnnotations =>
        new()
        {
            // tool, readOnly, destructive, idempotent, openWorld
            { FlirtyToolNames.DialogCreate, false, false, false, false },
            { FlirtyToolNames.DialogList, true, false, true, false },
            { FlirtyToolNames.DialogGet, true, false, true, false },
            { FlirtyToolNames.DialogUpdate, false, false, true, false },
            { FlirtyToolNames.DialogDelete, false, true, false, false },
            { FlirtyToolNames.DialogPublish, false, false, true, false },
            { FlirtyToolNames.DialogUnpublish, false, false, true, false },
            { FlirtyToolNames.DialogCreateVersion, false, false, false, false },
            { FlirtyToolNames.DialogAbandonSessions, false, true, true, false },
            { FlirtyToolNames.DialogCountActiveSessions, true, false, true, false },
            { FlirtyToolNames.QuestionCreate, false, false, false, false },
            { FlirtyToolNames.QuestionUpdate, false, false, true, false },
            { FlirtyToolNames.QuestionDelete, false, true, false, false },
            { FlirtyToolNames.QuestionTypeList, true, false, true, false },
            { FlirtyToolNames.PlaceholderList, true, false, true, false },
            { FlirtyToolNames.OptionCreate, false, false, false, false },
            { FlirtyToolNames.OptionUpdate, false, false, true, false },
            { FlirtyToolNames.OptionDelete, false, true, false, false },
            { FlirtyToolNames.TransitionCreate, false, false, false, false },
            { FlirtyToolNames.TransitionUpdate, false, false, true, false },
            { FlirtyToolNames.TransitionDelete, false, true, false, false },
            { FlirtyToolNames.LoopCreate, false, false, false, false },
            { FlirtyToolNames.LoopUpdate, false, false, true, false },
            { FlirtyToolNames.LoopDelete, false, true, false, false },
            { FlirtyToolNames.TriggerCreate, false, false, false, false },
            { FlirtyToolNames.TriggerUpdate, false, false, true, false },
            { FlirtyToolNames.TriggerDelete, false, true, false, false },
            { FlirtyToolNames.LayoutSet, false, false, true, false },
            { FlirtyToolNames.LayoutReset, false, true, true, false },
            { FlirtyToolNames.SessionStart, false, false, true, true },
            { FlirtyToolNames.SessionStartVersion, false, false, true, true },
            { FlirtyToolNames.SessionGet, true, false, true, false },
            { FlirtyToolNames.SessionSubmitAnswer, false, false, false, true },
            { FlirtyToolNames.SessionEditAnswer, false, true, true, true },
            { FlirtyToolNames.DatabaseListTargets, true, false, true, false },
            { FlirtyToolNames.DatabaseTestConnection, true, false, true, false },
            { FlirtyToolNames.DatabasePendingMigrations, true, false, true, false },
            { FlirtyToolNames.DatabaseMigrate, false, true, true, false },
        };

    /// <summary>
    /// The wire names of the whole surface, ordinal-sorted, as literals – the one side of the golden
    /// comparison that is deliberately not derived from <see cref="FlirtyToolNames"/>.
    /// </summary>
    private static string[] ExpectedToolNames =>
        [
            "flirty_db_list_targets",
            "flirty_db_migrate",
            "flirty_db_pending_migrations",
            "flirty_db_test_connection",
            "flirty_dialog_abandon_sessions",
            "flirty_dialog_count_active_sessions",
            "flirty_dialog_create",
            "flirty_dialog_create_version",
            "flirty_dialog_delete",
            "flirty_dialog_get",
            "flirty_dialog_list",
            "flirty_dialog_publish",
            "flirty_dialog_unpublish",
            "flirty_dialog_update",
            "flirty_layout_reset",
            "flirty_layout_set",
            "flirty_loop_create",
            "flirty_loop_delete",
            "flirty_loop_update",
            "flirty_option_create",
            "flirty_option_delete",
            "flirty_option_update",
            "flirty_placeholder_list",
            "flirty_question_create",
            "flirty_question_delete",
            "flirty_question_type_list",
            "flirty_question_update",
            "flirty_session_edit_answer",
            "flirty_session_get",
            "flirty_session_start",
            "flirty_session_start_version",
            "flirty_session_submit_answer",
            "flirty_transition_create",
            "flirty_transition_delete",
            "flirty_transition_update",
            "flirty_trigger_create",
            "flirty_trigger_delete",
            "flirty_trigger_update",
        ];
}
