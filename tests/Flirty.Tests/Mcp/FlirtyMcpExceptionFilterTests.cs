using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

namespace Flirty.Tests.Mcp;

/// <summary>
/// The parts of <c>FlirtyMcpExceptionFilter</c> that have no HTTP counterpart (#126): the branch order at
/// runtime, the payload shape, the two MCP-only tail branches and the exceptions whose control flow the SDK
/// owns. The parity against the HTTP surface lives in <see cref="FlirtyMcpExceptionParityTests"/>.
/// </summary>
/// <remarks>
/// Driven through a real <c>McpClient</c> rather than by calling the filter delegate directly. That is
/// deliberate: the riskiest assumption in the whole design is that the SDK composes a call-tool filter
/// inside its own try/catch and therefore hands it the original exception at all. A unit test of the mapping
/// table would stay green if that ever changed, while the package silently reverted to the SDK's generic
/// <c>"An error occurred invoking 'x'."</c>.
/// </remarks>
public sealed class FlirtyMcpExceptionFilterTests
{
    /// <summary>
    /// The order of the catch branches is observable in exactly one place: an answer validation error must
    /// be reported as "Invalid answer" and not as "Invalid request" of its base type. Every other ordering
    /// among the six is either unobservable (the three not-found types are unrelated) or lands on the same
    /// status anyway, and a wrong order is a compile error (CS0160) rather than a test failure – so this one
    /// assertion is the complete runtime proof.
    /// </summary>
    [Fact]
    public async Task AnswerValidationException_is_mapped_before_ValidationException()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync(includeThrowingTools: true);

        var result = await ThrowAsync(host, "AnswerValidation");

