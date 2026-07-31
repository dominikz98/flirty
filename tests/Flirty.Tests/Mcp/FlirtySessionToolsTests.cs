using Flirty.Domain;
using Flirty.Mcp.Tools;
using Flirty.Persistence;
using Flirty.Runtime;
using Flirty.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Flirty.Tests.Mcp;

/// <summary>
/// Integration tests for the runtime tools of <c>Flirty.Mcp</c> (#128): starting, playing, reading and
/// correcting a dialog session, driven by a real <c>McpClient</c> over an in-process TestServer against a
/// SQLite in-memory database (Docker-free).
/// </summary>
/// <remarks>
/// <para>
/// The graphs come from <see cref="TestDialogFactory"/> rather than being authored over the configuration
/// tools, and that is a scope decision, not convenience. Building over MCP <i>and</i> playing over MCP in
/// one test is precisely the round trip that stage 5 (#130) owns as its headline acceptance criterion;
/// duplicating it here would leave that stage with nothing left to prove. So this suite seeds the graph and
/// tests the five runtime tools, and it shares its dialogs with the HTTP runtime tests in
/// <see cref="AspNetCore.MapFlirtyEndpointsTests"/> – which is what lets the two be read against each
/// other, the same trick the graph suite plays on <c>MapFlirtyAdminEndpointsTests</c>.
/// </para>
/// <para>
/// The not-found and validation error shapes are asserted in
/// <see cref="FlirtyMcpExceptionParityTests"/> instead, against the HTTP surface as the reference. Only the
/// two failures that are <i>about</i> the runtime rather than about the mapping are here: an answer to a
/// question that is not open, and the value contract of <c>flirty_session_submit_answer</c>.
/// </para>
/// </remarks>
public sealed class FlirtySessionToolsTests
{
    /// <summary>
    /// The acceptance criterion in one test: a dialog is started, answered, read back and completed
    /// entirely over MCP.
    /// </summary>
    [Fact]
    public async Task A_dialog_is_started_answered_and_read_back_over_mcp()
    {
        var dialog = TestDialogFactory.BuildBranchingDialog(Guid.NewGuid(), out var ids);
        await using var host = await FlirtyMcpTestHost.StartAsync(context => context.Dialogs.Add(dialog));

        var start = await host.StartSessionAsync("branching");
        Assert.False(start.IsResumed);
        Assert.Equal("role", start.CurrentQuestion.Key);
        Assert.Equal(QuestionType.SingleChoice, start.CurrentQuestion.Type);
        Assert.Equal(
            new[] { "dev", "pm" }, start.CurrentQuestion.Options.Select(option => option.Value));

        var submitted = await host.SubmitAnswerAsync(start.SessionId, ids.RoleQuestionId, "\"dev\"");
        Assert.False(submitted.IsCompleted);
        Assert.NotNull(submitted.NextQuestion);
        Assert.Equal("devDetail", submitted.NextQuestion.Key);

        var state = await host.GetSessionAsync(start.SessionId);
        Assert.Equal(SessionStatus.InProgress, state.Status);
        Assert.NotNull(state.CurrentQuestion);
        Assert.Equal("devDetail", state.CurrentQuestion.Key);
        Assert.Equal("role", Assert.Single(state.Answers).QuestionKey);

        var completed = await host.SubmitAnswerAsync(start.SessionId, ids.DevQuestionId, "\"C#\"");
        Assert.True(completed.IsCompleted);
        Assert.Null(completed.NextQuestion);

        var finished = await host.GetSessionAsync(start.SessionId);
        Assert.Equal(SessionStatus.Completed, finished.Status);
        Assert.Null(finished.CurrentQuestion);
        Assert.Equal(2, finished.Answers.Count);
    }

    /// <summary>
    /// Starting the same dialog again for the same user resumes the running session instead of opening a
    /// second one – which is what makes the tool's <c>idempotentHint</c> true.
    /// </summary>
    [Fact]
    public async Task Starting_twice_for_the_same_user_resumes_the_running_session()
    {
        var dialog = TestDialogFactory.BuildBranchingDialog(Guid.NewGuid(), out _);
        await using var host = await FlirtyMcpTestHost.StartAsync(context => context.Dialogs.Add(dialog));

        var first = await host.StartSessionAsync("branching", "user-1");
        var second = await host.StartSessionAsync("branching", "user-1");

        Assert.Equal(first.SessionId, second.SessionId);
        Assert.False(first.IsResumed);
        Assert.True(second.IsResumed);
    }

