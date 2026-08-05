using Flirty.Domain;
using Flirty.Mcp.Tools;
using Flirty.Runtime.Admin;

namespace Flirty.Tests.Mcp;

/// <summary>
/// The acceptance criterion of EPIC 13 in test form (#130): everything an operator does in the Blazor
/// designer, done by an MCP client alone – authoring a dialog, publishing it, deriving the next version and
/// playing that version through, edits included. One <c>McpClient</c>, no designer, and not one HTTP call.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a file and a single test of its own.</b> The per-area suites answer "does this tool work";
/// <see cref="FlirtyGraphToolsTests"/> and <see cref="FlirtySessionToolsTests"/> cover every element type
/// and every session operation between them, and each of their tests is deliberately narrow. What none of
/// them can show is that the tools compose into the *workflow* the EPIC promises – a graph authored over
/// MCP, locked by publishing, evolved into a draft whose element ids are all different, and then run. That
/// claim is one sequence, so it is one test; split into seven it would assert seven preconditions it had
/// just built itself.
/// </para>
/// <para>
/// <b>The counter-check in the middle is the load-bearing part.</b> After publishing, a graph change is a
/// 409 while <c>flirty_layout_set</c> still succeeds – ADR 0005 and ADR 0007 in two calls, on a real graph,
/// at the moment in the workflow where a client actually meets them.
/// <see cref="FlirtyGraphToolsTests.Layout_is_editable_on_a_published_dialog_while_a_graph_change_reports_conflict"/>
/// keeps making the same claim on a minimal dialog, and the overlap is wanted: that one fails with a
/// pointer at the pair, this one fails with a pointer at the workflow step that broke.
/// </para>
/// <para>
/// <b>The trap this test is built around</b> is step 3. <c>CreateDialogVersionCommand</c> gives every cloned
/// question a <b>new</b> id (ADR 0005), so a client that carries the ids of the published version into the
/// draft addresses elements that are not in the dialog it is running – and gets a 404 several calls later,
/// nowhere near the mistake. The ids for the whole runtime half therefore come from the clone, matched by
/// <c>Key</c>, and the assertion that they are genuinely different is part of the test rather than a comment.
/// </para>
/// <para>
/// Every assertion reads a quantity the <b>server</b> produced – a status, a question key, an
/// <c>iterationIndex</c>, an <c>invalidatedAnswers</c> count, an <c>isCompleted</c> flag. That is the house
/// rule for this surface: a tool call that is silently discarded looks exactly like one that did nothing,
/// so nothing here may rest on a call having "probably" taken effect.
/// </para>
/// <para>
/// The one trigger is deliberately <c>InProcess</c>. A <c>Webhook</c> trigger would really post to its
/// configured url (see <c>docs/MCP.md § A test run is a real run</c>), and a test that needs a listening
/// endpoint to stay green would be a worse test of the same thing;
/// <see cref="FlirtyGraphToolsTests"/> covers the webhook shape as configuration.
/// </para>
/// </remarks>
public sealed class FlirtyMcpRoundTripTests
{
    /// <summary>
    /// The whole workflow over MCP: author, arrange, publish, hit the publish lock, arrange anyway, derive a
    /// version, start the draft, run both branches with two loop iterations, correct one iteration, resume
    /// and finish.
    /// </summary>
    [Fact]
    public async Task An_mcp_client_authors_publishes_versions_and_plays_a_dialog_through_on_its_own()
    {
        await using var host = await FlirtyMcpTestHost.StartAsync();

        // ---- 1. Author the graph, over MCP only -----------------------------------------------------

        var dialog = await host.CreateDialogAsync("round-trip");
        Assert.Equal(1, dialog.Version);

        var role = await host.CreateQuestionAsync(dialog.Id, "role", QuestionType.SingleChoice, 0);
        await host.CreateOptionAsync(dialog.Id, role.Id, "dev", "Developer", "dev", 0);
        await host.CreateOptionAsync(dialog.Id, role.Id, "ops", "Operations", "ops", 1);
        var detail = await host.CreateQuestionAsync(dialog.Id, "detail", QuestionType.FreeText, 1);
        var more = await host.CreateQuestionAsync(dialog.Id, "more", QuestionType.Boolean, 2);
        var summary = await host.CreateQuestionAsync(dialog.Id, "summary", QuestionType.FreeText, 3);

        // Two branching questions, each with a conditional edge and a default fallback. The condition on
        // "role" compares the option's VALUE, not its label - the distinction that cost the sample chat UI
        // a bug in #47 and is the one a client gets wrong from a schema alone.
        await host.CreateTransitionAsync(
            dialog.Id, role.Id, detail.Id, isDefault: false, expression: "role == \"dev\"");
        await host.CreateTransitionAsync(dialog.Id, role.Id, summary.Id, isDefault: true, priority: 1);
        await host.CreateTransitionAsync(dialog.Id, detail.Id, more.Id, isDefault: true);
        await host.CreateTransitionAsync(
            dialog.Id, more.Id, detail.Id, isDefault: false, expression: "more == true");
        await host.CreateTransitionAsync(dialog.Id, more.Id, summary.Id, isDefault: true, priority: 1);

        // The marker does not create the loop - the cycle above is what does. It only tells the runtime to
        // collect the answers of each pass instead of overwriting them.
        await host.CreateLoopAsync(dialog.Id, "details", detail.Id, more.Id);
        await host.CreateTriggerAsync(
            dialog.Id, TriggerScope.OnDialogStarted, TriggerKind.InProcess, "{\"name\":\"started\"}");
        await host.SetLayoutAsync(
            dialog.Id,
            FlirtyMcpToolCalls.LayoutEntry(role.Id, 0, 0),
            FlirtyMcpToolCalls.LayoutEntry(detail.Id, 0, 120),
            FlirtyMcpToolCalls.LayoutEntry(more.Id, 0, 240),
            FlirtyMcpToolCalls.LayoutEntry(summary.Id, 0, 360));

        await host.SetStartQuestionAsync(dialog, role.Id);
        await host.PublishAsync(dialog.Id);

        // ---- 2. The counter-check: the graph is locked, the canvas is not ---------------------------

        var refused = await host.Mcp.CallToolAsync(
            FlirtyToolNames.QuestionCreate,
            new Dictionary<string, object?>
            {
                ["dialogId"] = dialog.Id,
                ["key"] = "afterthought",
                ["text"] = "Anything else?",
                ["type"] = nameof(QuestionType.FreeText),
                ["order"] = 4,
                ["isRequired"] = false,
            });
        Assert.Equal(409, FlirtyMcpExceptionParityTests.ReadProblem(refused).Status);

        var rearranged = await host.SetLayoutAsync(
            dialog.Id, FlirtyMcpToolCalls.LayoutEntry(role.Id, 60, 20));
        Assert.Equal(60, Assert.Single(rearranged.Entries, row => row.ElementId == role.Id).X);

        // ---- 3. Derive the next version - and re-read every id from the clone -----------------------

        var draft = await host.CreateVersionAsync(dialog.Id);
        Assert.Equal(2, draft.Dialog.Version);
        Assert.False(draft.Dialog.IsPublished);

        var draftRole = QuestionOf(draft, "role");
        var draftDetail = QuestionOf(draft, "detail");
        var draftMore = QuestionOf(draft, "more");
        var draftSummary = QuestionOf(draft, "summary");

        // The clone is a different graph, not a second pointer at the same one. Asserted rather than
        // trusted, because everything below would otherwise address the published version by accident.
        Assert.NotEqual(role.Id, draftRole.Id);
        Assert.Equal(draftRole.Id, draft.Dialog.StartQuestionId);
        Assert.Equal(2, draftRole.Options.Count);
        Assert.Equal(5, draft.Transitions.Count);
        Assert.Equal(draftDetail.Id, Assert.Single(draft.Loops).EntryQuestionId);
        Assert.Contains("started", Assert.Single(draft.Triggers).Config);

        // The arrangement survives the clone with the element references rewritten - the layout table and
        // the version derivation interlock, which is only visible where both happen.
        Assert.Equal(4, draft.Layout.Count);
        Assert.Equal(60, Assert.Single(draft.Layout, row => row.ElementId == draftRole.Id).X);

        // Every transition of the clone points at cloned questions; not one reference stayed behind.
        var draftQuestionIds = draft.Questions.Select(question => question.Id).ToHashSet();
        Assert.All(draft.Transitions, transition =>
        {
            Assert.Contains(transition.FromQuestionId, draftQuestionIds);
            Assert.Contains(transition.TargetQuestionId, draftQuestionIds);
        });

        // ---- 4. Start the draft - unpublished, which is the only reason this tool exists ------------

        var session = await host.StartSessionVersionAsync(draft.Dialog.Id, "round-trip-user");
        Assert.False(session.IsResumed);
        Assert.Equal("role", session.CurrentQuestion.Key);
        Assert.Equal(2, session.CurrentQuestion.Options.Count);

        // ---- 5. Both branches of "more", two loop iterations ---------------------------------------

        var answered = await host.SubmitAnswerAsync(session.SessionId, draftRole.Id, "\"dev\"");
        Assert.Equal("detail", answered.NextQuestion?.Key);

        answered = await host.SubmitAnswerAsync(session.SessionId, draftDetail.Id, "\"Backend\"");
        Assert.Equal("more", answered.NextQuestion?.Key);

        // true takes the conditional edge back into the loop body.
        answered = await host.SubmitAnswerAsync(session.SessionId, draftMore.Id, "true");
        Assert.Equal("detail", answered.NextQuestion?.Key);

        answered = await host.SubmitAnswerAsync(session.SessionId, draftDetail.Id, "\"Frontend\"");
        Assert.Equal("more", answered.NextQuestion?.Key);

        // false falls through to the default edge and leaves the loop.
        answered = await host.SubmitAnswerAsync(session.SessionId, draftMore.Id, "false");
        Assert.Equal("summary", answered.NextQuestion?.Key);

        answered = await host.SubmitAnswerAsync(session.SessionId, draftSummary.Id, "\"Done\"");
        Assert.True(answered.IsCompleted);
        Assert.Null(answered.NextQuestion);

        // Both passes survived with their own iteration index - that is what the marker bought.
        var state = await host.GetSessionAsync(session.SessionId);
        Assert.Equal(SessionStatus.Completed, state.Status);
        var details = state.Answers.Where(answer => answer.QuestionKey == "detail").ToList();
        Assert.Equal(new int?[] { 0, 1 }, details.Select(answer => answer.IterationIndex));
        Assert.Equal(new[] { "\"Backend\"", "\"Frontend\"" }, details.Select(answer => answer.Value));

        // ---- 6. Correct the first iteration; everything after it is discarded ----------------------

        var edited = await host.EditAnswerAsync(
            session.SessionId, draftDetail.Id, "\"Platform\"", iterationIndex: 0);

        // Four answers hung downstream of that one: more=true, detail=Frontend, more=false, summary=Done.
        Assert.Equal(4, edited.InvalidatedAnswers);
        Assert.False(edited.IsCompleted);
        Assert.Equal("more", edited.NextQuestion?.Key);

        state = await host.GetSessionAsync(session.SessionId);
        Assert.Equal(SessionStatus.InProgress, state.Status);
        var corrected = Assert.Single(state.Answers, answer => answer.QuestionKey == "detail");
        Assert.Equal("\"Platform\"", corrected.Value);
        Assert.Equal(0, corrected.IterationIndex);

        // ---- 7. Resume where the edit left it, and finish -------------------------------------------

        var resumed = await host.GetSessionAsync(session.SessionId);
        Assert.Equal("more", resumed.CurrentQuestion?.Key);

        answered = await host.SubmitAnswerAsync(session.SessionId, draftMore.Id, "false");
        Assert.Equal("summary", answered.NextQuestion?.Key);
        answered = await host.SubmitAnswerAsync(session.SessionId, draftSummary.Id, "\"Shipped\"");
        Assert.True(answered.IsCompleted);

        // ---- 8. The other branch of the branching question -----------------------------------------

        // "ops" fails the condition, so the default edge skips the loop entirely. Editing the very first
        // answer is what makes both branches of one question observable in one session.
        var switched = await host.EditAnswerAsync(session.SessionId, draftRole.Id, "\"ops\"");
        Assert.Equal(3, switched.InvalidatedAnswers);
        Assert.Equal("summary", switched.NextQuestion?.Key);

        answered = await host.SubmitAnswerAsync(session.SessionId, draftSummary.Id, "\"Skipped\"");
        Assert.True(answered.IsCompleted);

        var final = await host.GetSessionAsync(session.SessionId);
        Assert.Equal(SessionStatus.Completed, final.Status);
        Assert.Equal(
            new[] { "role", "summary" }, final.Answers.Select(answer => answer.QuestionKey));
        Assert.DoesNotContain(final.Answers, answer => answer.QuestionKey == "detail");

        // The published version was never touched by any of this: it still has its own ids and stands.
        var published = await host.CallAsync<DialogDetail>(
            FlirtyToolNames.DialogGet, new Dictionary<string, object?> { ["dialogId"] = dialog.Id });
        Assert.True(published.Dialog.IsPublished);
        Assert.Equal(1, published.Dialog.Version);
        Assert.Equal(role.Id, published.Dialog.StartQuestionId);
    }

    /// <summary>
    /// The cloned counterpart of a question, found by its business key – the only reference that survives
    /// <c>CreateDialogVersionCommand</c> unchanged.
    /// </summary>
    /// <param name="dialog">The cloned dialog.</param>
    /// <param name="key">The business key of the question.</param>
    /// <returns>The question of the clone.</returns>
    private static QuestionDetail QuestionOf(DialogDetail dialog, string key)
        => Assert.Single(dialog.Questions, question => question.Key == key);
}
