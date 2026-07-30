using Flirty.Designer.Services;
using Flirty.Domain;
using Flirty.Runtime;
using Flirty.Runtime.Admin;
using Microsoft.Extensions.DependencyInjection;

namespace Flirty.Tests.Designer;

/// <summary>
/// Tests for the test runner's <see cref="FlirtyRuntimeGateway"/> (#43): playing a dialog through over
/// <see cref="IFlirtyEngine"/> against the active connection profile, and the error mapping onto
/// displayable messages.
/// </summary>
/// <remarks>
/// The core check is <see cref="A_draft_with_a_loop_can_be_played_through_end_to_end"/> – the issue's
/// acceptance criterion in test form: create a dialog incl. a loop over the admin commands and play it
/// through with two iterations <b>without publishing</b>.
/// </remarks>
public sealed class FlirtyRuntimeGatewayTests
{
    /// <summary>
    /// The end-to-end pass: two iterations of the loop, the exit, the completion – and the collected
    /// answers carry the expected iteration indexes. The dialog stays a <b>draft</b> throughout;
    /// without <c>StartDialogVersionCommand</c> that would not be possible.
    /// </summary>
    [Fact]
    public async Task A_draft_with_a_loop_can_be_played_through_end_to_end()
    {
        await RunAsync(async (admin, runtime, _) =>
        {
            var graph = await DesignerTestHost.ArrangeLoopDialogAsync(admin);

            var started = await runtime.ExecuteAsync(
                (engine, token) => engine.StartDialogVersionAsync(graph.DialogId, "designer-test-1", token));
            Assert.True(started.Success, started.Error);
            Assert.False(started.Value!.IsResumed);
            Assert.Equal("position", started.Value.CurrentQuestion.Key);

            var sessionId = started.Value.SessionId;

            // Iteration 1: record a position, answer "another one?" with yes -> back jump.
            await SubmitAsync(runtime, sessionId, graph.PositionQuestionId, "\"Developer\"", "more");
            await SubmitAsync(runtime, sessionId, graph.MoreQuestionId, "\"yes\"", "position");

            // Iteration 2: a second position, then exit.
            await SubmitAsync(runtime, sessionId, graph.PositionQuestionId, "\"Architect\"", "more");
            await SubmitAsync(runtime, sessionId, graph.MoreQuestionId, "\"no\"", "summary");

            var finished = await runtime.ExecuteAsync((engine, token) =>
                engine.SubmitAnswerAsync(sessionId, graph.SummaryQuestionId, "\"done\"", token));
            Assert.True(finished.Success, finished.Error);
            Assert.True(finished.Value!.IsCompleted);

            var state = await runtime.ExecuteAsync(
                (engine, token) => engine.ResumeDialogAsync(sessionId, token));
            Assert.True(state.Success, state.Error);
            Assert.Equal(SessionStatus.Completed, state.Value!.Status);
            Assert.Null(state.Value.CurrentQuestion);

            // Both position answers lie in the same loop instance but in iteration 0/1 – exactly what
            // the runner's history shows as "Iteration 1/2".
            var positions = state.Value.Answers
                .Where(answer => answer.QuestionKey == "position")
                .OrderBy(answer => answer.Sequence)
                .ToList();

            Assert.Equal(["\"Developer\"", "\"Architect\""], positions.Select(answer => answer.Value));
            Assert.Equal([0, 1], positions.Select(answer => answer.IterationIndex));
            Assert.Single(positions.Select(answer => answer.LoopInstanceId).Distinct());

            // The answer outside the loop deliberately carries no loop assignment.
            var summary = Assert.Single(state.Value.Answers, answer => answer.QuestionKey == "summary");
            Assert.Null(summary.IterationIndex);
            Assert.Null(summary.LoopInstanceId);
        });
    }

    /// <summary>
    /// Editing an iteration's answer hits exactly the given iteration and discards the downstream
    /// answers – the basis of the runner's success message.
    /// </summary>
    [Fact]
    public async Task Editing_hits_the_given_iteration()
    {
        await RunAsync(async (admin, runtime, _) =>
        {
            var graph = await DesignerTestHost.ArrangeLoopDialogAsync(admin);

            var started = await runtime.ExecuteAsync(
                (engine, token) => engine.StartDialogVersionAsync(graph.DialogId, "designer-test-1", token));
            Assert.True(started.Success, started.Error);
            var sessionId = started.Value!.SessionId;

            await SubmitAsync(runtime, sessionId, graph.PositionQuestionId, "\"Developer\"", "more");
            await SubmitAsync(runtime, sessionId, graph.MoreQuestionId, "\"yes\"", "position");
            await SubmitAsync(runtime, sessionId, graph.PositionQuestionId, "\"Architect\"", "more");

            var edited = await runtime.ExecuteAsync((engine, token) => engine.EditAnswerAsync(
                sessionId, graph.PositionQuestionId, "\"Tester\"", iterationIndex: 1, token));

            Assert.True(edited.Success, edited.Error);

            var state = await runtime.ExecuteAsync(
                (engine, token) => engine.ResumeDialogAsync(sessionId, token));
            Assert.True(state.Success, state.Error);

            var positions = state.Value!.Answers
                .Where(answer => answer.QuestionKey == "position")
                .OrderBy(answer => answer.IterationIndex)
                .Select(answer => answer.Value)
                .ToList();

            Assert.Equal(["\"Developer\"", "\"Tester\""], positions);
        });
    }

