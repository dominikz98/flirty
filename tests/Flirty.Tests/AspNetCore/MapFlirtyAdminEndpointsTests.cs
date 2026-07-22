using System.Net;
using System.Net.Http.Json;
using Flirty.AspNetCore.Dtos;
using Flirty.AspNetCore.Dtos.Admin;
using Flirty.Domain;

namespace Flirty.Tests.AspNetCore;

/// <summary>
/// Integrationstests für <c>MapFlirtyAdminEndpoints</c> (#36): fahren die Admin-CRUD-Endpunkte über
/// einen In-Process-TestServer mit echten HTTP-Aufrufen gegen eine SQLite-in-memory-Datenbank
/// (Docker-frei). Geprüft werden die CRUD-Happy-Paths je Entität (Dialog/Frage/Option/Übergang), der
/// Publish-Workflow, das Fehler-Mapping (404/400/409), das Delete-Cleanup verwaister Übergänge sowie
/// der End-to-End-Nachweis, dass ein rein per API aufgebauter Dialog anschließend über die Laufzeit
/// startbar ist.
/// </summary>
public sealed class MapFlirtyAdminEndpointsTests
{
    // ---- Dialog-CRUD ----

    /// <summary>Das Anlegen eines Dialogs liefert 201 mit Location-Header und initialen Metadaten.</summary>
    [Fact]
    public async Task CreateDialog_liefert_201_mit_Location_und_Metadaten()
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

