using System.Text.Json;
using Flirty.Domain;
using Flirty.Mcp.Tools;
using Flirty.Runtime;
using Flirty.Runtime.Admin;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace Flirty.Tests.Mcp;

/// <summary>
/// The shared tool-call helpers of the <c>Flirty.Mcp</c> integration tests: the structured-content reader
/// and the graph builders a test needs before it can assert anything. The MCP counterpart of the private
/// <c>CreateDialogAsync</c>/<c>CreateQuestionAsync</c>/… helpers of
/// <see cref="AspNetCore.MapFlirtyAdminEndpointsTests"/>, and deliberately the same shape, so the two
/// suites read against each other.
/// </summary>
/// <remarks>
/// <para>
/// Extension methods on <see cref="FlirtyMcpTestHost"/> rather than members of it: that type is host
/// lifecycle (TestServer, keep-alive connection, MCP client, log recorder), and hanging domain builders on
/// it would make it two things. As extensions they can also be shared without either test class inheriting
/// anything.
/// </para>
/// <para>
/// Every builder asserts success, so a failing precondition fails on the line that set it up rather than
/// three calls later on a confusing assertion.
/// </para>
/// </remarks>
internal static class FlirtyMcpToolCalls
{
    /// <summary>
    /// Reads the structured content of a successful tool call. Deserialized into the real result type rather
    /// than poked at by property name, so a renamed member breaks the test.
    /// </summary>
    /// <typeparam name="T">The expected result type.</typeparam>
    /// <param name="result">The tool result.</param>
    /// <returns>The deserialized result.</returns>
    internal static T Read<T>(CallToolResult result)
    {
        Assert.NotEqual(true, result.IsError);
        Assert.NotNull(result.StructuredContent);
        var value = result.StructuredContent.Value.Deserialize<T>(McpJsonUtilities.DefaultOptions);
        Assert.NotNull(value);
        return value;
    }

    /// <summary>Calls a tool and reads its structured content as <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">The expected result type.</typeparam>
    /// <param name="host">The test host.</param>
    /// <param name="tool">The wire name of the tool.</param>
    /// <param name="arguments">The tool arguments.</param>
    /// <returns>The deserialized result.</returns>
    internal static async Task<T> CallAsync<T>(
        this FlirtyMcpTestHost host, string tool, Dictionary<string, object?>? arguments = null)
        => Read<T>(await host.Mcp.CallToolAsync(tool, arguments));

    /// <summary>Creates an unpublished dialog whose key and name are <paramref name="key"/>.</summary>
    /// <param name="host">The test host.</param>
    /// <param name="key">The business key of the dialog.</param>
    /// <returns>The created dialog.</returns>
    internal static async Task<DialogSummary> CreateDialogAsync(this FlirtyMcpTestHost host, string key)
        => await host.CallAsync<DialogSummary>(
            FlirtyToolNames.DialogCreate,
            new Dictionary<string, object?> { ["key"] = key, ["name"] = key });

    /// <summary>Creates a question in a dialog.</summary>
    /// <param name="host">The test host.</param>
    /// <param name="dialogId">The dialog the question belongs to.</param>
    /// <param name="key">The business key of the question.</param>
    /// <param name="type">The answer type.</param>
    /// <param name="order">The sort index within the dialog.</param>
    /// <returns>The created question.</returns>
    internal static async Task<QuestionDetail> CreateQuestionAsync(
        this FlirtyMcpTestHost host, Guid dialogId, string key, QuestionType type, int order)
        => await host.CallAsync<QuestionDetail>(
            FlirtyToolNames.QuestionCreate,
            new Dictionary<string, object?>
            {
                ["dialogId"] = dialogId,
                ["key"] = key,
                ["text"] = $"{key}?",
                ["type"] = type.ToString(),
                ["order"] = order,
                ["isRequired"] = false,
            });