    // ---- Error mapping -----------------------------------------------------------------------

    /// <summary>
    /// A rejected answer must not tear the Blazor circuit. What is reported are the engine's
    /// individual violations – without the raw question GUID the exception's <c>Message</c> carries
    /// along.
    /// </summary>
    [Fact]
    public async Task Reports_an_invalid_answer_without_technical_identifiers()
    {
        await RunAsync(async (admin, runtime, _) =>
        {
            var graph = await DesignerTestHost.ArrangeLoopDialogAsync(admin);

            var started = await runtime.ExecuteAsync(
                (engine, token) => engine.StartDialogVersionAsync(graph.DialogId, "designer-test-1", token));
            Assert.True(started.Success, started.Error);
            var sessionId = started.Value!.SessionId;

            await SubmitAsync(runtime, sessionId, graph.PositionQuestionId, "\"Developer\"", "more");

            // "maybe" is not a configured option of the question "more".
            var rejected = await runtime.ExecuteAsync((engine, token) =>
                engine.SubmitAnswerAsync(sessionId, graph.MoreQuestionId, "\"maybe\"", token));

            Assert.False(rejected.Success);
            Assert.StartsWith("Answer invalid:", rejected.Error, StringComparison.Ordinal);
            Assert.Contains("maybe", rejected.Error, StringComparison.Ordinal);
            Assert.DoesNotContain(graph.MoreQuestionId.ToString(), rejected.Error, StringComparison.Ordinal);
        });
    }

    /// <summary>An unknown session is shown as a message, not thrown as an exception.</summary>
    [Fact]
    public async Task Reports_an_unknown_session()
    {
        await RunAsync(async (_, runtime, _) =>
        {
            var result = await runtime.ExecuteAsync(
                (engine, token) => engine.ResumeDialogAsync(Guid.NewGuid(), token));

            Assert.False(result.Success);
            Assert.Contains("session", result.Error, StringComparison.Ordinal);
        });
    }

    /// <summary>An unknown dialog version is shown as a message.</summary>
    [Fact]
    public async Task Reports_an_unknown_dialog_version()
    {
        await RunAsync(async (_, runtime, _) =>
        {
            var result = await runtime.ExecuteAsync((engine, token) =>
                engine.StartDialogVersionAsync(Guid.NewGuid(), "designer-test-1", token));

            Assert.False(result.Success);
            Assert.Contains("dialog", result.Error, StringComparison.Ordinal);
        });
    }

    /// <summary>Without an active connection profile the context factory reports understandably.</summary>
    [Fact]
    public async Task Reports_a_missing_connection_profile()
    {
        await DesignerTestHost.RunWithTempDbAsync(async (services, _) =>
        {
            // Bewusst KEIN Activate.
            var result = await services.GetRequiredService<FlirtyRuntimeGateway>().ExecuteAsync(
                (engine, token) => engine.StartDialogVersionAsync(Guid.NewGuid(), "designer-test-1", token));

            Assert.False(result.Success);
            Assert.Contains("Connections", result.Error, StringComparison.Ordinal);
        });
    }

    // ---- Testaufbau --------------------------------------------------------------------------

    /// <summary>
    /// Runs the test body with an activated profile and both gateways.
    /// </summary>
    /// <param name="test">The test body (admin gateway, runtime gateway, trigger log).</param>
    private static Task RunAsync(
        Func<FlirtyAdminGateway, FlirtyRuntimeGateway, DesignerTriggerLog, Task> test)
        => DesignerTestHost.RunWithTempDbAsync((services, profile) =>
        {
            services.GetRequiredService<ActiveConnectionProfile>().Activate(profile);

            return test(
                services.GetRequiredService<FlirtyAdminGateway>(),
                services.GetRequiredService<FlirtyRuntimeGateway>(),
                services.GetRequiredService<DesignerTriggerLog>());
        });

    /// <summary>Submits an answer and checks which question is open afterwards.</summary>
    /// <param name="runtime">Das Runtime-Gateway.</param>
    /// <param name="sessionId">Die laufende Session.</param>
    /// <param name="questionId">Die zu beantwortende Frage.</param>
    /// <param name="value">Der rohe JSON-Antwortwert.</param>
    /// <param name="expectedNextKey">The key of the expected follow-up question.</param>
    private static async Task SubmitAsync(
        FlirtyRuntimeGateway runtime, Guid sessionId, Guid questionId, string value, string expectedNextKey)
    {
        var result = await runtime.ExecuteAsync(
            (engine, token) => engine.SubmitAnswerAsync(sessionId, questionId, value, token));

        Assert.True(result.Success, result.Error);
        Assert.False(result.Value!.IsCompleted);
        Assert.Equal(expectedNextKey, result.Value.NextQuestion!.Key);
    }

}