    /// <summary>
    /// The point of <c>flirty_session_start_version</c>, and its counter-check, in one test: it starts an
    /// <b>unpublished</b> draft that <c>flirty_session_start</c> refuses.
    /// </summary>
    /// <remarks>
    /// Written as one test on purpose. Split, the first half would report "a draft is startable" without
    /// showing that the production barrier still stands – and that barrier is the entire reason this tool
    /// has no HTTP endpoint. The pair is the same shape as the ADR-0007 pair in
    /// <see cref="FlirtyGraphToolsTests"/>.
    /// </remarks>
    [Fact]
    public async Task StartVersion_starts_an_unpublished_draft_that_start_refuses()
    {
        var dialog = TestDialogFactory.BuildBranchingDialog(Guid.NewGuid(), out _);
        dialog.IsPublished = false;
        await using var host = await FlirtyMcpTestHost.StartAsync(context => context.Dialogs.Add(dialog));

        var refused = await host.Mcp.CallToolAsync(
            FlirtyToolNames.SessionStart,
            new Dictionary<string, object?>
            {
                ["dialogKey"] = "branching",
                ["externalUserKey"] = "user-1",
            });

        Assert.True(refused.IsError);
        Assert.Equal(404, FlirtyMcpExceptionParityTests.ReadProblem(refused).Status);

        var started = await host.StartSessionVersionAsync(dialog.Id);

        Assert.Equal("role", started.CurrentQuestion.Key);
    }

    /// <summary>
    /// A test run is a real run, so the sessions it writes carry the <c>mcp-test-</c> marker that tells
    /// them apart from production ones afterwards.
    /// </summary>
    /// <remarks>
    /// Asserted against the stored row rather than a tool result, because no result carries the external
    /// user key – which is also why the marker has to be applied server-side to be worth anything.
    /// </remarks>
    [Fact]
    public async Task StartVersion_marks_the_session_it_writes_as_a_test_run()
    {
        var dialog = TestDialogFactory.BuildBranchingDialog(Guid.NewGuid(), out _);
        dialog.IsPublished = false;
        await using var host = await FlirtyMcpTestHost.StartAsync(context => context.Dialogs.Add(dialog));

        var started = await host.StartSessionVersionAsync(dialog.Id, "agent-7");

        var stored = await LoadSessionAsync(host, started.SessionId);
        Assert.Equal($"{FlirtySessionTools.TestUserKeyPrefix}agent-7", stored.ExternalUserKey);
    }

    /// <summary>
    /// A blank external user key is still rejected – the marker must not stand in front of the engine's
    /// own validation.
    /// </summary>
    /// <remarks>
    /// The trap this guards: prefixing unconditionally would turn <c>""</c> into a non-empty string, so
    /// <c>[Required]</c> on <c>StartDialogVersionCommand.ExternalUserKey</c> would be silently satisfied and
    /// the run would be stored under the bare prefix instead of reporting the 400 it owes the caller. The
    /// <i>first</i> assertion is what goes red on that regression – the call would simply succeed. The
    /// counter-check on the table is kept anyway, because it also holds if a later change makes the call
    /// fail for some unrelated reason after a session was already written.
    /// <c>string.IsNullOrWhiteSpace</c> is the right guard rather than an approximation of one:
    /// <c>RequiredAttribute</c> trims before testing for empty, so the two agree on every input.
    /// </remarks>
    [Fact]
    public async Task StartVersion_with_a_blank_external_user_key_is_still_rejected()
    {
        var dialog = TestDialogFactory.BuildBranchingDialog(Guid.NewGuid(), out _);
        dialog.IsPublished = false;
        await using var host = await FlirtyMcpTestHost.StartAsync(context => context.Dialogs.Add(dialog));

        var result = await host.Mcp.CallToolAsync(
            FlirtyToolNames.SessionStartVersion,
            new Dictionary<string, object?> { ["dialogId"] = dialog.Id, ["externalUserKey"] = "" });

        Assert.True(result.IsError);
        var problem = FlirtyMcpExceptionParityTests.ReadProblem(result);
        Assert.Equal(400, problem.Status);
        Assert.Equal("Invalid request", problem.Title);

        using var scope = host.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FlirtyDbContext>();
        Assert.Empty(await context.DialogSessions.ToListAsync());
    }

