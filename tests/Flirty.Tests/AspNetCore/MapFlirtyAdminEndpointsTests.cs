using System.Net;
using System.Net.Http.Json;
using Flirty.AspNetCore.Dtos;
using Flirty.AspNetCore.Dtos.Admin;
using Flirty.Domain;
using Microsoft.AspNetCore.Mvc;

namespace Flirty.Tests.AspNetCore;

/// <summary>
/// Integration tests for <c>MapFlirtyAdminEndpoints</c> (#36): drive the admin CRUD endpoints over an
/// in-process TestServer with real HTTP calls against a SQLite in-memory database (Docker-free).
/// Checked are the CRUD happy paths per entity (dialog/question/option/transition/loop/trigger), the
/// publish workflow, the error mapping (404/400/409), the delete cleanup of orphaned transitions, loop
/// markers and triggers as well as the end-to-end proof that a dialog built purely over the API can
/// then be started over the runtime.
/// </summary>
public sealed class MapFlirtyAdminEndpointsTests
{
    // ---- Dialog CRUD ----

    /// <summary>Creating a dialog returns 201 with a Location header and the initial metadata.</summary>
    [Fact]
    public async Task CreateDialog_returns_201_with_the_location_and_the_metadata()
    {
        await using var host = await FlirtyTestHost.StartAsync();

        var response = await host.Client.PostAsJsonAsync(
            "/flirty/admin/dialogs", new CreateDialogRequest("onboarding", "Onboarding", "Beschreibung"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DialogResponse>();
        Assert.NotNull(body);
        Assert.NotEqual(Guid.Empty, body.Id);
        Assert.Equal("onboarding", body.Key);
        Assert.Equal(1, body.Version);
        Assert.False(body.IsPublished);
        Assert.Null(body.StartQuestionId);
        Assert.Contains($"/flirty/admin/dialogs/{body.Id}", response.Headers.Location?.ToString());
    }

    /// <summary>A missing required value (Key) is mapped to 400 by the pipeline validation.</summary>
    [Fact]
    public async Task CreateDialog_without_a_key_returns_400()
    {
        await using var host = await FlirtyTestHost.StartAsync();

        var response = await host.Client.PostAsJsonAsync(
            "/flirty/admin/dialogs", new { name = "Ohne Key" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>A second dialog with the same key is mapped to 409.</summary>
    [Fact]
    public async Task CreateDialog_with_a_duplicate_key_returns_409()
    {
        await using var host = await FlirtyTestHost.StartAsync();
        await CreateDialogAsync(host, "dup");

        var response = await host.Client.PostAsJsonAsync(
            "/flirty/admin/dialogs", new CreateDialogRequest("dup", "Andere", null));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    /// <summary>The list contains the dialogs created before.</summary>
    [Fact]
    public async Task ListDialogs_returns_the_created_dialogs()
    {
        await using var host = await FlirtyTestHost.StartAsync();
        await CreateDialogAsync(host, "a");
        await CreateDialogAsync(host, "b");

        var list = await host.Client.GetFromJsonAsync<List<DialogResponse>>("/flirty/admin/dialogs");

        Assert.NotNull(list);
        Assert.Equal(2, list.Count);
        Assert.Contains(list, dialog => dialog.Key == "a");
        Assert.Contains(list, dialog => dialog.Key == "b");
    }

    /// <summary>Changing a dialog takes over the new metadata.</summary>
    [Fact]
    public async Task UpdateDialog_changes_the_metadata()
    {
        await using var host = await FlirtyTestHost.StartAsync();
        var dialog = await CreateDialogAsync(host, "edit");

        var response = await host.Client.PutAsJsonAsync(
            $"/flirty/admin/dialogs/{dialog.Id}",
            new UpdateDialogRequest("edit", "Neuer Name", "Neu", null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DialogResponse>();
        Assert.NotNull(body);
        Assert.Equal("Neuer Name", body.Name);
        Assert.Equal("Neu", body.Description);
    }

    /// <summary>Changing an unknown dialog is mapped to 404.</summary>
    [Fact]
    public async Task UpdateDialog_of_an_unknown_dialog_returns_404()
    {
        await using var host = await FlirtyTestHost.StartAsync();

        var response = await host.Client.PutAsJsonAsync(
            $"/flirty/admin/dialogs/{Guid.NewGuid()}",
            new UpdateDialogRequest("x", "X", null, null));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>Deleting returns 204; a subsequent read returns 404.</summary>
    [Fact]
    public async Task DeleteDialog_returns_204_and_then_404()
    {
        await using var host = await FlirtyTestHost.StartAsync();
        var dialog = await CreateDialogAsync(host, "weg");

        var delete = await host.Client.DeleteAsync($"/flirty/admin/dialogs/{dialog.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var get = await host.Client.GetAsync($"/flirty/admin/dialogs/{dialog.Id}");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }

    // ---- Question/option CRUD + graph ----

    /// <summary>Reading a dialog returns its graph with questions, options and transitions.</summary>
    [Fact]
    public async Task GetDialog_returns_the_graph_with_questions_and_transitions()
    {
        await using var host = await FlirtyTestHost.StartAsync();
        var dialog = await CreateDialogAsync(host, "graph");
        var role = await CreateQuestionAsync(host, dialog.Id, "role", QuestionType.SingleChoice, 0);
        await CreateOptionAsync(host, dialog.Id, role.Id, "dev", "Developer", "dev", 0);
        var detail = await CreateQuestionAsync(host, dialog.Id, "detail", QuestionType.FreeText, 1);
        await CreateTransitionAsync(host, dialog.Id, role.Id, detail.Id, isDefault: true);

        var body = await host.Client.GetFromJsonAsync<DialogDetailResponse>(
            $"/flirty/admin/dialogs/{dialog.Id}");

        Assert.NotNull(body);
        Assert.Equal(2, body.Questions.Count);
        var roleQuestion = Assert.Single(body.Questions, question => question.Key == "role");
        var option = Assert.Single(roleQuestion.Options);
        Assert.Equal("dev", option.Key);
        var transition = Assert.Single(body.Transitions);
        Assert.Equal(role.Id, transition.FromQuestionId);
        Assert.Equal(detail.Id, transition.TargetQuestionId);
    }

    /// <summary>A question under an unknown dialog is mapped to 404.</summary>
    [Fact]
    public async Task CreateQuestion_under_an_unknown_dialog_returns_404()
    {
        await using var host = await FlirtyTestHost.StartAsync();

        var response = await host.Client.PostAsJsonAsync(
            $"/flirty/admin/dialogs/{Guid.NewGuid()}/questions",
            new CreateQuestionRequest("q", "Frage?", QuestionType.FreeText, 0, false, null));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>A second question with the same key in the same dialog is mapped to 409.</summary>
    [Fact]
    public async Task CreateQuestion_with_a_duplicate_key_returns_409()
    {
        await using var host = await FlirtyTestHost.StartAsync();
        var dialog = await CreateDialogAsync(host, "dupq");
        await CreateQuestionAsync(host, dialog.Id, "q", QuestionType.FreeText, 0);

        var response = await host.Client.PostAsJsonAsync(
            $"/flirty/admin/dialogs/{dialog.Id}/questions",
            new CreateQuestionRequest("q", "Nochmal?", QuestionType.FreeText, 1, false, null));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    /// <summary>Option CRUD: creating, changing and deleting an answer option incl. status codes.</summary>
    [Fact]
    public async Task AnswerOption_CRUD_walks_all_status_codes()
    {
        await using var host = await FlirtyTestHost.StartAsync();
        var dialog = await CreateDialogAsync(host, "opt");
        var question = await CreateQuestionAsync(host, dialog.Id, "role", QuestionType.SingleChoice, 0);

        var option = await CreateOptionAsync(host, dialog.Id, question.Id, "dev", "Developer", "dev", 0);

        var update = await host.Client.PutAsJsonAsync(
            $"/flirty/admin/dialogs/{dialog.Id}/questions/{question.Id}/options/{option.Id}",
            new UpdateAnswerOptionRequest("dev", "Software developer", "dev", 0));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = await update.Content.ReadFromJsonAsync<AnswerOptionResponse>();
        Assert.Equal("Software developer", updated!.Label);

        var delete = await host.Client.DeleteAsync(
            $"/flirty/admin/dialogs/{dialog.Id}/questions/{question.Id}/options/{option.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
    }

    /// <summary>An option under an unknown question is mapped to 404.</summary>
    [Fact]
    public async Task CreateOption_under_an_unknown_question_returns_404()
    {
        await using var host = await FlirtyTestHost.StartAsync();
        var dialog = await CreateDialogAsync(host, "optnf");

        var response = await host.Client.PostAsJsonAsync(
            $"/flirty/admin/dialogs/{dialog.Id}/questions/{Guid.NewGuid()}/options",
            new CreateAnswerOptionRequest("k", "L", "v", 0));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// Deleting a question removes the transitions that reference it and resets an entry question
    /// pointing at it.
    /// </summary>
    [Fact]
    public async Task DeleteQuestion_cleans_up_transitions_and_the_start_question()
    {
        await using var host = await FlirtyTestHost.StartAsync();
        var dialog = await CreateDialogAsync(host, "cleanup");
        var start = await CreateQuestionAsync(host, dialog.Id, "start", QuestionType.FreeText, 0);
        var next = await CreateQuestionAsync(host, dialog.Id, "next", QuestionType.FreeText, 1);
        await CreateTransitionAsync(host, dialog.Id, start.Id, next.Id, isDefault: true);
        await SetStartQuestionAsync(host, dialog, start.Id);

        var delete = await host.Client.DeleteAsync(
            $"/flirty/admin/dialogs/{dialog.Id}/questions/{start.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var detail = await host.Client.GetFromJsonAsync<DialogDetailResponse>(
            $"/flirty/admin/dialogs/{dialog.Id}");
        Assert.NotNull(detail);
        Assert.Empty(detail.Transitions);
        Assert.Null(detail.StartQuestionId);
        Assert.Single(detail.Questions);
    }

    // ---- Transition CRUD ----

    /// <summary>Changing an unknown transition is mapped to 404.</summary>
    [Fact]
    public async Task UpdateTransition_of_an_unknown_transition_returns_404()
    {
        await using var host = await FlirtyTestHost.StartAsync();
        var dialog = await CreateDialogAsync(host, "trans");

        var response = await host.Client.PutAsJsonAsync(
            $"/flirty/admin/dialogs/{dialog.Id}/transitions/{Guid.NewGuid()}",
            new UpdateTransitionRequest(Guid.NewGuid(), Guid.NewGuid(), null, 0, true));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- Loop CRUD (#41) ----

    /// <summary>Creating, changing and deleting a loop marker over the endpoints.</summary>
    [Fact]
    public async Task Loop_CRUD_creates_changes_and_deletes()
    {
        await using var host = await FlirtyTestHost.StartAsync();
        var dialog = await CreateDialogAsync(host, "loops");
        var entry = await CreateQuestionAsync(host, dialog.Id, "position", QuestionType.FreeText, 0);
        var breaking = await CreateQuestionAsync(host, dialog.Id, "more", QuestionType.FreeText, 1);

        var create = await host.Client.PostAsJsonAsync(
            $"/flirty/admin/dialogs/{dialog.Id}/loops",
            new CreateLoopRequest("positions", entry.Id, breaking.Id));

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = (await create.Content.ReadFromJsonAsync<LoopResponse>())!;
        Assert.Equal("positions", created.CollectionKey);

        var update = await host.Client.PutAsJsonAsync(
            $"/flirty/admin/dialogs/{dialog.Id}/loops/{created.Id}",
            new UpdateLoopRequest("stellen", entry.Id, breaking.Id));

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = (await update.Content.ReadFromJsonAsync<LoopResponse>())!;
        Assert.Equal("stellen", updated.CollectionKey);

        var delete = await host.Client.DeleteAsync($"/flirty/admin/dialogs/{dialog.Id}/loops/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
    }

    /// <summary>The dialog graph carries the loop markers along (since #41 over the REST layer too).</summary>
    [Fact]
    public async Task GetDialog_carries_the_loop_markers_along()
    {
        await using var host = await FlirtyTestHost.StartAsync();
        var dialog = await CreateDialogAsync(host, "loopgraph");
        var entry = await CreateQuestionAsync(host, dialog.Id, "position", QuestionType.FreeText, 0);
        var breaking = await CreateQuestionAsync(host, dialog.Id, "more", QuestionType.FreeText, 1);
        await host.Client.PostAsJsonAsync(
            $"/flirty/admin/dialogs/{dialog.Id}/loops",
            new CreateLoopRequest("positions", entry.Id, breaking.Id));

        var body = await host.Client.GetFromJsonAsync<DialogDetailResponse>(
            $"/flirty/admin/dialogs/{dialog.Id}");

        Assert.NotNull(body);
        var loop = Assert.Single(body.Loops);
        Assert.Equal("positions", loop.CollectionKey);
        Assert.Equal(entry.Id, loop.EntryQuestionId);
        Assert.Equal(breaking.Id, loop.BreakingQuestionId);
    }

    /// <summary>Changing an unknown loop is mapped to 404.</summary>
    [Fact]
    public async Task UpdateLoop_of_an_unknown_loop_returns_404()
    {
        await using var host = await FlirtyTestHost.StartAsync();
        var dialog = await CreateDialogAsync(host, "loop404");

        var response = await host.Client.PutAsJsonAsync(
            $"/flirty/admin/dialogs/{dialog.Id}/loops/{Guid.NewGuid()}",
            new UpdateLoopRequest("positions", Guid.NewGuid(), Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>A second marker with the same collection key in the same dialog is mapped to 409.</summary>
    [Fact]
    public async Task CreateLoop_with_a_duplicate_collection_key_returns_409()
    {
        await using var host = await FlirtyTestHost.StartAsync();
        var dialog = await CreateDialogAsync(host, "duploop");
        var entry = await CreateQuestionAsync(host, dialog.Id, "position", QuestionType.FreeText, 0);
        var breaking = await CreateQuestionAsync(host, dialog.Id, "more", QuestionType.FreeText, 1);
        await host.Client.PostAsJsonAsync(
            $"/flirty/admin/dialogs/{dialog.Id}/loops",
            new CreateLoopRequest("positions", entry.Id, breaking.Id));

        var response = await host.Client.PostAsJsonAsync(
            $"/flirty/admin/dialogs/{dialog.Id}/loops",
            new CreateLoopRequest("positions", breaking.Id, entry.Id));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // ---- Trigger CRUD (#42) ----

    /// <summary>Creating, changing and deleting a trigger definition over the endpoints.</summary>
    [Fact]
    public async Task Trigger_CRUD_creates_changes_and_deletes()
    {
        await using var host = await FlirtyTestHost.StartAsync();
        var dialog = await CreateDialogAsync(host, "triggers");

        var create = await host.Client.PostAsJsonAsync(
            $"/flirty/admin/dialogs/{dialog.Id}/triggers",
            new CreateTriggerRequest(
                TriggerScope.OnDialogCompleted, null, TriggerKind.Webhook,
                "{\"url\":\"https://example.test/hook\"}", null));

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = (await create.Content.ReadFromJsonAsync<TriggerResponse>())!;
        Assert.Equal(TriggerScope.OnDialogCompleted, created.Scope);
        Assert.Equal(TriggerKind.Webhook, created.Kind);
        Assert.Null(created.QuestionId);

        var update = await host.Client.PutAsJsonAsync(
            $"/flirty/admin/dialogs/{dialog.Id}/triggers/{created.Id}",
            new UpdateTriggerRequest(
                TriggerScope.OnDialogCompleted, null, TriggerKind.Webhook,
                "{\"url\":\"https://example.test/andere\",\"name\":\"fertig\"}", "now.Year >= 2026"));

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = (await update.Content.ReadFromJsonAsync<TriggerResponse>())!;
        Assert.Contains("andere", updated.Config);
        Assert.Equal("now.Year >= 2026", updated.Expression);

        var delete = await host.Client.DeleteAsync($"/flirty/admin/dialogs/{dialog.Id}/triggers/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
    }

    /// <summary>The dialog graph carries the triggers along (since #42 over the REST layer too).</summary>
    [Fact]
    public async Task GetDialog_carries_the_triggers_along()
    {
        await using var host = await FlirtyTestHost.StartAsync();
        var dialog = await CreateDialogAsync(host, "triggergraph");
        await host.Client.PostAsJsonAsync(
            $"/flirty/admin/dialogs/{dialog.Id}/triggers",
            new CreateTriggerRequest(
                TriggerScope.AfterAnswer, null, TriggerKind.InProcess, "{\"name\":\"antwort\"}", null));

        var body = await host.Client.GetFromJsonAsync<DialogDetailResponse>($"/flirty/admin/dialogs/{dialog.Id}");

        Assert.NotNull(body);
        var trigger = Assert.Single(body.Triggers);
        Assert.Equal(TriggerScope.AfterAnswer, trigger.Scope);
        Assert.Equal(TriggerKind.InProcess, trigger.Kind);
        Assert.Contains("antwort", trigger.Config);
    }

    /// <summary>Inconsistent requests (configuration, question reference) are mapped to 400 by the pipeline.</summary>
    [Theory]
    [InlineData(TriggerScope.OnDialogCompleted, false, TriggerKind.Webhook, "kein json")]
    [InlineData(TriggerScope.OnDialogCompleted, false, TriggerKind.Webhook, "{\"name\":\"ohne-url\"}")]
    [InlineData(TriggerScope.OnDialogCompleted, false, TriggerKind.Webhook, "{\"url\":\"nicht-absolut\"}")]
    [InlineData(TriggerScope.AfterQuestion, false, TriggerKind.InProcess, "{}")]
    [InlineData(TriggerScope.OnDialogStarted, true, TriggerKind.InProcess, "{}")]
    public async Task CreateTrigger_with_an_inconsistent_request_returns_400(
        TriggerScope scope, bool withQuestion, TriggerKind kind, string config)
    {
        await using var host = await FlirtyTestHost.StartAsync();
        var dialog = await CreateDialogAsync(host, $"bad-{scope}-{withQuestion}-{config.Length}");
        Guid? questionId = withQuestion
            ? (await CreateQuestionAsync(host, dialog.Id, "q", QuestionType.FreeText, 0)).Id
            : null;

        var response = await host.Client.PostAsJsonAsync(
            $"/flirty/admin/dialogs/{dialog.Id}/triggers",
            new CreateTriggerRequest(scope, questionId, kind, config, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>Changing an unknown trigger is mapped to 404.</summary>
    [Fact]
    public async Task UpdateTrigger_of_an_unknown_trigger_returns_404()
    {
        await using var host = await FlirtyTestHost.StartAsync();
        var dialog = await CreateDialogAsync(host, "trigger404");

        var response = await host.Client.PutAsJsonAsync(
            $"/flirty/admin/dialogs/{dialog.Id}/triggers/{Guid.NewGuid()}",
            new UpdateTriggerRequest(
                TriggerScope.OnDialogCompleted, null, TriggerKind.Webhook,
                "{\"url\":\"https://example.test/hook\"}", null));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// Deleting a question clears the triggers referencing it along with it – an
    /// <c>AfterQuestion</c> trigger on a deleted question would otherwise never fire again.
    /// </summary>
    [Fact]
    public async Task DeleteQuestion_removes_the_referencing_triggers()
    {
        await using var host = await FlirtyTestHost.StartAsync();
        var dialog = await CreateDialogAsync(host, "triggercleanup");
        var question = await CreateQuestionAsync(host, dialog.Id, "q", QuestionType.FreeText, 0);
        await host.Client.PostAsJsonAsync(
            $"/flirty/admin/dialogs/{dialog.Id}/triggers",
            new CreateTriggerRequest(
                TriggerScope.AfterQuestion, question.Id, TriggerKind.Webhook,
                "{\"url\":\"https://example.test/hook\"}", null));

        var delete = await host.Client.DeleteAsync(
            $"/flirty/admin/dialogs/{dialog.Id}/questions/{question.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var body = await host.Client.GetFromJsonAsync<DialogDetailResponse>($"/flirty/admin/dialogs/{dialog.Id}");
        Assert.NotNull(body);
        Assert.Empty(body.Triggers);
    }

    // ---- Canvas layout (#102) ----

    /// <summary>
    /// Setting, adjusting and discarding over the endpoints. <c>PUT</c> is deliberately a <b>merge</b>:
    /// a drag gesture moves one element and does not send the whole layout along.
    /// </summary>
    [Fact]
    public async Task Layout_is_set_and_reset_over_the_endpoints()
    {
        await using var host = await FlirtyTestHost.StartAsync();
        var dialog = await CreateDialogAsync(host, "layout");
        var first = await CreateQuestionAsync(host, dialog.Id, "eins", QuestionType.FreeText, 0);
        var second = await CreateQuestionAsync(host, dialog.Id, "zwei", QuestionType.FreeText, 1);

        var set = await host.Client.PutAsJsonAsync(
            $"/flirty/admin/dialogs/{dialog.Id}/layout",
            new SetDialogLayoutRequest(
            [
                new DialogLayoutEntryRequest(LayoutElementKind.Question, first.Id, 100, 200),
                new DialogLayoutEntryRequest(LayoutElementKind.Question, second.Id, 300, 400),
            ]));

        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        Assert.Equal(2, (await set.Content.ReadFromJsonAsync<DialogLayoutResponse[]>())!.Length);

        // Merge: only the first question is named, the second keeps its position.
        var move = await host.Client.PutAsJsonAsync(
            $"/flirty/admin/dialogs/{dialog.Id}/layout",
            new SetDialogLayoutRequest(
                [new DialogLayoutEntryRequest(LayoutElementKind.Question, first.Id, 140, 260)]));

        Assert.Equal(HttpStatusCode.OK, move.StatusCode);
        var merged = (await move.Content.ReadFromJsonAsync<DialogLayoutResponse[]>())!;
        Assert.Equal(2, merged.Length);
        Assert.Equal(140, Assert.Single(merged, row => row.ElementId == first.Id).X);
        Assert.Equal(300, Assert.Single(merged, row => row.ElementId == second.Id).X);

        var reset = await host.Client.DeleteAsync($"/flirty/admin/dialogs/{dialog.Id}/layout");
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        var body = await host.Client.GetFromJsonAsync<DialogDetailResponse>($"/flirty/admin/dialogs/{dialog.Id}");
        Assert.NotNull(body);
        Assert.Empty(body.Layout);
    }

    /// <summary>The dialog detail endpoint carries the positions along – the source of the graph view.</summary>
    [Fact]
    public async Task GetDialog_carries_the_layout_along()
    {
        await using var host = await FlirtyTestHost.StartAsync();
        var dialog = await CreateDialogAsync(host, "layoutread");
        var question = await CreateQuestionAsync(host, dialog.Id, "q", QuestionType.FreeText, 0);

        var set = await host.Client.PutAsJsonAsync(
            $"/flirty/admin/dialogs/{dialog.Id}/layout",
            new SetDialogLayoutRequest(
                [new DialogLayoutEntryRequest(LayoutElementKind.Question, question.Id, 88, 99)]));
        set.EnsureSuccessStatusCode();

        var body = await host.Client.GetFromJsonAsync<DialogDetailResponse>($"/flirty/admin/dialogs/{dialog.Id}");
        Assert.NotNull(body);

        var layout = Assert.Single(body.Layout);
        Assert.Equal(question.Id, layout.ElementId);
        Assert.Equal(88, layout.X);
        Assert.Equal(99, layout.Y);
    }

    /// <summary>
    /// <b>This stage's promise over HTTP:</b> a published dialog can still be arranged. Where every
    /// graph change returns 409, the layout endpoint answers with 200 (ADR 0007).
    /// </summary>
    [Fact]
    public async Task SetLayout_on_a_published_dialog_returns_200()
    {
        await using var host = await FlirtyTestHost.StartAsync();
        var (dialog, question) = await CreatePublishedDialogAsync(host, "layoutpublished");

        // Counter-check: a real graph change is locked on exactly this dialog.
        var rename = await host.Client.PutAsJsonAsync(
            $"/flirty/admin/dialogs/{dialog.Id}/questions/{question.Id}",
            new UpdateQuestionRequest("start", "Neu?", QuestionType.FreeText, 0, false, null));
        Assert.Equal(HttpStatusCode.Conflict, rename.StatusCode);

        var move = await host.Client.PutAsJsonAsync(
            $"/flirty/admin/dialogs/{dialog.Id}/layout",
            new SetDialogLayoutRequest(
                [new DialogLayoutEntryRequest(LayoutElementKind.Question, question.Id, 640, 480)]));

        Assert.Equal(HttpStatusCode.OK, move.StatusCode);

        var reset = await host.Client.DeleteAsync($"/flirty/admin/dialogs/{dialog.Id}/layout");
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
    }

    /// <summary>The batch's request rules come through as 400, not as 409.</summary>
    [Fact]
    public async Task SetLayout_with_a_duplicate_element_returns_400()
    {
        await using var host = await FlirtyTestHost.StartAsync();
        var dialog = await CreateDialogAsync(host, "layoutinvalid");
        var question = await CreateQuestionAsync(host, dialog.Id, "q", QuestionType.FreeText, 0);

        var response = await host.Client.PutAsJsonAsync(
            $"/flirty/admin/dialogs/{dialog.Id}/layout",
            new SetDialogLayoutRequest(
            [
                new DialogLayoutEntryRequest(LayoutElementKind.Question, question.Id, 10, 10),
                new DialogLayoutEntryRequest(LayoutElementKind.Question, question.Id, 20, 20),
            ]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>Unknown dialog: 404, not 204 – a silent nothing would be misleading.</summary>
    [Fact]
    public async Task Layout_of_an_unknown_dialog_returns_404()
    {
        await using var host = await FlirtyTestHost.StartAsync();

        var set = await host.Client.PutAsJsonAsync(
            $"/flirty/admin/dialogs/{Guid.NewGuid()}/layout",
            new SetDialogLayoutRequest(
                [new DialogLayoutEntryRequest(LayoutElementKind.Question, Guid.NewGuid(), 1, 1)]));
        Assert.Equal(HttpStatusCode.NotFound, set.StatusCode);

        var reset = await host.Client.DeleteAsync($"/flirty/admin/dialogs/{Guid.NewGuid()}/layout");
        Assert.Equal(HttpStatusCode.NotFound, reset.StatusCode);
    }

    /// <summary>
    /// Deriving a version takes the positions along and rewrites them onto the cloned questions –
    /// checked over HTTP, because exactly this branch is manual work.
    /// </summary>
    [Fact]
    public async Task CreateDialogVersion_clones_the_layout_onto_the_new_question_ids()
    {
        await using var host = await FlirtyTestHost.StartAsync();
        var (dialog, question) = await CreatePublishedDialogAsync(host, "layoutclone");

        var set = await host.Client.PutAsJsonAsync(
            $"/flirty/admin/dialogs/{dialog.Id}/layout",
            new SetDialogLayoutRequest(
                [new DialogLayoutEntryRequest(LayoutElementKind.Question, question.Id, 120, 240)]));
        set.EnsureSuccessStatusCode();

        var version = await host.Client.PostAsync(
            $"/flirty/admin/dialogs/{dialog.Id}/versions", content: null);
        Assert.Equal(HttpStatusCode.Created, version.StatusCode);

        var copy = (await version.Content.ReadFromJsonAsync<DialogDetailResponse>())!;
        var layout = Assert.Single(copy.Layout);

        Assert.Equal(120, layout.X);
        Assert.Equal(240, layout.Y);
        Assert.NotEqual(question.Id, layout.ElementId);
        Assert.Equal(Assert.Single(copy.Questions).Id, layout.ElementId);
    }

    /// <summary>
    /// Deleting a question clears its position along with it – <c>ElementId</c> is FK-free, so the
    /// database does not do it on its own.
    /// </summary>
    [Fact]
    public async Task DeleteQuestion_removes_the_layout_row()
    {
        await using var host = await FlirtyTestHost.StartAsync();
        var dialog = await CreateDialogAsync(host, "layoutcleanup");
        var question = await CreateQuestionAsync(host, dialog.Id, "q", QuestionType.FreeText, 0);

        var set = await host.Client.PutAsJsonAsync(
            $"/flirty/admin/dialogs/{dialog.Id}/layout",
            new SetDialogLayoutRequest(
                [new DialogLayoutEntryRequest(LayoutElementKind.Question, question.Id, 10, 20)]));
        set.EnsureSuccessStatusCode();

        var delete = await host.Client.DeleteAsync(
            $"/flirty/admin/dialogs/{dialog.Id}/questions/{question.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var body = await host.Client.GetFromJsonAsync<DialogDetailResponse>($"/flirty/admin/dialogs/{dialog.Id}");
        Assert.NotNull(body);
        Assert.Empty(body.Layout);
    }

    // ---- Publish workflow ----

    /// <summary>A dialog without an entry question cannot be published (409).</summary>
    [Fact]
    public async Task Publish_without_an_entry_question_returns_409()
    {
        await using var host = await FlirtyTestHost.StartAsync();
        var dialog = await CreateDialogAsync(host, "unready");

        var response = await host.Client.PostAsync($"/flirty/admin/dialogs/{dialog.Id}/publish", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    /// <summary>Publish/unpublish toggles the publication flag.</summary>
    [Fact]
    public async Task PublishUnpublish_toggles_the_flag()
    {
        await using var host = await FlirtyTestHost.StartAsync();
        var dialog = await CreateDialogAsync(host, "toggle");
        var question = await CreateQuestionAsync(host, dialog.Id, "q", QuestionType.FreeText, 0);
        await SetStartQuestionAsync(host, dialog, question.Id);

        var publish = await host.Client.PostAsync($"/flirty/admin/dialogs/{dialog.Id}/publish", content: null);
        Assert.Equal(HttpStatusCode.OK, publish.StatusCode);
        var published = await publish.Content.ReadFromJsonAsync<DialogResponse>();
        Assert.True(published!.IsPublished);

        var unpublish = await host.Client.PostAsync($"/flirty/admin/dialogs/{dialog.Id}/unpublish", content: null);
        Assert.Equal(HttpStatusCode.OK, unpublish.StatusCode);
        var unpublished = await unpublish.Content.ReadFromJsonAsync<DialogResponse>();
        Assert.False(unpublished!.IsPublished);
    }

    // ---- Versioning ----

    /// <summary>
    /// On a published dialog, graph changes return <c>409</c> – the message names the way out.
    /// </summary>
    [Fact]
    public async Task Graph_change_on_a_published_dialog_returns_409()
    {
        await using var host = await FlirtyTestHost.StartAsync();
        var (dialog, question) = await CreatePublishedDialogAsync(host, "locked");

        var created = await host.Client.PostAsJsonAsync(
            $"/flirty/admin/dialogs/{dialog.Id}/questions",
            new CreateQuestionRequest("weitere", "Weitere?", QuestionType.FreeText, 1, false, null));
        Assert.Equal(HttpStatusCode.Conflict, created.StatusCode);

        var deleted = await host.Client.DeleteAsync(
            $"/flirty/admin/dialogs/{dialog.Id}/questions/{question.Id}");
        Assert.Equal(HttpStatusCode.Conflict, deleted.StatusCode);

        var problem = await created.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Contains("new version", problem!.Detail);
    }

    /// <summary>
    /// <c>POST .../versions</c> returns the copy as a draft with the next version number – and that one
    /// is editable again.
    /// </summary>
    [Fact]
    public async Task Versions_creates_an_editable_follow_up_version()
    {
        await using var host = await FlirtyTestHost.StartAsync();
        var (dialog, _) = await CreatePublishedDialogAsync(host, "versioned");

        var response = await host.Client.PostAsync($"/flirty/admin/dialogs/{dialog.Id}/versions", content: null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var copy = await response.Content.ReadFromJsonAsync<DialogDetailResponse>();
        Assert.NotNull(copy);
        Assert.Equal("versioned", copy.Key);
        Assert.Equal(2, copy.Version);
        Assert.False(copy.IsPublished);
        Assert.NotEqual(dialog.Id, copy.Id);
        Assert.Single(copy.Questions);

        // The draft can be changed, the published version stays locked.
        var added = await host.Client.PostAsJsonAsync(
            $"/flirty/admin/dialogs/{copy.Id}/questions",
            new CreateQuestionRequest("weitere", "Weitere?", QuestionType.FreeText, 1, false, null));
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
    }

    /// <summary>
    /// Deleting with a running session returns <c>409</c>; after <c>abandon-sessions</c> it takes
    /// effect. The session is preserved afterwards as an abandoned row.
    /// </summary>
    [Fact]
    public async Task Delete_with_a_running_session_returns_409_and_works_after_the_abandon()
    {
        await using var host = await FlirtyTestHost.StartAsync();
        var (dialog, _) = await CreatePublishedDialogAsync(host, "busy");

        var start = await host.Client.PostAsJsonAsync(
            "/flirty/sessions", new StartSessionRequest("busy", "user-1"));
        start.EnsureSuccessStatusCode();
        var session = await start.Content.ReadFromJsonAsync<StartSessionResponse>();

        var blocked = await host.Client.DeleteAsync($"/flirty/admin/dialogs/{dialog.Id}");
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        var problem = await blocked.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Contains("1 session(s)", problem!.Detail);

        var abandon = await host.Client.PostAsync(
            $"/flirty/admin/dialogs/{dialog.Id}/abandon-sessions", content: null);
        Assert.Equal(HttpStatusCode.OK, abandon.StatusCode);
        var abandoned = await abandon.Content.ReadFromJsonAsync<AbandonSessionsResponse>();
        Assert.Equal(1, abandoned!.AbandonedSessions);

        // The abandoned session is still readable (only no longer resumable).
        var state = await host.Client.GetAsync($"/flirty/sessions/{session!.SessionId}");
        Assert.Equal(HttpStatusCode.OK, state.StatusCode);

        var deleted = await host.Client.DeleteAsync($"/flirty/admin/dialogs/{dialog.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }

    // ---- End-to-End ----

    /// <summary>
    /// A published dialog built purely over the admin API can then be started over the runtime
    /// endpoint and played through to completion.
    /// </summary>
    [Fact]
    public async Task Admin_created_dialog_is_startable_over_the_runtime()
    {
        await using var host = await FlirtyTestHost.StartAsync();

        var dialog = await CreateDialogAsync(host, "e2e");
        var question = await CreateQuestionAsync(host, dialog.Id, "name", QuestionType.FreeText, 0);
        await SetStartQuestionAsync(host, dialog, question.Id);

        var publish = await host.Client.PostAsync($"/flirty/admin/dialogs/{dialog.Id}/publish", content: null);
        publish.EnsureSuccessStatusCode();

        // Runtime: start a session over the regular endpoint.
        var start = await host.Client.PostAsJsonAsync(
            "/flirty/sessions", new StartSessionRequest("e2e", "user-1"));
        Assert.Equal(HttpStatusCode.Created, start.StatusCode);
        var session = await start.Content.ReadFromJsonAsync<StartSessionResponse>();
        Assert.NotNull(session);
        Assert.Equal("name", session.CurrentQuestion.Key);

        // Answer the (terminal) question -> the dialog is completed.
        var answer = await host.Client.PostAsJsonAsync(
            $"/flirty/sessions/{session.SessionId}/answers",
            new SubmitAnswerRequest(session.CurrentQuestion.Id, "\"Ada\""));
        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        var answered = await answer.Content.ReadFromJsonAsync<SubmitAnswerResponse>();
        Assert.NotNull(answered);
        Assert.True(answered.IsCompleted);
    }

    // ---- Helpers ----

    private static async Task<DialogResponse> CreateDialogAsync(FlirtyTestHost host, string key)
    {
        var response = await host.Client.PostAsJsonAsync(
            "/flirty/admin/dialogs", new CreateDialogRequest(key, key, null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<DialogResponse>())!;
    }

    private static async Task<QuestionResponse> CreateQuestionAsync(
        FlirtyTestHost host, Guid dialogId, string key, QuestionType type, int order)
    {
        var response = await host.Client.PostAsJsonAsync(
            $"/flirty/admin/dialogs/{dialogId}/questions",
            new CreateQuestionRequest(key, $"{key}?", type, order, false, null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<QuestionResponse>())!;
    }

    private static async Task<AnswerOptionResponse> CreateOptionAsync(
        FlirtyTestHost host, Guid dialogId, Guid questionId, string key, string label, string value, int order)
    {
        var response = await host.Client.PostAsJsonAsync(
            $"/flirty/admin/dialogs/{dialogId}/questions/{questionId}/options",
            new CreateAnswerOptionRequest(key, label, value, order));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AnswerOptionResponse>())!;
    }

    private static async Task<TransitionResponse> CreateTransitionAsync(
        FlirtyTestHost host, Guid dialogId, Guid fromQuestionId, Guid targetQuestionId, bool isDefault)
    {
        var response = await host.Client.PostAsJsonAsync(
            $"/flirty/admin/dialogs/{dialogId}/transitions",
            new CreateTransitionRequest(fromQuestionId, targetQuestionId, null, 0, isDefault));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TransitionResponse>())!;
    }

    /// <summary>
    /// Creates a published dialog with exactly one (terminal) question – the starting point of the
    /// versioning tests.
    /// </summary>
    private static async Task<(DialogResponse Dialog, QuestionResponse Question)> CreatePublishedDialogAsync(
        FlirtyTestHost host, string key)
    {
        var dialog = await CreateDialogAsync(host, key);
        var question = await CreateQuestionAsync(host, dialog.Id, "start", QuestionType.FreeText, 0);
        await SetStartQuestionAsync(host, dialog, question.Id);

        var publish = await host.Client.PostAsync($"/flirty/admin/dialogs/{dialog.Id}/publish", content: null);
        publish.EnsureSuccessStatusCode();

        return (dialog, question);
    }

    private static async Task SetStartQuestionAsync(FlirtyTestHost host, DialogResponse dialog, Guid startQuestionId)
    {
        var response = await host.Client.PutAsJsonAsync(
            $"/flirty/admin/dialogs/{dialog.Id}",
            new UpdateDialogRequest(dialog.Key, dialog.Name, dialog.Description, startQuestionId));
        response.EnsureSuccessStatusCode();
    }
}
