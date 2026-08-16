using Flirty.Mcp;
using Flirty.Mcp.Tools;
using Flirty.Runtime.Admin;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using static Flirty.Tests.Mcp.FlirtyMcpToolCalls;

namespace Flirty.Tests.Mcp;

/// <summary>
/// Integration tests for <c>AddFlirtyMcp</c> / <c>MapFlirtyMcp</c> (#126): a real <c>McpClient</c> lists and
/// calls the dialog tools over an in-process TestServer against a SQLite in-memory database (Docker-free).
/// Checked are the input schemas, the return shapes (including the wrappers the core has no record for) and
/// the two prerequisites the extension methods document.
/// </summary>
/// <remarks>
/// The tool <i>surface</i> – which tools exist, and what each advertises – lives in
/// <see cref="FlirtyToolSurfaceTests"/> since #127, because it grows with every build-out stage while the
/// wiring checked here does not.
/// </remarks>
public sealed class MapFlirtyMcpTests
{
    /// <summary>A dialog created over MCP is readable over the HTTP surface – it is one database.</summary>
    [Fact]
    public async Task CreateDialog_over_mcp_persists_the_dialog()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync();

        var created = await host.Mcp.CallToolAsync(
            "flirty_dialog_create",
            new Dictionary<string, object?>
            {
                ["key"] = "onboarding",
                ["name"] = "Onboarding",
                ["description"] = "The first dialog",
            });

        Assert.NotEqual(true, created.IsError);
        var summary = Read<DialogSummary>(created);
        Assert.Equal("onboarding", summary.Key);
        Assert.Equal(1, summary.Version);
        Assert.False(summary.IsPublished);