    /// <summary>
    /// An answer that fails validation reports the field errors under <c>errors.value</c> – the only branch
    /// of the error mapping that carries structured errors at all.
    /// </summary>
    /// <remarks>
    /// The submitted value is the option's <b>label</b> where its <b>value</b> was wanted. That is the
    /// documented trap of the answer contract rather than an arbitrary bad input: it is the bug the sample
    /// chat UI shipped with (#47), and a <c>SingleChoice</c> is where it fails loudly enough to assert on.
    /// The quiet failure of the same contract is a different one and lives a layer further in – a
    /// <c>Boolean</c> sent as the quoted <c>"true"</c> validates and is stored, but binds as a
    /// <see cref="string"/> in a branching expression, so a condition comparing it to a boolean stops
    /// matching with nothing rejected. That one has no error to assert on here by construction.
    /// </remarks>
    [Fact]
    public async Task SubmitAnswer_with_an_option_label_instead_of_its_value_reports_the_field_errors()
    {
        var dialog = TestDialogFactory.BuildBranchingDialog(Guid.NewGuid(), out var ids);
        await using var host = await FlirtyMcpTestHost.StartAsync(context => context.Dialogs.Add(dialog));
        var start = await host.StartSessionAsync("branching");

        var result = await host.Mcp.CallToolAsync(
            FlirtyToolNames.SessionSubmitAnswer,
            new Dictionary<string, object?>
            {
                ["sessionId"] = start.SessionId,
                ["questionId"] = ids.RoleQuestionId,
                ["value"] = "\"Developer\"",
            });

        Assert.True(result.IsError);
        var problem = FlirtyMcpExceptionParityTests.ReadProblem(result);
        Assert.Equal(400, problem.Status);
        Assert.Equal("Invalid answer", problem.Title);
        Assert.NotNull(problem.Errors);
        Assert.NotEmpty(problem.Errors["value"]);
    }

    /// <summary>Answering a question that is not the open one is a conflict.</summary>
    [Fact]
    public async Task SubmitAnswer_to_a_question_that_is_not_open_reports_a_conflict()
    {
        var dialog = TestDialogFactory.BuildBranchingDialog(Guid.NewGuid(), out var ids);
        await using var host = await FlirtyMcpTestHost.StartAsync(context => context.Dialogs.Add(dialog));
        var start = await host.StartSessionAsync("branching");

        var result = await host.Mcp.CallToolAsync(
            FlirtyToolNames.SessionSubmitAnswer,
            new Dictionary<string, object?>
            {
                ["sessionId"] = start.SessionId,
                ["questionId"] = ids.DevQuestionId,
                ["value"] = "\"C#\"",
            });

        Assert.True(result.IsError);
        var problem = FlirtyMcpExceptionParityTests.ReadProblem(result);
        Assert.Equal(409, problem.Status);
        Assert.Equal("Conflict", problem.Title);
    }

    /// <summary>
    /// Editing an earlier answer switches the branch and reports how many downstream answers that
    /// discarded.
    /// </summary>
    [Fact]
    public async Task EditAnswer_switches_the_branch_and_invalidates_the_downstream_answers()
    {
        var dialog = TestDialogFactory.BuildBranchingDialog(Guid.NewGuid(), out var ids);
        await using var host = await FlirtyMcpTestHost.StartAsync(context => context.Dialogs.Add(dialog));
        var start = await host.StartSessionAsync("branching");
        await host.SubmitAnswerAsync(start.SessionId, ids.RoleQuestionId, "\"dev\"");
        await host.SubmitAnswerAsync(start.SessionId, ids.DevQuestionId, "\"C#\"");

        // The argument is OMITTED rather than passed as null, which is the only place the suite exercises
        // the "every optional parameter carries an explicit = null" convention for this tool class. Sending
        // an explicit null keeps working if that default is dropped; omitting it becomes a binder 400.
        var edited = await host.CallAsync<EditAnswerResult>(
            FlirtyToolNames.SessionEditAnswer,
            new Dictionary<string, object?>
            {
                ["sessionId"] = start.SessionId,
                ["questionId"] = ids.RoleQuestionId,
                ["value"] = "\"pm\"",
            });

        Assert.Equal(1, edited.InvalidatedAnswers);
        Assert.False(edited.IsCompleted);
        Assert.NotNull(edited.NextQuestion);
        Assert.Equal("pmDetail", edited.NextQuestion.Key);

        var state = await host.GetSessionAsync(start.SessionId);
        Assert.Equal("pm", Assert.Single(state.Answers).Value.Trim('"'));
    }