    /// <summary>Creates an answer option on a question.</summary>
    /// <param name="host">The test host.</param>
    /// <param name="dialogId">The dialog the question belongs to.</param>
    /// <param name="questionId">The question the option belongs to.</param>
    /// <param name="key">The business key of the option.</param>
    /// <param name="label">The displayed label.</param>
    /// <param name="value">The stored value.</param>
    /// <param name="order">The sort index within the question.</param>
    /// <returns>The created answer option.</returns>
    internal static async Task<AnswerOptionDetail> CreateOptionAsync(
        this FlirtyMcpTestHost host,
        Guid dialogId,
        Guid questionId,
        string key,
        string label,
        string value,
        int order)
        => await host.CallAsync<AnswerOptionDetail>(
            FlirtyToolNames.OptionCreate,
            new Dictionary<string, object?>
            {
                ["dialogId"] = dialogId,
                ["questionId"] = questionId,
                ["key"] = key,
                ["label"] = label,
                ["value"] = value,
                ["order"] = order,
            });

    /// <summary>Creates a transition between two questions.</summary>
    /// <param name="host">The test host.</param>
    /// <param name="dialogId">The dialog the transition belongs to.</param>
    /// <param name="fromQuestionId">The source question.</param>
    /// <param name="targetQuestionId">The target question.</param>
    /// <param name="isDefault">Whether it is the fallback of the source question.</param>
    /// <param name="expression">The optional condition.</param>
    /// <param name="priority">The evaluation order within the source question.</param>
    /// <returns>The created transition.</returns>
    internal static async Task<TransitionDetail> CreateTransitionAsync(
        this FlirtyMcpTestHost host,
        Guid dialogId,
        Guid fromQuestionId,
        Guid targetQuestionId,
        bool isDefault,
        string? expression = null,
        int priority = 0)
        => await host.CallAsync<TransitionDetail>(
            FlirtyToolNames.TransitionCreate,
            new Dictionary<string, object?>
            {
                ["dialogId"] = dialogId,
                ["fromQuestionId"] = fromQuestionId,
                ["targetQuestionId"] = targetQuestionId,
                ["priority"] = priority,
                ["isDefault"] = isDefault,
                ["expression"] = expression,
            });

    /// <summary>
    /// Sets the entry question of a dialog – the one metadata field that a publish needs and that
    /// <c>flirty_dialog_update</c> refuses to change once the dialog is published.
    /// </summary>
    /// <param name="host">The test host.</param>
    /// <param name="dialog">The dialog whose entry question is set.</param>
    /// <param name="startQuestionId">The entry question.</param>
    /// <returns>The updated dialog.</returns>
    internal static async Task<DialogSummary> SetStartQuestionAsync(
        this FlirtyMcpTestHost host, DialogSummary dialog, Guid startQuestionId)
        => await host.CallAsync<DialogSummary>(
            FlirtyToolNames.DialogUpdate,
            new Dictionary<string, object?>
            {
                ["dialogId"] = dialog.Id,
                ["key"] = dialog.Key,
                ["name"] = dialog.Name,
                ["description"] = dialog.Description,
                ["startQuestionId"] = startQuestionId,
            });

    /// <summary>
    /// Creates a published dialog with exactly one (terminal) question – the starting point of every test
    /// about the publish lock.
    /// </summary>
    /// <param name="host">The test host.</param>
    /// <param name="key">The business key of the dialog.</param>
    /// <returns>The published dialog and its single question.</returns>
    internal static async Task<(DialogSummary Dialog, QuestionDetail Question)> CreatePublishedDialogAsync(
        this FlirtyMcpTestHost host, string key)
    {
        var dialog = await host.CreateDialogAsync(key);
        var question = await host.CreateQuestionAsync(dialog.Id, "start", QuestionType.FreeText, 0);
        await host.SetStartQuestionAsync(dialog, question.Id);

        var published = await host.CallAsync<DialogSummary>(
            FlirtyToolNames.DialogPublish,
            new Dictionary<string, object?> { ["dialogId"] = dialog.Id });
        Assert.True(published.IsPublished);

        return (published, question);
    }

