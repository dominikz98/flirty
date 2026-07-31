using System.Net.Http.Json;
using System.Text.Json;
using Flirty.AspNetCore.Dtos;
using Flirty.AspNetCore.Dtos.Admin;
using Flirty.Domain;
using Flirty.Mcp;
using Flirty.Mcp.Tools;
using Flirty.Persistence;
using Flirty.Tests.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace Flirty.Tests.Mcp;

/// <summary>
/// Error-mapping parity between the HTTP surface and the MCP surface (#126, extended in #127 and #128): the same
/// failure must carry the same status, title and detail on both, since <c>FlirtyMcpExceptionFilter</c>
/// mirrors <c>FlirtyExceptionEndpointFilter</c>. Both surfaces run on <b>one</b> host over <b>one</b> seeded
/// SQLite in-memory database, so the comparison is literal rather than two hosts sharing a connection
/// string.
/// </summary>
/// <remarks>
/// <para>
/// The acceptance criterion hides two logically independent halves in one sentence, and it is worth naming
/// them, because this stage can prove them to different depths:
/// </para>
/// <para>
/// <b>H1 – same command, same exception.</b> Transport-independent: both surfaces send the same
/// <c>ICommand</c> over the same <c>ISender</c> to the same handler, so nothing about MCP can change which
/// exception arises. <b>H2 – same exception, same status/title/detail.</b> This is the only half the new
/// filter can get wrong, and it is what "mirrors" means.
/// </para>
/// <para>
/// <b>Every test below proves both halves</b>, and saying so is the news of #128. Stage 1 could only reach
/// three of the six engine exceptions through real tools; the other three – dialog-not-found,
/// session-not-found and answer-validation – all need the runtime operations, so on the MCP side they went
/// through <see cref="FlirtyThrowingTestTools"/> and proved H2 alone, with a standing note that H1 would
/// follow. Stage 2 (#127) deepened Tier 1 to six exception paths but could not shrink that set. The runtime
/// tools close it: the tier split is gone from this file, and each of the six exceptions now arises from
/// the real engine on <i>both</i> sides of its comparison.
/// </para>
/// <para>
/// Two of those paths carry more than the count. A <c>DialogPublishedException</c> from a real graph
/// command (#127) is what holds the filter's clause order honest – that subtype must be caught before its
/// base <see cref="InvalidOperationException"/>, and the compiler enforces the order via CS0160 but not the
/// correctness of it. And <c>AnswerValidationException</c> is the only branch carrying structured field
/// errors, so it is the only one where parity means comparing a dictionary rather than three strings.
/// </para>
/// <para>
/// <see cref="FlirtyThrowingTestTools"/> stays all the same. Four of its kinds – <c>McpProtocol</c>,
/// <c>Cancellation</c>, <c>ArgumentNull</c> and <c>Unexpected</c> – are unreachable through any real tool
/// by design, and they are what <see cref="FlirtyMcpExceptionFilterTests"/> drives. The six-row
/// <see cref="EngineExceptions"/> theory below keeps using it too: it pins the documented mapping table as
/// a table, one row per exception, where the parity tests pin it one call path at a time.
/// </para>
/// </remarks>
public sealed class FlirtyMcpExceptionParityTests
{
    // ---- The three configuration exceptions ----

    /// <summary>An unknown dialog id yields the same 404 over both surfaces.</summary>
    [Fact]
    public async Task GetDialog_of_an_unknown_dialog_maps_the_same_over_http_and_mcp()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync();
        var unknown = Guid.NewGuid();

        var http = await host.Client.GetAsync($"/flirty/admin/dialogs/{unknown}");
        var mcp = await host.Mcp.CallToolAsync(
            "flirty_dialog_get", new Dictionary<string, object?> { ["dialogId"] = unknown });