    /// <summary>
    /// Two passes through a loop collect one answer per iteration with ascending
    /// <c>iterationIndex</c> values, and the terminal question outside the loop has none.
    /// </summary>
    [Fact]
    public async Task A_loop_runs_two_iterations_and_reports_the_iteration_index()
    {
        var dialog = TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out var ids);
        await using var host = await FlirtyMcpTestHost.StartAsync(context => context.Dialogs.Add(dialog));
        var start = await host.StartSessionAsync("loop");

        await host.SubmitAnswerAsync(start.SessionId, ids.PositionQuestionId, "\"Backend\"");
        await host.SubmitAnswerAsync(start.SessionId, ids.MoreQuestionId, "\"yes\"");
        await host.SubmitAnswerAsync(start.SessionId, ids.PositionQuestionId, "\"Frontend\"");
        var exit = await host.SubmitAnswerAsync(start.SessionId, ids.MoreQuestionId, "\"no\"");

        Assert.NotNull(exit.NextQuestion);
        Assert.Equal("summary", exit.NextQuestion.Key);

        var state = await host.GetSessionAsync(start.SessionId);
        var positions = state.Answers.Where(answer => answer.QuestionKey == "position").ToList();
        Assert.Equal(new int?[] { 0, 1 }, positions.Select(answer => answer.IterationIndex));
        Assert.Equal(
            new[] { "\"Backend\"", "\"Frontend\"" }, positions.Select(answer => answer.Value));
        // Assert.NotNull matters: LoopInstanceId is Guid?, so Assert.Single alone is also satisfied by
        // two nulls - i.e. by the loop never having been recognized at all.
        Assert.NotNull(Assert.Single(positions.Select(answer => answer.LoopInstanceId).Distinct()));
    }

    /// <summary>
    /// Editing one loop iteration hits exactly that iteration and discards everything answered after it.
    /// </summary>
    /// <remarks>
    /// The engine needs no MCP-side code for this: <c>EditAnswerCommand</c> drops the downstream answers by
    /// sequence, so correcting the first iteration takes the second one with it – three answers here, the
    /// second position and the two <c>more</c> answers that surrounded it.
    /// </remarks>
    [Fact]
    public async Task EditAnswer_of_a_loop_iteration_targets_that_iteration_and_discards_the_rest()
    {
        var dialog = TestDialogFactory.BuildLoopDialog(Guid.NewGuid(), out var ids);
        await using var host = await FlirtyMcpTestHost.StartAsync(context => context.Dialogs.Add(dialog));
        var start = await host.StartSessionAsync("loop");
        await host.SubmitAnswerAsync(start.SessionId, ids.PositionQuestionId, "\"Backend\"");
        await host.SubmitAnswerAsync(start.SessionId, ids.MoreQuestionId, "\"yes\"");
        await host.SubmitAnswerAsync(start.SessionId, ids.PositionQuestionId, "\"Frontend\"");
        await host.SubmitAnswerAsync(start.SessionId, ids.MoreQuestionId, "\"no\"");

        var edited = await host.EditAnswerAsync(
            start.SessionId, ids.PositionQuestionId, "\"Platform\"", iterationIndex: 0);

        Assert.Equal(3, edited.InvalidatedAnswers);

        var state = await host.GetSessionAsync(start.SessionId);
        var remaining = Assert.Single(state.Answers);
        Assert.Equal("position", remaining.QuestionKey);
        Assert.Equal("\"Platform\"", remaining.Value);
        Assert.Equal(0, remaining.IterationIndex);
    }

    /// <summary>Loads a session from the host's own database.</summary>
    private static async Task<DialogSession> LoadSessionAsync(FlirtyMcpTestHost host, Guid sessionId)
    {
        using var scope = host.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FlirtyDbContext>();
        return await context.DialogSessions.SingleAsync(session => session.Id == sessionId);
    }
}