    /// <summary>Ein fehlender Pflichtwert (Key) wird über die Pipeline-Validierung auf 400 abgebildet.</summary>
    [Fact]
    public async Task CreateDialog_ohne_Key_liefert_400()
    {
        await using var host = await FlirtyTestHost.StartAsync();

        var response = await host.Client.PostAsJsonAsync(
            "/flirty/admin/dialogs", new { name = "Ohne Key" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>Ein zweiter Dialog mit gleichem Schlüssel wird auf 409 abgebildet.</summary>
    [Fact]
    public async Task CreateDialog_mit_doppeltem_Key_liefert_409()
    {
        await using var host = await FlirtyTestHost.StartAsync();
        await CreateDialogAsync(host, "dup");

        var response = await host.Client.PostAsJsonAsync(
            "/flirty/admin/dialogs", new CreateDialogRequest("dup", "Andere", null));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    /// <summary>Die Liste enthält die zuvor angelegten Dialoge.</summary>
    [Fact]
    public async Task ListDialogs_liefert_angelegte_Dialoge()
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

    /// <summary>Das Ändern eines Dialogs übernimmt die neuen Metadaten.</summary>
    [Fact]
    public async Task UpdateDialog_aendert_Metadaten()
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

    /// <summary>Das Ändern eines unbekannten Dialogs wird auf 404 abgebildet.</summary>
    [Fact]
    public async Task UpdateDialog_unbekannt_liefert_404()
    {
        await using var host = await FlirtyTestHost.StartAsync();

        var response = await host.Client.PutAsJsonAsync(
            $"/flirty/admin/dialogs/{Guid.NewGuid()}",
            new UpdateDialogRequest("x", "X", null, null));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>Das Löschen liefert 204; ein anschließendes Lesen 404.</summary>
    [Fact]
    public async Task DeleteDialog_liefert_204_und_danach_404()
    {
        await using var host = await FlirtyTestHost.StartAsync();
        var dialog = await CreateDialogAsync(host, "weg");

        var delete = await host.Client.DeleteAsync($"/flirty/admin/dialogs/{dialog.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var get = await host.Client.GetAsync($"/flirty/admin/dialogs/{dialog.Id}");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }

    // ---- Frage-/Options-CRUD + Graph ----

    /// <summary>Das Lesen eines Dialogs liefert seinen Graphen mit Fragen, Optionen und Übergängen.</summary>
    [Fact]
    public async Task GetDialog_liefert_Graph_mit_Fragen_und_Uebergaengen()
    {
        await using var host = await FlirtyTestHost.StartAsync();
        var dialog = await CreateDialogAsync(host, "graph");
        var role = await CreateQuestionAsync(host, dialog.Id, "role", QuestionType.SingleChoice, 0);
        await CreateOptionAsync(host, dialog.Id, role.Id, "dev", "Entwickler", "dev", 0);
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

    /// <summary>Eine Frage unter einem unbekannten Dialog wird auf 404 abgebildet.</summary>
    [Fact]
    public async Task CreateQuestion_unter_unbekanntem_Dialog_liefert_404()
    {
        await using var host = await FlirtyTestHost.StartAsync();

        var response = await host.Client.PostAsJsonAsync(
            $"/flirty/admin/dialogs/{Guid.NewGuid()}/questions",
            new CreateQuestionRequest("q", "Frage?", QuestionType.FreeText, 0, false, null));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>Eine zweite Frage mit gleichem Schlüssel im selben Dialog wird auf 409 abgebildet.</summary>
    [Fact]
    public async Task CreateQuestion_mit_doppeltem_Key_liefert_409()
    {
        await using var host = await FlirtyTestHost.StartAsync();
        var dialog = await CreateDialogAsync(host, "dupq");
        await CreateQuestionAsync(host, dialog.Id, "q", QuestionType.FreeText, 0);

        var response = await host.Client.PostAsJsonAsync(
            $"/flirty/admin/dialogs/{dialog.Id}/questions",
            new CreateQuestionRequest("q", "Nochmal?", QuestionType.FreeText, 1, false, null));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    /// <summary>Options-CRUD: Anlegen, Ändern und Löschen einer Antwortoption inkl. Statuscodes.</summary>
    [Fact]
    public async Task AnswerOption_CRUD_durchlaeuft_alle_Statuscodes()
    {
        await using var host = await FlirtyTestHost.StartAsync();
        var dialog = await CreateDialogAsync(host, "opt");
        var question = await CreateQuestionAsync(host, dialog.Id, "role", QuestionType.SingleChoice, 0);

        var option = await CreateOptionAsync(host, dialog.Id, question.Id, "dev", "Entwickler", "dev", 0);

        var update = await host.Client.PutAsJsonAsync(
            $"/flirty/admin/dialogs/{dialog.Id}/questions/{question.Id}/options/{option.Id}",
            new UpdateAnswerOptionRequest("dev", "Software-Entwickler", "dev", 0));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = await update.Content.ReadFromJsonAsync<AnswerOptionResponse>();
        Assert.Equal("Software-Entwickler", updated!.Label);

        var delete = await host.Client.DeleteAsync(
            $"/flirty/admin/dialogs/{dialog.Id}/questions/{question.Id}/options/{option.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
    }

    /// <summary>Eine Option unter einer unbekannten Frage wird auf 404 abgebildet.</summary>
    [Fact]
    public async Task CreateOption_unter_unbekannter_Frage_liefert_404()
    {
        await using var host = await FlirtyTestHost.StartAsync();
        var dialog = await CreateDialogAsync(host, "optnf");

        var response = await host.Client.PostAsJsonAsync(
            $"/flirty/admin/dialogs/{dialog.Id}/questions/{Guid.NewGuid()}/options",
            new CreateAnswerOptionRequest("k", "L", "v", 0));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// Das Löschen einer Frage entfernt referenzierende Übergänge und setzt eine darauf zeigende
    /// Einstiegsfrage zurück.
    /// </summary>
    [Fact]
    public async Task DeleteQuestion_bereinigt_Uebergaenge_und_StartQuestion()
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

    // ---- Übergang-CRUD ----

    /// <summary>Das Ändern eines unbekannten Übergangs wird auf 404 abgebildet.</summary>
    [Fact]
    public async Task UpdateTransition_unbekannt_liefert_404()
    {
        await using var host = await FlirtyTestHost.StartAsync();
        var dialog = await CreateDialogAsync(host, "trans");

        var response = await host.Client.PutAsJsonAsync(
            $"/flirty/admin/dialogs/{dialog.Id}/transitions/{Guid.NewGuid()}",
            new UpdateTransitionRequest(Guid.NewGuid(), Guid.NewGuid(), null, 0, true));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- Schleifen-CRUD (#41) ----

    /// <summary>Anlegen, Ändern und Löschen eines Schleifen-Markers über die Endpunkte.</summary>
    [Fact]
    public async Task Loop_CRUD_legt_an_aendert_und_loescht()
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

    /// <summary>Der Dialog-Graph liefert die Schleifen-Marker mit (seit #41 auch über die REST-Schicht).</summary>
    [Fact]
    public async Task GetDialog_liefert_die_Schleifen_Marker_mit()
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

    /// <summary>Das Ändern einer unbekannten Schleife wird auf 404 abgebildet.</summary>
    [Fact]
    public async Task UpdateLoop_unbekannt_liefert_404()
    {
        await using var host = await FlirtyTestHost.StartAsync();
        var dialog = await CreateDialogAsync(host, "loop404");

        var response = await host.Client.PutAsJsonAsync(
            $"/flirty/admin/dialogs/{dialog.Id}/loops/{Guid.NewGuid()}",
            new UpdateLoopRequest("positions", Guid.NewGuid(), Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>Ein zweiter Marker mit gleichem Collection-Schlüssel im selben Dialog wird auf 409 abgebildet.</summary>
    [Fact]
    public async Task CreateLoop_mit_doppeltem_CollectionKey_liefert_409()
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

    // ---- Publish-Workflow ----

    /// <summary>Ein Dialog ohne Einstiegsfrage kann nicht veröffentlicht werden (409).</summary>
    [Fact]
    public async Task Publish_ohne_Einstiegsfrage_liefert_409()
    {
        await using var host = await FlirtyTestHost.StartAsync();
        var dialog = await CreateDialogAsync(host, "unready");

        var response = await host.Client.PostAsync($"/flirty/admin/dialogs/{dialog.Id}/publish", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    /// <summary>Publish/Unpublish schaltet das Veröffentlichungs-Flag um.</summary>
    [Fact]
    public async Task PublishUnpublish_schaltet_das_Flag()
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

    // ---- End-to-End ----

    /// <summary>
    /// Ein rein per Admin-API aufgebauter, veröffentlichter Dialog ist anschließend über den
    /// Laufzeit-Endpunkt startbar und bis zum Abschluss durchspielbar.
    /// </summary>
    [Fact]
    public async Task Admin_erstellter_Dialog_ist_ueber_die_Laufzeit_startbar()
    {
        await using var host = await FlirtyTestHost.StartAsync();

        var dialog = await CreateDialogAsync(host, "e2e");
        var question = await CreateQuestionAsync(host, dialog.Id, "name", QuestionType.FreeText, 0);
        await SetStartQuestionAsync(host, dialog, question.Id);

        var publish = await host.Client.PostAsync($"/flirty/admin/dialogs/{dialog.Id}/publish", content: null);
        publish.EnsureSuccessStatusCode();

        // Laufzeit: Session über den regulären Endpunkt starten.
        var start = await host.Client.PostAsJsonAsync(
            "/flirty/sessions", new StartSessionRequest("e2e", "user-1"));
        Assert.Equal(HttpStatusCode.Created, start.StatusCode);
        var session = await start.Content.ReadFromJsonAsync<StartSessionResponse>();
        Assert.NotNull(session);
        Assert.Equal("name", session.CurrentQuestion.Key);

        // Die (terminale) Frage beantworten -> Dialog ist abgeschlossen.
        var answer = await host.Client.PostAsJsonAsync(
            $"/flirty/sessions/{session.SessionId}/answers",
            new SubmitAnswerRequest(session.CurrentQuestion.Id, "\"Ada\""));
        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        var answered = await answer.Content.ReadFromJsonAsync<SubmitAnswerResponse>();
        Assert.NotNull(answered);
        Assert.True(answered.IsCompleted);
    }

    // ---- Helfer ----

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

    private static async Task SetStartQuestionAsync(FlirtyTestHost host, DialogResponse dialog, Guid startQuestionId)
    {
        var response = await host.Client.PutAsJsonAsync(
            $"/flirty/admin/dialogs/{dialog.Id}",
            new UpdateDialogRequest(dialog.Key, dialog.Name, dialog.Description, startQuestionId));
        response.EnsureSuccessStatusCode();
    }
}