        await AssertSameProblemAsync(http, mcp, 404, "Not found");
    }

    /// <summary>
    /// An empty key is rejected by the pipeline validation on both surfaces, with the same message.
    /// </summary>
    /// <remarks>
    /// Deliberately an <b>empty</b> key rather than an omitted one: over MCP a missing required argument
    /// never reaches the pipeline – the SDK's argument marshaller rejects it first, which is the MCP-only
    /// binder branch and a different test. An empty string does reach it, because <c>[Required]</c> rejects
    /// empty strings too.
    /// </remarks>
    [Fact]
    public async Task CreateDialog_with_an_empty_key_maps_the_same_over_http_and_mcp()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync();

        var http = await host.Client.PostAsJsonAsync(
            "/flirty/admin/dialogs", new CreateDialogRequest(string.Empty, "Onboarding", null));
        var mcp = await host.Mcp.CallToolAsync(
            "flirty_dialog_create",
            new Dictionary<string, object?> { ["key"] = string.Empty, ["name"] = "Onboarding" });

        await AssertSameProblemAsync(http, mcp, 400, "Invalid request");
    }

    /// <summary>A duplicate key is a 409 conflict on both surfaces, with the same message.</summary>
    [Fact]
    public async Task CreateDialog_with_a_duplicate_key_maps_the_same_over_http_and_mcp()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync();
        await host.Mcp.CallToolAsync(
            "flirty_dialog_create",
            new Dictionary<string, object?> { ["key"] = "dup", ["name"] = "First" });

        var http = await host.Client.PostAsJsonAsync(
            "/flirty/admin/dialogs", new CreateDialogRequest("dup", "Second", null));
        var mcp = await host.Mcp.CallToolAsync(
            "flirty_dialog_create",
            new Dictionary<string, object?> { ["key"] = "dup", ["name"] = "Third" });

        await AssertSameProblemAsync(http, mcp, 409, "Conflict");
    }

    /// <summary>
    /// A nested lookup that misses – an option under a question that does not exist – yields the same 404
    /// over both surfaces.
    /// </summary>
    /// <remarks>
    /// Not covered by the unknown-dialog test above: the nested lookup produces a different <c>detail</c>
    /// string, and <c>detail</c> is precisely the field a mapping table would get wrong while status and
    /// title still matched.
    /// </remarks>
    [Fact]
    public async Task CreateOption_under_an_unknown_question_maps_the_same_over_http_and_mcp()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync();
        var dialog = await host.CreateDialogAsync("nested-404");
        var unknownQuestion = Guid.NewGuid();

        var http = await host.Client.PostAsJsonAsync(
            $"/flirty/admin/dialogs/{dialog.Id}/questions/{unknownQuestion}/options",
            new CreateAnswerOptionRequest("dev", "Developer", "dev", 0));
        var mcp = await host.Mcp.CallToolAsync(
            FlirtyToolNames.OptionCreate,
            new Dictionary<string, object?>
            {
                ["dialogId"] = dialog.Id,
                ["questionId"] = unknownQuestion,
                ["key"] = "dev",
                ["label"] = "Developer",
                ["value"] = "dev",
                ["order"] = 0,
            });

        await AssertSameProblemAsync(http, mcp, 404, "Not found");
    }

    /// <summary>
    /// A graph change on a published dialog is the same 409 on both surfaces – the first end-to-end witness
    /// for <c>DialogPublishedException</c>, whose position before its base type the filter depends on.
    /// </summary>
    [Fact]
    public async Task CreateQuestion_on_a_published_dialog_maps_the_same_over_http_and_mcp()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync();
        var (dialog, _) = await host.CreatePublishedDialogAsync("published-parity");

        var http = await host.Client.PostAsJsonAsync(
            $"/flirty/admin/dialogs/{dialog.Id}/questions",
            new CreateQuestionRequest("second", "And?", QuestionType.FreeText, 1, false, null));
        var mcp = await host.Mcp.CallToolAsync(
            FlirtyToolNames.QuestionCreate,
            new Dictionary<string, object?>
            {
                ["dialogId"] = dialog.Id,
                ["key"] = "third",
                ["text"] = "And then?",
                ["type"] = nameof(QuestionType.FreeText),
                ["order"] = 2,
                ["isRequired"] = false,
            });

        await AssertSameProblemAsync(http, mcp, 409, "Conflict");
    }

    /// <summary>
    /// A trigger whose fields contradict each other is the same 400 on both surfaces.
    /// </summary>
    /// <remarks>
    /// A different branch from the empty-key test: that one is an attribute failure
    /// (<c>[Required]</c>), this one is the cross-field <c>IValidatableObject</c> of the trigger commands,
    /// which builds its <c>ValidationResult</c>s itself and is aggregated into <c>detail</c> separately.
    /// </remarks>
    [Fact]
    public async Task CreateTrigger_with_an_inconsistent_config_maps_the_same_over_http_and_mcp()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync();
        var dialog = await host.CreateDialogAsync("trigger-parity");

        var http = await host.Client.PostAsJsonAsync(
            $"/flirty/admin/dialogs/{dialog.Id}/triggers",
            new CreateTriggerRequest(
                TriggerScope.OnDialogCompleted, null, TriggerKind.Webhook, "{\"name\":\"no-url\"}", null));
        var mcp = await host.Mcp.CallToolAsync(
            FlirtyToolNames.TriggerCreate,
            new Dictionary<string, object?>
            {
                ["dialogId"] = dialog.Id,
                ["scope"] = nameof(TriggerScope.OnDialogCompleted),
                ["kind"] = nameof(TriggerKind.Webhook),
                ["config"] = "{\"name\":\"no-url\"}",
            });

        await AssertSameProblemAsync(http, mcp, 400, "Invalid request");
    }

    // ---- The three runtime exceptions, real on both sides since #128 ----

    /// <summary>Starting on an unknown dialog key yields the same 404 over both surfaces.</summary>
    [Fact]
    public async Task StartSession_for_an_unknown_dialog_key_maps_the_same_over_http_and_mcp()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync();

        var http = await host.Client.PostAsJsonAsync(
            "/flirty/sessions", new StartSessionRequest("nope", "user-1"));
        var mcp = await host.Mcp.CallToolAsync(
            FlirtyToolNames.SessionStart,
            new Dictionary<string, object?> { ["dialogKey"] = "nope", ["externalUserKey"] = "user-1" });

        await AssertSameProblemAsync(http, mcp, 404, "Dialog not found");
    }

    /// <summary>Reading an unknown session yields the same 404 over both surfaces.</summary>
    [Fact]
    public async Task ResumeSession_of_an_unknown_session_maps_the_same_over_http_and_mcp()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync();
        var unknown = Guid.NewGuid();

        var http = await host.Client.GetAsync($"/flirty/sessions/{unknown}");
        var mcp = await host.Mcp.CallToolAsync(
            FlirtyToolNames.SessionGet, new Dictionary<string, object?> { ["sessionId"] = unknown });

        await AssertSameProblemAsync(http, mcp, 404, "Session not found");
    }

    /// <summary>
    /// An answer the validator rejects yields the same 400 with the same individual errors over both
    /// surfaces – including the <c>"value"</c> key the HTTP validation problem uses.
    /// </summary>
    /// <remarks>
    /// The two surfaces answer their own session here rather than sharing one, because a submitted answer
    /// advances the session and the first call would leave the second nothing to reject. Same dialog, same
    /// database, same question – which is all the comparison needs.
    /// </remarks>
    [Fact]
    public async Task SubmitAnswer_with_an_invalid_value_maps_the_same_over_http_and_mcp()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync(SeedNumberDialog);

        var start = await host.Client.PostAsJsonAsync(
            "/flirty/sessions", new StartSessionRequest("numbers", "http-user"));
        var session = await start.Content.ReadFromJsonAsync<StartSessionResponse>();
        Assert.NotNull(session);

        var http = await host.Client.PostAsJsonAsync(
            $"/flirty/sessions/{session.SessionId}/answers",
            new SubmitAnswerRequest(session.CurrentQuestion.Id, "not-a-number"));

        var expected = await http.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        Assert.NotNull(expected);

        var mcpSession = await host.StartSessionAsync("numbers", "mcp-user");
        var mcp = await host.Mcp.CallToolAsync(
            FlirtyToolNames.SessionSubmitAnswer,
            new Dictionary<string, object?>
            {
                ["sessionId"] = mcpSession.SessionId,
                ["questionId"] = mcpSession.CurrentQuestion.Id,
                ["value"] = "not-a-number",
            });

        var problem = ReadProblem(mcp);
        Assert.Equal(400, problem.Status);
        Assert.Equal("Invalid answer", problem.Title);
        Assert.Equal(expected.Detail, problem.Detail);
        Assert.NotNull(problem.Errors);
        Assert.Equal(expected.Errors["value"], problem.Errors["value"]);
    }

    /// <summary>
    /// The whole mapping table in one place: every engine exception carries the documented status and
    /// title over MCP.
    /// </summary>
    [Theory]
    [MemberData(nameof(EngineExceptions))]
    public async Task Engine_exceptions_map_to_the_documented_status_and_title(
        string kind, int status, string title)
    {
        await using var host = await FlirtyMcpTestHost.StartAsync(includeThrowingTools: true);

        var result = await host.Mcp.CallToolAsync(
            "flirty_test_throw", new Dictionary<string, object?> { ["kind"] = kind });

        var problem = ReadProblem(result);
        Assert.True(result.IsError);
        Assert.Equal(status, problem.Status);
        Assert.Equal(title, problem.Title);
    }

    /// <summary>The six engine exceptions with the status and title of the HTTP filter.</summary>
    public static TheoryData<string, int, string> EngineExceptions =>
        new()
        {
            { "DialogNotFound", 404, "Dialog not found" },
            { "SessionNotFound", 404, "Session not found" },
            { "ConfigurationNotFound", 404, "Not found" },
            { "AnswerValidation", 400, "Invalid answer" },
            { "Validation", 400, "Invalid request" },
            { "InvalidOperation", 409, "Conflict" },
        };

    // ---- Helpers ----

    /// <summary>
    /// Compares the two surfaces field by field – never as whole objects: the HTTP payload carries a
    /// <c>type</c> member pointing into HTTP response semantics, which the MCP payload deliberately omits.
    /// </summary>
    private static async Task AssertSameProblemAsync(
        HttpResponseMessage http, CallToolResult mcp,
        int expectedStatus, string expectedTitle)
    {
        var expected = await http.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(expected);
        Assert.Equal(expectedStatus, (int)http.StatusCode);
        Assert.Equal(expectedTitle, expected.Title);

        var problem = ReadProblem(mcp);
        Assert.True(mcp.IsError);
        Assert.Equal((int)http.StatusCode, problem.Status);
        Assert.Equal(expected.Title, problem.Title);
        Assert.Equal(expected.Detail, problem.Detail);
    }

    /// <summary>
    /// Reads the structured error payload. Deserialized into <see cref="FlirtyProblem"/> rather than poked
    /// at by property name, so a renamed member breaks the test.
    /// </summary>
    internal static FlirtyProblem ReadProblem(CallToolResult result)
    {
        Assert.NotNull(result.StructuredContent);
        var problem = result.StructuredContent.Value.Deserialize<FlirtyProblem>(
            McpJsonUtilities.DefaultOptions);
        Assert.NotNull(problem);
        return problem;
    }

    /// <summary>A published dialog with a single, required number question.</summary>
    private static void SeedNumberDialog(FlirtyDbContext context)
    {
        var dialogId = Guid.NewGuid();
        var questionId = Guid.NewGuid();

        context.Dialogs.Add(new Dialog
        {
            Id = dialogId,
            Key = "numbers",
            Name = "Numbers",
            Version = 1,
            IsPublished = true,
            StartQuestionId = questionId,
            CreatedAt = TestDialogFactory.SampleTime,
            UpdatedAt = TestDialogFactory.SampleTime,
            Questions =
            {
                new Question
                {
                    Id = questionId,
                    DialogId = dialogId,
                    Key = "amount",
                    Text = "How many?",
                    Type = QuestionType.Number,
                    Order = 0,
                    IsRequired = true,
                },
            },
        });
    }
}