        var http = await host.Client.GetAsync($"/flirty/admin/dialogs/{summary.Id}");
        Assert.Equal(System.Net.HttpStatusCode.OK, http.StatusCode);
    }

    /// <summary>
    /// The dialog metadata stays nested under <c>dialog</c>, where the HTTP DTO flattens it. Deliberate:
    /// it makes visible that <c>flirty_dialog_create</c> returns the same block.
    /// </summary>
    [Fact]
    public async Task GetDialog_returns_the_dialog_block_nested_under_dialog()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync();
        var summary = Read<DialogSummary>(await host.Mcp.CallToolAsync(
            "flirty_dialog_create",
            new Dictionary<string, object?> { ["key"] = "onboarding", ["name"] = "Onboarding" }));

        var result = await host.Mcp.CallToolAsync(
            "flirty_dialog_get", new Dictionary<string, object?> { ["dialogId"] = summary.Id });

        Assert.NotNull(result.StructuredContent);
        Assert.True(result.StructuredContent.Value.TryGetProperty("dialog", out var dialog));
        Assert.Equal("onboarding", dialog.GetProperty("key").GetString());
        var detail = Read<DialogDetail>(result);
        Assert.Empty(detail.Questions);
        Assert.Empty(detail.Layout);
    }

    /// <summary>
    /// The active-session count arrives as an object. It has to: a bare number as
    /// <c>structuredContent</c> is wrapped differently depending on the client's protocol revision. This
    /// query is also the first one to reach any transport – it deliberately has no HTTP endpoint.
    /// </summary>
    [Fact]
    public async Task CountActiveSessions_returns_the_count_as_an_object()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync();
        var summary = Read<DialogSummary>(await host.Mcp.CallToolAsync(
            "flirty_dialog_create",
            new Dictionary<string, object?> { ["key"] = "onboarding", ["name"] = "Onboarding" }));

        var result = await host.Mcp.CallToolAsync(
            "flirty_dialog_count_active_sessions",
            new Dictionary<string, object?> { ["dialogId"] = summary.Id });

        var count = Read<FlirtyActiveSessionCount>(result);
        Assert.Equal(summary.Id, count.DialogId);
        Assert.Equal(0, count.ActiveSessions);
    }

    /// <summary>
    /// A command returning <c>Unit</c> – where HTTP answers 204 – arrives as an acknowledgement object, for
    /// the same reason.
    /// </summary>
    [Fact]
    public async Task DeleteDialog_returns_an_acknowledgement_object()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync();
        var summary = Read<DialogSummary>(await host.Mcp.CallToolAsync(
            "flirty_dialog_create",
            new Dictionary<string, object?> { ["key"] = "onboarding", ["name"] = "Onboarding" }));

        var result = await host.Mcp.CallToolAsync(
            "flirty_dialog_delete", new Dictionary<string, object?> { ["dialogId"] = summary.Id });

        Assert.True(Read<FlirtyAck>(result).Succeeded);
        var gone = await host.Mcp.CallToolAsync(
            "flirty_dialog_get", new Dictionary<string, object?> { ["dialogId"] = summary.Id });
        Assert.Equal(404, FlirtyMcpExceptionParityTests.ReadProblem(gone).Status);
    }

    /// <summary>
    /// The list arrives wrapped as an object too, and carries what was created.
    /// </summary>
    [Fact]
    public async Task ListDialogs_returns_the_created_dialogs_wrapped_in_an_object()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync();
        await host.Mcp.CallToolAsync(
            "flirty_dialog_create",
            new Dictionary<string, object?> { ["key"] = "a", ["name"] = "A" });
        await host.Mcp.CallToolAsync(
            "flirty_dialog_create",
            new Dictionary<string, object?> { ["key"] = "b", ["name"] = "B" });

        var list = Read<FlirtyDialogList>(await host.Mcp.CallToolAsync("flirty_dialog_list"));

        Assert.Equal(["a", "b"], list.Dialogs.Select(dialog => dialog.Key));
    }

    /// <summary>
    /// Publishing and deriving a version walk the whole lifecycle over MCP, and a new version really gets
    /// new element ids.
    /// </summary>
    [Fact]
    public async Task Publish_and_create_version_walk_the_dialog_lifecycle()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync();
        var summary = Read<DialogSummary>(await host.Mcp.CallToolAsync(
            "flirty_dialog_create",
            new Dictionary<string, object?> { ["key"] = "onboarding", ["name"] = "Onboarding" }));

        // Without an entry question, publishing is a conflict.
        var tooEarly = await host.Mcp.CallToolAsync(
            "flirty_dialog_publish", new Dictionary<string, object?> { ["dialogId"] = summary.Id });
        Assert.Equal(409, FlirtyMcpExceptionParityTests.ReadProblem(tooEarly).Status);

        var draft = Read<DialogDetail>(await host.Mcp.CallToolAsync(
            "flirty_dialog_create_version", new Dictionary<string, object?> { ["dialogId"] = summary.Id }));

        Assert.Equal(2, draft.Dialog.Version);
        Assert.False(draft.Dialog.IsPublished);
        Assert.NotEqual(summary.Id, draft.Dialog.Id);
    }

    /// <summary>
    /// Update, unpublish and abandon-sessions round out the smoke surface: every one of the ten tools is
    /// invoked at least once across this class, so a tool that does not even dispatch its command cannot
    /// hide behind its neighbours.
    /// </summary>
    [Fact]
    public async Task Update_unpublish_and_abandon_sessions_round_trip_over_mcp()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync();
        var created = Read<DialogSummary>(await host.Mcp.CallToolAsync(
            "flirty_dialog_create",
            new Dictionary<string, object?> { ["key"] = "onboarding", ["name"] = "Onboarding" }));

        var updated = Read<DialogSummary>(await host.Mcp.CallToolAsync(
            "flirty_dialog_update",
            new Dictionary<string, object?>
            {
                ["dialogId"] = created.Id,
                ["key"] = "onboarding",
                ["name"] = "Onboarding v2",
                ["description"] = "Renamed over MCP",
            }));
        Assert.Equal("Onboarding v2", updated.Name);
        Assert.Equal("Renamed over MCP", updated.Description);

        // Unpublishing an unpublished dialog is idempotent, so it needs no publishable graph here.
        var unpublished = Read<DialogSummary>(await host.Mcp.CallToolAsync(
            "flirty_dialog_unpublish", new Dictionary<string, object?> { ["dialogId"] = created.Id }));
        Assert.False(unpublished.IsPublished);

        var abandoned = Read<AbandonSessionsResult>(await host.Mcp.CallToolAsync(
            "flirty_dialog_abandon_sessions",
            new Dictionary<string, object?> { ["dialogId"] = created.Id }));
        Assert.Equal(created.Id, abandoned.DialogId);
        Assert.Equal(0, abandoned.AbandonedSessions);
    }

    /// <summary>
    /// A <see cref="Guid"/> parameter is exposed as a schema property, while the injected
    /// <c>ISender</c> and the cancellation token are not – a DI-registered type is silently excluded.
    /// </summary>
    [Fact]
    public async Task A_guid_argument_is_exposed_in_the_input_schema()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync();

        var tool = Assert.Single(
            await host.Mcp.ListToolsAsync(), t => t.Name == "flirty_dialog_get");

        var properties = tool.ProtocolTool.InputSchema.GetProperty("properties");
        Assert.True(properties.TryGetProperty("dialogId", out _));
        Assert.False(properties.TryGetProperty("sender", out _));
        Assert.False(properties.TryGetProperty("cancellationToken", out _));
    }

    /// <summary>
    /// An enum parameter arrives as an <c>enum</c> constraint of <b>names</b> rather than as a number – the
    /// MCP surface differs from the HTTP one here (where an enum is an integer) on purpose, and to the
    /// client's benefit: a model can see the admissible values.
    /// </summary>
    /// <remarks>
    /// The names are the C# member names verbatim, i.e. PascalCase: the SDK adds a bare
    /// <c>JsonStringEnumConverter</c> with no naming policy. Reading is case-insensitive, so a camelCase
    /// argument is accepted too – but PascalCase is what the schema advertises, and therefore what a client
    /// will send.
    /// </remarks>
    [Fact]
    public async Task An_enum_argument_is_exposed_with_an_enum_constraint()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync(includeThrowingTools: true);

        var tool = Assert.Single(
            await host.Mcp.ListToolsAsync(), t => t.Name == "flirty_test_throw");

        var kind = tool.ProtocolTool.InputSchema.GetProperty("properties").GetProperty("kind");
        var names = kind.GetProperty("enum").EnumerateArray().Select(v => v.GetString()).ToArray();
        Assert.Contains("DialogNotFound", names);
        Assert.Contains("AnswerValidation", names);
    }

    /// <summary>
    /// <c>FlirtyMcpSurface</c> really scopes the registration: a host that only asks for the runtime surface
    /// gets the five session tools and no configuration tool at all.
    /// </summary>
    /// <remarks>
    /// Until #128 this test asserted the opposite – that <c>tools/list</c> was unavailable with
    /// <c>-32601</c>, because a server with no tools advertises no tools capability. That was the SDK's
    /// semantics showing through an empty surface, not the flag's meaning, and it stopped being observable
    /// the moment the flag registered something. What is worth pinning is the scoping itself, which is why
    /// <see cref="Surface_Admin_registers_no_session_tools"/> now states the other direction: the flag has
    /// two meanings and only one of them was ever tested.
    /// </remarks>
    [Fact]
    public async Task Surface_Runtime_registers_only_the_session_tools()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync(
            configureMcp: options => options.Surface = FlirtyMcpSurface.Runtime);

        var tools = (await host.Mcp.ListToolsAsync()).Select(tool => tool.Name).ToList();

        Assert.Equal(5, tools.Count);
        Assert.All(tools, name => Assert.StartsWith("flirty_session_", name, StringComparison.Ordinal));
    }

    /// <summary>
    /// A server with no tools at all advertises no tools capability, which makes <c>tools/list</c> itself
    /// unavailable (JSON-RPC <c>-32601</c>).
    /// </summary>
    /// <remarks>
    /// That is the SDK's semantics rather than a Flirty decision, and it is pinned here because it is a
    /// documented claim in <c>docs/MCP.md</c> that lost its witness in #128: it used to fall out of
    /// <c>Surface = Runtime</c> back when that flag registered nothing. <c>None</c> is now the only
    /// configuration that observes it.
    /// </remarks>
    [Fact]
    public async Task Surface_None_leaves_the_server_without_a_tools_capability()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync(
            configureMcp: options => options.Surface = FlirtyMcpSurface.None);

        var exception = await Record.ExceptionAsync(async () => await host.Mcp.ListToolsAsync());

        Assert.NotNull(exception);
        Assert.Contains("tools/list", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The other direction of the same flag: a host that only asks for the configuration surface gets no
    /// session tool, so it cannot start a dialog – let alone an unpublished draft.
    /// </summary>
    [Fact]
    public async Task Surface_Admin_registers_no_session_tools()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync(
            configureMcp: options => options.Surface = FlirtyMcpSurface.Admin);

        var tools = (await host.Mcp.ListToolsAsync()).Select(tool => tool.Name).ToList();

        // 27 configuration tools plus the two authoring list tools flirty_question_type_list (#136) and
        // flirty_placeholder_list (#140), which explain the customTypeKey parameter and the {{key}} markers
        // of flirty_question_create/_update.
        Assert.Equal(29, tools.Count);
        Assert.Contains(FlirtyToolNames.QuestionTypeList, tools, StringComparer.Ordinal);
        Assert.Contains(FlirtyToolNames.PlaceholderList, tools, StringComparer.Ordinal);
        Assert.DoesNotContain(tools, name => name.StartsWith("flirty_session_", StringComparison.Ordinal));
    }

    /// <summary>
    /// The returned builder is the SDK's endpoint convention builder, so the recommended
    /// <c>RequireAuthorization()</c> really chains onto it.
    /// </summary>
    [Fact]
    public void MapFlirtyMcp_returns_a_builder_that_accepts_RequireAuthorization()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddAuthorization();
        builder.Services.AddFlirty(options => options.UseSqlite("Data Source=:memory:"));
        builder.Services.AddFlirtyMcp();
        using var app = builder.Build();

        var endpoints = app.MapFlirtyMcp("/mcp").RequireAuthorization();

        Assert.NotNull(endpoints);
    }

    /// <summary>
    /// Mapping without registering the server first fails loudly at startup rather than serving a broken
    /// endpoint – the prerequisite the XML docs name.
    /// </summary>
    [Fact]
    public void MapFlirtyMcp_without_AddFlirtyMcp_throws()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddFlirty(options => options.UseSqlite("Data Source=:memory:"));
        using var app = builder.Build();

        Assert.Throws<InvalidOperationException>(() => app.MapFlirtyMcp("/mcp"));
    }

    /// <summary>
    /// Consecutive tool calls each run in their own request scope. That is what makes the stateless
    /// transport enough on its own – no scope factory of the package's, as the designer needs.
    /// </summary>
    [Fact]
    public async Task Consecutive_tool_calls_each_resolve_a_fresh_dbcontext()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync();

        var first = Read<DialogSummary>(await host.Mcp.CallToolAsync(
            "flirty_dialog_create",
            new Dictionary<string, object?> { ["key"] = "a", ["name"] = "A" }));
        var second = Read<DialogSummary>(await host.Mcp.CallToolAsync(
            "flirty_dialog_create",
            new Dictionary<string, object?> { ["key"] = "b", ["name"] = "B" }));

        // A pinned, reused context would have tracked the first dialog and the second insert would have
        // seen it; both succeeding with distinct ids is the observable effect of two scopes.
        Assert.NotEqual(first.Id, second.Id);
        var list = Read<FlirtyDialogList>(await host.Mcp.CallToolAsync("flirty_dialog_list"));
        Assert.Equal(2, list.Dialogs.Count);
    }
}
