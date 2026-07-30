using System.Net;
using System.Net.Http.Json;
using Flirty.AspNetCore.Dtos;
using Flirty.Domain;
using Flirty.Tests.Persistence;

namespace Flirty.Tests.AspNetCore;

/// <summary>
/// Integration tests for <c>MapFlirtyEndpoints</c> (#35): drive the four endpoints over an in-process
/// <c>TestServer</c> with real HTTP calls against a SQLite in-memory database (Docker-free). Checked
/// are the happy path (start/answer/resume/edit incl. end-to-end completion) as well as the error
/// mapping of the engine exceptions onto HTTP status codes (404/400/409).
/// </summary>
public sealed class MapFlirtyEndpointsTests
{
    // ---- Happy path ----

    /// <summary>A fresh start returns 201 with a Location header, a new session and the first question.</summary>
    [Fact]
    public async Task Start_returns_201_with_the_session_and_the_first_question()
    {
        await using var host = await StartBranchingHostAsync();

        var response = await host.Client.PostAsJsonAsync(
            "/flirty/sessions", new StartSessionRequest("branching", "user-1"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<StartSessionResponse>();
        Assert.NotNull(body);
        Assert.NotEqual(Guid.Empty, body.SessionId);
        Assert.False(body.IsResumed);
        Assert.Equal("role", body.CurrentQuestion.Key);
        Assert.Equal(QuestionType.SingleChoice, body.CurrentQuestion.Type);
        Assert.Equal(2, body.CurrentQuestion.Options.Count);
        Assert.Contains($"/flirty/sessions/{body.SessionId}", response.Headers.Location?.ToString());
    }

    /// <summary>An answer to the open question returns 200 and advances to the follow-up question.</summary>
    [Fact]
    public async Task Answer_advances_to_the_next_question()
    {
        await using var host = await StartBranchingHostAsync();
        var start = await StartSessionAsync(host);

        var response = await host.Client.PostAsJsonAsync(
            $"/flirty/sessions/{start.SessionId}/answers",
            new SubmitAnswerRequest(start.CurrentQuestion.Id, "\"dev\""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SubmitAnswerResponse>();
        Assert.NotNull(body);
        Assert.False(body.IsCompleted);
        Assert.NotNull(body.NextQuestion);
        Assert.Equal("devDetail", body.NextQuestion.Key);
    }

    /// <summary>The end-to-end run (start -> dev -> free text) completes the dialog.</summary>
    [Fact]
    public async Task Answer_to_a_terminal_question_completes_the_dialog()
    {
        await using var host = await StartBranchingHostAsync();
        var start = await StartSessionAsync(host);

        var afterDev = await SubmitAnswerAsync(host, start.SessionId, start.CurrentQuestion.Id, "\"dev\"");
        Assert.NotNull(afterDev.NextQuestion);

        var response = await host.Client.PostAsJsonAsync(
            $"/flirty/sessions/{start.SessionId}/answers",
            new SubmitAnswerRequest(afterDev.NextQuestion.Id, "\"C#\""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SubmitAnswerResponse>();
        Assert.NotNull(body);
        Assert.True(body.IsCompleted);
        Assert.Null(body.NextQuestion);
    }

    /// <summary>Reading the state returns 200 with the status, the current question and the answers so far.</summary>
    [Fact]
    public async Task Resume_returns_the_status_the_current_question_and_the_answers()
    {
        await using var host = await StartBranchingHostAsync();
        var start = await StartSessionAsync(host);
        await SubmitAnswerAsync(host, start.SessionId, start.CurrentQuestion.Id, "\"dev\"");

        var response = await host.Client.GetAsync($"/flirty/sessions/{start.SessionId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SessionStateResponse>();
        Assert.NotNull(body);
        Assert.Equal(SessionStatus.InProgress, body.Status);
        Assert.NotNull(body.CurrentQuestion);
        Assert.Equal("devDetail", body.CurrentQuestion.Key);
        var answer = Assert.Single(body.Answers);
        Assert.Equal("role", answer.QuestionKey);
        Assert.Equal("\"dev\"", answer.Value);
    }

    /// <summary>Editing an earlier answer recomputes the path and reports the discarded answers.</summary>
    [Fact]
    public async Task Edit_recomputes_the_path_and_reports_the_invalidated_answers()
    {
        await using var host = await StartBranchingHostAsync();
        var start = await StartSessionAsync(host);
        var roleQuestionId = start.CurrentQuestion.Id;
        var afterDev = await SubmitAnswerAsync(host, start.SessionId, roleQuestionId, "\"dev\"");
        await SubmitAnswerAsync(host, start.SessionId, afterDev.NextQuestion!.Id, "\"C#\"");

        // Edit from "dev" to "pm": the dev branch (the devDetail answer) is discarded and the path
        // now leads to pmDetail.
        var response = await host.Client.PutAsJsonAsync(
            $"/flirty/sessions/{start.SessionId}/answers/{roleQuestionId}",
            new EditAnswerRequest("\"pm\""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<EditAnswerResponse>();
        Assert.NotNull(body);
        Assert.False(body.IsCompleted);
        Assert.NotNull(body.NextQuestion);
        Assert.Equal("pmDetail", body.NextQuestion.Key);
        Assert.Equal(1, body.InvalidatedAnswers);
    }

    // ---- Error cases ----

    /// <summary>Starting an unknown dialog is mapped to 404.</summary>
    [Fact]
    public async Task Start_with_an_unknown_dialog_returns_404()
    {
        await using var host = await StartBranchingHostAsync();

        var response = await host.Client.PostAsJsonAsync(
            "/flirty/sessions", new StartSessionRequest("gibt-es-nicht", "user-1"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>Reading an unknown session is mapped to 404.</summary>
    [Fact]
    public async Task Resume_of_an_unknown_session_returns_404()
    {
        await using var host = await StartBranchingHostAsync();

        var response = await host.Client.GetAsync($"/flirty/sessions/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>An answer to a question that is no longer open is mapped to 409.</summary>
    [Fact]
    public async Task Answer_to_a_question_that_is_not_open_returns_409()
    {
        await using var host = await StartBranchingHostAsync();
        var start = await StartSessionAsync(host);
        var roleQuestionId = start.CurrentQuestion.Id;
        await SubmitAnswerAsync(host, start.SessionId, roleQuestionId, "\"dev\"");

        // After advancing, the entry question is no longer the currently open question.
        var response = await host.Client.PostAsJsonAsync(
            $"/flirty/sessions/{start.SessionId}/answers",
            new SubmitAnswerRequest(roleQuestionId, "\"dev\""));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    /// <summary>A missing required value (DialogKey) is mapped to 400 by the pipeline validation.</summary>
    [Fact]
    public async Task Start_without_a_DialogKey_returns_400()
    {
        await using var host = await StartBranchingHostAsync();

        // dialogKey is deliberately omitted -> [Required] on the StartDialogCommand kicks in.
        var response = await host.Client.PostAsJsonAsync(
            "/flirty/sessions", new { externalUserKey = "user-1" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- Infrastructure ----

    /// <summary>Starts a TestServer that has the branching dialog (#26) seeded.</summary>
    private static Task<FlirtyTestHost> StartBranchingHostAsync()
        => FlirtyTestHost.StartAsync(context =>
            context.Dialogs.Add(TestDialogFactory.BuildBranchingDialog(Guid.NewGuid(), out _)));

    /// <summary>Starts a session over the endpoint and returns the response.</summary>
    private static async Task<StartSessionResponse> StartSessionAsync(FlirtyTestHost host)
    {
        var response = await host.Client.PostAsJsonAsync(
            "/flirty/sessions", new StartSessionRequest("branching", "user-1"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<StartSessionResponse>())!;
    }

    /// <summary>Submits an answer over the endpoint and returns the response.</summary>
    private static async Task<SubmitAnswerResponse> SubmitAnswerAsync(
        FlirtyTestHost host, Guid sessionId, Guid questionId, string value)
    {
        var response = await host.Client.PostAsJsonAsync(
            $"/flirty/sessions/{sessionId}/answers", new SubmitAnswerRequest(questionId, value));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SubmitAnswerResponse>())!;
    }
}