    // ---- Runtime (#128) ----------------------------------------------------------------------------

    /// <summary>Starts the published dialog with the given key over <c>flirty_session_start</c>.</summary>
    /// <param name="host">The test host.</param>
    /// <param name="dialogKey">The business key of the dialog.</param>
    /// <param name="externalUserKey">The caller's identifier for the user.</param>
    /// <returns>The started (or resumed) session.</returns>
    internal static async Task<StartDialogResult> StartSessionAsync(
        this FlirtyMcpTestHost host, string dialogKey, string externalUserKey = "user-1")
        => await host.CallAsync<StartDialogResult>(
            FlirtyToolNames.SessionStart,
            new Dictionary<string, object?>
            {
                ["dialogKey"] = dialogKey,
                ["externalUserKey"] = externalUserKey,
            });

    /// <summary>
    /// Starts one dialog version by id over <c>flirty_session_start_version</c>, published or not.
    /// </summary>
    /// <param name="host">The test host.</param>
    /// <param name="dialogId">The dialog version to start.</param>
    /// <param name="externalUserKey">The caller's identifier for the user, before the test prefix.</param>
    /// <returns>The started (or resumed) session.</returns>
    internal static async Task<StartDialogResult> StartSessionVersionAsync(
        this FlirtyMcpTestHost host, Guid dialogId, string externalUserKey = "user-1")
        => await host.CallAsync<StartDialogResult>(
            FlirtyToolNames.SessionStartVersion,
            new Dictionary<string, object?>
            {
                ["dialogId"] = dialogId,
                ["externalUserKey"] = externalUserKey,
            });

    /// <summary>Reads the state of a session over <c>flirty_session_get</c>.</summary>
    /// <param name="host">The test host.</param>
    /// <param name="sessionId">The session to read.</param>
    /// <returns>The session state.</returns>
    internal static async Task<ResumeDialogResult> GetSessionAsync(
        this FlirtyMcpTestHost host, Guid sessionId)
        => await host.CallAsync<ResumeDialogResult>(
            FlirtyToolNames.SessionGet,
            new Dictionary<string, object?> { ["sessionId"] = sessionId });

    /// <summary>Answers the open question of a session over <c>flirty_session_submit_answer</c>.</summary>
    /// <param name="host">The test host.</param>
    /// <param name="sessionId">The session to answer in.</param>
    /// <param name="questionId">The question being answered.</param>
    /// <param name="value">The answer as raw JSON text.</param>
    /// <returns>The result of the submission.</returns>
    internal static async Task<SubmitAnswerResult> SubmitAnswerAsync(
        this FlirtyMcpTestHost host, Guid sessionId, Guid questionId, string value)
        => await host.CallAsync<SubmitAnswerResult>(
            FlirtyToolNames.SessionSubmitAnswer,
            new Dictionary<string, object?>
            {
                ["sessionId"] = sessionId,
                ["questionId"] = questionId,
                ["value"] = value,
            });

    /// <summary>Corrects an answer over <c>flirty_session_edit_answer</c>.</summary>
    /// <param name="host">The test host.</param>
    /// <param name="sessionId">The session whose answer is corrected.</param>
    /// <param name="questionId">The question whose answer is corrected.</param>
    /// <param name="value">The new answer as raw JSON text.</param>
    /// <param name="iterationIndex">The loop iteration to correct, or <see langword="null"/>.</param>
    /// <returns>The result of the edit.</returns>
    internal static async Task<EditAnswerResult> EditAnswerAsync(
        this FlirtyMcpTestHost host,
        Guid sessionId,
        Guid questionId,
        string value,
        int? iterationIndex = null)
        => await host.CallAsync<EditAnswerResult>(
            FlirtyToolNames.SessionEditAnswer,
            new Dictionary<string, object?>
            {
                ["sessionId"] = sessionId,
                ["questionId"] = questionId,
                ["value"] = value,
                ["iterationIndex"] = iterationIndex,
            });
}