        var problem = FlirtyMcpExceptionParityTests.ReadProblem(result);
        Assert.Equal("Invalid answer", problem.Title);
        Assert.Equal(400, problem.Status);
    }

    /// <summary>The individual answer errors arrive under the same key the HTTP surface uses.</summary>
    [Fact]
    public async Task AnswerValidationException_reports_the_individual_errors_under_the_value_key()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync(includeThrowingTools: true);

        var result = await host.Mcp.CallToolAsync(
            "flirty_test_throw",
            new Dictionary<string, object?>
            {
                ["kind"] = "AnswerValidation",
                ["errors"] = new[] { "Too small.", "Not a number." },
            });

        var problem = FlirtyMcpExceptionParityTests.ReadProblem(result);
        Assert.NotNull(problem.Errors);
        Assert.Equal(["Too small.", "Not a number."], problem.Errors["value"]);
    }

    /// <summary>
    /// A graph change on a published dialog is an <c>InvalidOperationException</c> subtype and therefore
    /// lands on 409 without a branch of its own – the publish lock keeps the HTTP semantics over MCP.
    /// </summary>
    /// <remarks>
    /// Since #127 the same exception is reachable through a real tool, and
    /// <c>FlirtyMcpExceptionParityTests.CreateQuestion_on_a_published_dialog_maps_the_same_over_http_and_mcp</c>
    /// is that witness. This test is kept anyway, and deliberately: it isolates the <i>clause order</i>
    /// claim from a four-call graph setup, so when it fails it says "the catch order broke", where a red
    /// parity test could equally mean the guard, the publish flow or the filter.
    /// </remarks>
    [Fact]
    public async Task DialogPublishedException_is_mapped_to_409_like_its_base_type()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync(includeThrowingTools: true);

        var result = await ThrowAsync(host, "DialogPublished");

        var problem = FlirtyMcpExceptionParityTests.ReadProblem(result);
        Assert.Equal(409, problem.Status);
        Assert.Equal("Conflict", problem.Title);
        Assert.Contains("is published in version 1", problem.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// The engine's message reaches the client twice: as prose in the text block the model reads, and as
    /// <c>detail</c> in the structured payload a host branches on.
    /// </summary>
    [Fact]
    public async Task A_mapped_error_carries_the_message_in_both_content_and_structured_content()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync(includeThrowingTools: true);

        var result = await host.Mcp.CallToolAsync(
            "flirty_test_throw",
            new Dictionary<string, object?> { ["kind"] = "InvalidOperation", ["key"] = "Key 'dup' is taken." });

        var problem = FlirtyMcpExceptionParityTests.ReadProblem(result);
        Assert.Equal("Key 'dup' is taken.", problem.Detail);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        Assert.Equal("Conflict: Key 'dup' is taken.", text.Text);
    }

    /// <summary>
    /// The <c>type</c> member of the HTTP <c>ProblemDetails</c> is deliberately not carried across: it
    /// points into HTTP response semantics, and over MCP there is no HTTP response.
    /// </summary>
    [Fact]
    public async Task A_mapped_error_omits_the_problem_details_type_member()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync(includeThrowingTools: true);

        var result = await ThrowAsync(host, "InvalidOperation");

        Assert.NotNull(result.StructuredContent);
        Assert.False(result.StructuredContent.Value.TryGetProperty("type", out _));
        Assert.True(result.StructuredContent.Value.TryGetProperty("status", out _));
    }

    /// <summary>
    /// An unbindable argument value is a 400 – over HTTP the route constraint would have rejected it at
    /// routing, so this branch has no HTTP counterpart. It doubles as the confirmation of which exception
    /// the SDK's argument marshaller actually raises.
    /// </summary>
    [Fact]
    public async Task An_unbindable_guid_argument_is_mapped_to_400()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync();

        var result = await host.Mcp.CallToolAsync(
            "flirty_dialog_get", new Dictionary<string, object?> { ["dialogId"] = "not-a-guid" });

        var problem = FlirtyMcpExceptionParityTests.ReadProblem(result);
        Assert.Equal(400, problem.Status);
        Assert.Equal("Invalid request", problem.Title);
        Assert.Contains("flirty_dialog_get", problem.Detail, StringComparison.Ordinal);
    }

    /// <summary>A missing required argument never reaches the pipeline validation; it is a 400 too.</summary>
    [Fact]
    public async Task A_missing_required_argument_is_mapped_to_400()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync();

        var result = await host.Mcp.CallToolAsync(
            "flirty_dialog_create", new Dictionary<string, object?> { ["name"] = "Without a key" });

        var problem = FlirtyMcpExceptionParityTests.ReadProblem(result);
        Assert.Equal(400, problem.Status);
        Assert.Equal("Invalid request", problem.Title);
    }

    /// <summary>
    /// Anything unforeseen becomes a 500 with a generic detail – the exception's own message must not leak,
    /// exactly as ASP.NET Core treats an unhandled exception.
    /// </summary>
    [Fact]
    public async Task An_unexpected_exception_is_mapped_to_500_with_a_generic_detail()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync(includeThrowingTools: true);

        var result = await ThrowAsync(host, "Unexpected");

        var problem = FlirtyMcpExceptionParityTests.ReadProblem(result);
        Assert.Equal(500, problem.Status);
        Assert.Equal("Internal server error", problem.Title);
        Assert.DoesNotContain("secret", problem.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("flirty_test_throw", problem.Detail, StringComparison.Ordinal);
    }

    /// <summary>The swallowed message is not lost: it goes into the server-side log instead.</summary>
    [Fact]
    public async Task An_unexpected_exception_is_logged_on_the_server()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync(includeThrowingTools: true);

        await ThrowAsync(host, "Unexpected");

        var entry = Assert.Single(
            host.Logs.Entries, e => e.Category == "Flirty.Mcp.FlirtyMcpExceptionFilter");
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.IsType<NotSupportedException>(entry.Exception);
        Assert.Contains("flirty_test_throw", entry.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// An <c>McpException</c> is rethrown, not mapped: the SDK already preserves its message, so it is the
    /// documented way for a host's own tools out of Flirty's mapping. Recognized by the SDK's own wording.
    /// </summary>
    [Fact]
    public async Task An_McpException_is_left_to_the_sdk()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync(includeThrowingTools: true);

        var result = await host.Mcp.CallToolAsync(
            "flirty_test_throw",
            new Dictionary<string, object?> { ["kind"] = "McpProtocol", ["key"] = "Protocol trouble." });

        Assert.True(result.IsError);
        Assert.Null(result.StructuredContent);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        Assert.Equal("An error occurred invoking 'flirty_test_throw': Protocol trouble.", text.Text);
    }

    /// <summary>
    /// An <c>OperationCanceledException</c> whose token is <b>not</b> the cancelled request token is a
    /// genuine bug and falls through to the 500 branch.
    /// </summary>
    /// <remarks>
    /// This pins the second half of the rethrow guard, which is character-for-character the SDK's own
    /// predicate: it demands <i>both</i> the exception type and a cancelled token. Without that half, a
    /// misuse of cancellation inside a handler would be reported as an orderly cancellation and silently
    /// swallowed. The real cancellation path – a client disconnect, where the request token <i>is</i>
    /// cancelled – rethrows, and by its nature produces no tool result to assert on: the response stream is
    /// gone. So this is the observable half; the rethrow path itself is covered by
    /// <see cref="An_McpException_is_left_to_the_sdk"/>.
    /// </remarks>
    [Fact]
    public async Task An_uncancelled_OperationCanceledException_is_mapped_to_500()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync(includeThrowingTools: true);

        var result = await ThrowAsync(host, "Cancellation");

        var problem = FlirtyMcpExceptionParityTests.ReadProblem(result);
        Assert.Equal(500, problem.Status);
        Assert.Equal("Internal server error", problem.Title);
    }

    /// <summary>
    /// The binder branch is discriminated by <c>ParamName</c>, not by the exception type alone: an
    /// <c>ArgumentNullException</c> from a handler is a bug and stays a 500, as it is over HTTP.
    /// </summary>
    [Fact]
    public async Task An_ArgumentNullException_from_a_handler_is_mapped_to_500_not_400()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync(includeThrowingTools: true);

        var result = await ThrowAsync(host, "ArgumentNull");

        var problem = FlirtyMcpExceptionParityTests.ReadProblem(result);
        Assert.Equal(500, problem.Status);
        Assert.Equal("Internal server error", problem.Title);
    }

    private static async Task<CallToolResult> ThrowAsync(FlirtyMcpTestHost host, string kind)
        => await host.Mcp.CallToolAsync(
            "flirty_test_throw", new Dictionary<string, object?> { ["kind"] = kind });
}
