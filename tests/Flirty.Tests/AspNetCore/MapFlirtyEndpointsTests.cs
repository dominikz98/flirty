using System.Net;
using System.Net.Http.Json;
using Flirty.AspNetCore.Dtos;
using Flirty.Domain;
using Flirty.Persistence;
using Flirty.Tests.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Flirty.Tests.AspNetCore;

/// <summary>
/// Integrationstests für <c>MapFlirtyEndpoints</c> (#35): fahren die vier Endpunkte über einen
/// In-Process-<see cref="TestServer"/> mit echten HTTP-Aufrufen gegen eine SQLite-in-memory-Datenbank
/// (Docker-frei). Geprüft werden der Happy-Path (Start/Answer/Resume/Edit inkl. End-to-End-Abschluss)
/// sowie das Fehler-Mapping der Engine-Ausnahmen auf HTTP-Statuscodes (404/400/409).
/// </summary>
public sealed class MapFlirtyEndpointsTests
{
    // ---- Happy-Path ----

    /// <summary>Ein Neu-Start liefert 201 mit Location-Header, neuer Session und der ersten Frage.</summary>
    [Fact]
    public async Task Start_liefert_201_mit_Session_und_erster_Frage()
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

    /// <summary>Eine Antwort auf die offene Frage liefert 200 und schaltet auf die Folgefrage weiter.</summary>
    [Fact]
    public async Task Answer_schaltet_zur_naechsten_Frage()
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

    /// <summary>Der End-to-End-Durchlauf (Start -> dev -> Freitext) schließt den Dialog ab.</summary>
    [Fact]
    public async Task Antwort_auf_terminale_Frage_schliesst_den_Dialog_ab()
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

    /// <summary>Das Lesen des Zustands liefert 200 mit Status, aktueller Frage und bisherigen Antworten.</summary>
    [Fact]
    public async Task Resume_liefert_Status_aktuelle_Frage_und_Antworten()
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

    /// <summary>Das Editieren einer früheren Antwort berechnet den Pfad neu und meldet verworfene Antworten.</summary>
    [Fact]
    public async Task Edit_berechnet_Pfad_neu_und_meldet_invalidierte_Antworten()
    {
        await using var host = await StartBranchingHostAsync();
        var start = await StartSessionAsync(host);
        var roleQuestionId = start.CurrentQuestion.Id;
        var afterDev = await SubmitAnswerAsync(host, start.SessionId, roleQuestionId, "\"dev\"");
        await SubmitAnswerAsync(host, start.SessionId, afterDev.NextQuestion!.Id, "\"C#\"");

        // Von "dev" auf "pm" umeditieren: der dev-Zweig (devDetail-Antwort) wird verworfen, der Pfad
        // führt neu auf pmDetail.
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

    // ---- Fehlerfälle ----

    /// <summary>Der Start eines unbekannten Dialogs wird auf 404 abgebildet.</summary>
    [Fact]
    public async Task Start_mit_unbekanntem_Dialog_liefert_404()
    {
        await using var host = await StartBranchingHostAsync();

        var response = await host.Client.PostAsJsonAsync(
            "/flirty/sessions", new StartSessionRequest("gibt-es-nicht", "user-1"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>Das Lesen einer unbekannten Session wird auf 404 abgebildet.</summary>
    [Fact]
    public async Task Resume_einer_unbekannten_Session_liefert_404()
    {
        await using var host = await StartBranchingHostAsync();

        var response = await host.Client.GetAsync($"/flirty/sessions/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>Eine Antwort auf eine nicht (mehr) offene Frage wird auf 409 abgebildet.</summary>
    [Fact]
    public async Task Answer_auf_nicht_offene_Frage_liefert_409()
    {
        await using var host = await StartBranchingHostAsync();
        var start = await StartSessionAsync(host);
        var roleQuestionId = start.CurrentQuestion.Id;
        await SubmitAnswerAsync(host, start.SessionId, roleQuestionId, "\"dev\"");

        // Die Startfrage ist nach dem Weiterschalten nicht mehr die aktuell offene Frage.
        var response = await host.Client.PostAsJsonAsync(
            $"/flirty/sessions/{start.SessionId}/answers",
            new SubmitAnswerRequest(roleQuestionId, "\"dev\""));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    /// <summary>Ein fehlender Pflichtwert (DialogKey) wird über die Pipeline-Validierung auf 400 abgebildet.</summary>
    [Fact]
    public async Task Start_ohne_DialogKey_liefert_400()
    {
        await using var host = await StartBranchingHostAsync();

        // dialogKey wird bewusst weggelassen -> [Required] im StartDialogCommand schlägt an.
        var response = await host.Client.PostAsJsonAsync(
            "/flirty/sessions", new { externalUserKey = "user-1" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- Infrastruktur ----

    /// <summary>Startet einen TestServer, der den Branching-Dialog (#26) geseedet hat.</summary>
    private static Task<FlirtyTestHost> StartBranchingHostAsync()
        => StartHostAsync(context =>
            context.Dialogs.Add(TestDialogFactory.BuildBranchingDialog(Guid.NewGuid(), out _)));

    /// <summary>Startet einen In-Process-TestServer mit dem vollständigen Flirty-Stack (SQLite in-memory).</summary>
    private static async Task<FlirtyTestHost> StartHostAsync(Action<FlirtyDbContext> seed)
    {
        var connectionString = $"Data Source=FlirtyApiTest-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        var keepAlive = new SqliteConnection(connectionString);
        await keepAlive.OpenAsync();

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddLogging();
        builder.Services.AddFlirty(options => options.UseSqlite(connectionString));

        var app = builder.Build();
        app.MapFlirtyEndpoints("/flirty");
        await app.StartAsync();

        using (var scope = app.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FlirtyDbContext>();
            await context.Database.EnsureCreatedAsync();
            seed(context);
            await context.SaveChangesAsync();
        }

        return new FlirtyTestHost(app, keepAlive);
    }

    /// <summary>Startet über den Endpunkt eine Session und gibt die Antwort zurück.</summary>
    private static async Task<StartSessionResponse> StartSessionAsync(FlirtyTestHost host)
    {
        var response = await host.Client.PostAsJsonAsync(
            "/flirty/sessions", new StartSessionRequest("branching", "user-1"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<StartSessionResponse>())!;
    }

    /// <summary>Reicht über den Endpunkt eine Antwort ein und gibt die Antwort zurück.</summary>
    private static async Task<SubmitAnswerResponse> SubmitAnswerAsync(
        FlirtyTestHost host, Guid sessionId, Guid questionId, string value)
    {
        var response = await host.Client.PostAsJsonAsync(
            $"/flirty/sessions/{sessionId}/answers", new SubmitAnswerRequest(questionId, value));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SubmitAnswerResponse>())!;
    }

    /// <summary>
    /// Hält den TestServer samt der keep-alive-Verbindung, die die SQLite-in-memory-Datenbank (Shared
    /// Cache) über alle Request-Scopes am Leben hält, und räumt beide beim Verwerfen auf.
    /// </summary>
    private sealed class FlirtyTestHost : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly SqliteConnection _keepAlive;

        public FlirtyTestHost(WebApplication app, SqliteConnection keepAlive)
        {
            _app = app;
            _keepAlive = keepAlive;
            Client = app.GetTestClient();
        }

        /// <summary>Der an den TestServer gebundene HTTP-Client.</summary>
        public HttpClient Client { get; }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.DisposeAsync();
            await _keepAlive.DisposeAsync();
        }
    }
}
