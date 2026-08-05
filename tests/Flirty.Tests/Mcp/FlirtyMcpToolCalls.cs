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

    /// <summary>Marks a cycle in the graph as a loop.</summary>
    /// <param name="host">The test host.</param>
    /// <param name="dialogId">The dialog the marker belongs to.</param>
    /// <param name="collectionKey">The name the captured answers appear under in an expression.</param>
    /// <param name="entryQuestionId">The first question of the loop body.</param>
    /// <param name="breakingQuestionId">The question whose answer decides whether it runs again.</param>
    /// <returns>The created loop marker.</returns>
    internal static async Task<LoopDetail> CreateLoopAsync(
        this FlirtyMcpTestHost host,
        Guid dialogId,
        string collectionKey,
        Guid entryQuestionId,
        Guid breakingQuestionId)
        => await host.CallAsync<LoopDetail>(
            FlirtyToolNames.LoopCreate,
            new Dictionary<string, object?>
            {
                ["dialogId"] = dialogId,
                ["collectionKey"] = collectionKey,
                ["entryQuestionId"] = entryQuestionId,
                ["breakingQuestionId"] = breakingQuestionId,
            });

    /// <summary>Creates a trigger on a dialog.</summary>
    /// <param name="host">The test host.</param>
    /// <param name="dialogId">The dialog the trigger belongs to.</param>
    /// <param name="scope">When it fires.</param>
    /// <param name="kind">How it is delivered.</param>
    /// <param name="config">The JSON configuration (<c>url</c>/<c>name</c>).</param>
    /// <param name="questionId">The question of an <c>AfterQuestion</c> trigger.</param>
    /// <returns>The created trigger.</returns>
    internal static async Task<TriggerDetail> CreateTriggerAsync(
        this FlirtyMcpTestHost host,
        Guid dialogId,
        TriggerScope scope,
        TriggerKind kind,
        string config,
        Guid? questionId = null)
        => await host.CallAsync<TriggerDetail>(
            FlirtyToolNames.TriggerCreate,
            new Dictionary<string, object?>
            {
                ["dialogId"] = dialogId,
                ["scope"] = scope.ToString(),
                ["questionId"] = questionId,
                ["kind"] = kind.ToString(),
                ["config"] = config,
            });

    /// <summary>Places or moves canvas positions in one batch and asserts the call succeeded.</summary>
    /// <remarks>
    /// Only for the paths that expect success. The batch-validation tests keep calling the tool directly,
    /// because they have to read an <c>isError</c> result rather than a layout.
    /// </remarks>
    /// <param name="host">The test host.</param>
    /// <param name="dialogId">The dialog whose layout is written.</param>
    /// <param name="entries">The entries, built by <see cref="LayoutEntry"/>.</param>
    /// <returns>The complete layout after the write.</returns>
    internal static async Task<FlirtyDialogLayoutView> SetLayoutAsync(
        this FlirtyMcpTestHost host, Guid dialogId, params Dictionary<string, object?>[] entries)
        => await host.CallAsync<FlirtyDialogLayoutView>(
            FlirtyToolNames.LayoutSet,
            new Dictionary<string, object?> { ["dialogId"] = dialogId, ["entries"] = entries });

    /// <summary>
    /// One entry of the layout batch, in the wire shape the generated schema advertises: camelCase members
    /// and the element kind as a name.
    /// </summary>
    /// <param name="elementId">The element being placed.</param>
    /// <param name="x">The horizontal position.</param>
    /// <param name="y">The vertical position.</param>
    /// <returns>The entry as the tool takes it.</returns>
    internal static Dictionary<string, object?> LayoutEntry(Guid elementId, int x, int y)
        => new()
        {
            ["elementKind"] = nameof(LayoutElementKind.Question),
            ["elementId"] = elementId,
            ["x"] = x,
            ["y"] = y,
        };

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

    /// <summary>Publishes a dialog and asserts that it now is.</summary>
    /// <param name="host">The test host.</param>
    /// <param name="dialogId">The dialog version to publish.</param>
    /// <returns>The published dialog.</returns>
    internal static async Task<DialogSummary> PublishAsync(this FlirtyMcpTestHost host, Guid dialogId)
    {
        var published = await host.CallAsync<DialogSummary>(
            FlirtyToolNames.DialogPublish, new Dictionary<string, object?> { ["dialogId"] = dialogId });
        Assert.True(published.IsPublished);
        return published;
    }

    /// <summary>
    /// Derives the next version of a dialog – the only way forward once a version is published.
    /// </summary>
    /// <remarks>
    /// The clone assigns every question a <b>new</b> id (ADR 0005), so a caller that goes on working with
    /// the ids of the source version silently addresses the wrong dialog. The returned
    /// <see cref="DialogDetail"/> is the only place the new ids exist.
    /// </remarks>
    /// <param name="host">The test host.</param>
    /// <param name="dialogId">The version to clone.</param>
    /// <returns>The new draft version with its own question ids.</returns>
    internal static async Task<DialogDetail> CreateVersionAsync(
        this FlirtyMcpTestHost host, Guid dialogId)
        => await host.CallAsync<DialogDetail>(
            FlirtyToolNames.DialogCreateVersion,
            new Dictionary<string, object?> { ["dialogId"] = dialogId });

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

        return (await host.PublishAsync(dialog.Id), question);
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

/// <summary>
/// Read model of the <c>flirty_layout_set</c> result. The production wrapper
/// <c>Flirty.Mcp.FlirtyDialogLayout</c> is <c>internal</c> and visible here, but deserializing into a
/// test-local record keeps the assertion independent of its member names – and the wrapper's own shape is
/// already pinned by the output-schema test.
/// </summary>
/// <param name="Entries">The complete layout after the write, not only the rows that were set.</param>
internal sealed record FlirtyDialogLayoutView(IReadOnlyList<DialogLayoutDetail> Entries);
