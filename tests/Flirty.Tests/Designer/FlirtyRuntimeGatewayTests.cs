using Flirty.Designer.Services;
using Flirty.Domain;
using Flirty.Runtime;
using Flirty.Runtime.Admin;
using Microsoft.Extensions.DependencyInjection;

namespace Flirty.Tests.Designer;

/// <summary>
/// Tests für das <see cref="FlirtyRuntimeGateway"/> des Test-Runners (#43): der Dialog-Durchlauf über
/// <see cref="IFlirtyEngine"/> gegen das aktive Connection-Profil und das Fehler-Mapping auf anzeigbare
/// Meldungen.
/// </summary>
/// <remarks>
/// Kernprobe ist <see cref="Ein_Entwurf_mit_Schleife_laesst_sich_end_to_end_durchspielen"/> – das
/// Akzeptanzkriterium des Issues in Testform: Dialog samt Schleife über die Admin-Commands anlegen und
/// <b>ohne Veröffentlichung</b> mit zwei Iterationen durchspielen.
/// </remarks>
public sealed class FlirtyRuntimeGatewayTests
{
    /// <summary>
    /// Der End-to-End-Durchstich: zwei Iterationen der Schleife, Ausstieg, Abschluss – und die
    /// gesammelten Antworten tragen die erwarteten Iterationsindizes. Der Dialog bleibt dabei ein
    /// <b>Entwurf</b>; ohne <c>StartDialogVersionCommand</c> wäre das nicht möglich.
    /// </summary>
    [Fact]
    public async Task Ein_Entwurf_mit_Schleife_laesst_sich_end_to_end_durchspielen()
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

            // Iteration 1: Position erfassen, „weitere?" mit ja beantworten -> Rücksprung.
            await SubmitAsync(runtime, sessionId, graph.PositionQuestionId, "\"Entwickler\"", "more");
            await SubmitAsync(runtime, sessionId, graph.MoreQuestionId, "\"yes\"", "position");

            // Iteration 2: zweite Position, dann aussteigen.
            await SubmitAsync(runtime, sessionId, graph.PositionQuestionId, "\"Architekt\"", "more");
            await SubmitAsync(runtime, sessionId, graph.MoreQuestionId, "\"no\"", "summary");

            var finished = await runtime.ExecuteAsync((engine, token) =>
                engine.SubmitAnswerAsync(sessionId, graph.SummaryQuestionId, "\"fertig\"", token));
            Assert.True(finished.Success, finished.Error);
            Assert.True(finished.Value!.IsCompleted);

            var state = await runtime.ExecuteAsync(
                (engine, token) => engine.ResumeDialogAsync(sessionId, token));
            Assert.True(state.Success, state.Error);
            Assert.Equal(SessionStatus.Completed, state.Value!.Status);
            Assert.Null(state.Value.CurrentQuestion);

            // Die beiden Positions-Antworten liegen in derselben Schleifeninstanz, aber in Iteration 0/1 –
            // genau das zeigt der Verlauf des Runners als „Iteration 1/2".
            var positions = state.Value.Answers
                .Where(answer => answer.QuestionKey == "position")
                .OrderBy(answer => answer.Sequence)
                .ToList();

            Assert.Equal(["\"Entwickler\"", "\"Architekt\""], positions.Select(answer => answer.Value));
            Assert.Equal([0, 1], positions.Select(answer => answer.IterationIndex));
            Assert.Single(positions.Select(answer => answer.LoopInstanceId).Distinct());

            // Die Antwort außerhalb der Schleife trägt bewusst keine Loop-Zuordnung.
            var summary = Assert.Single(state.Value.Answers, answer => answer.QuestionKey == "summary");
            Assert.Null(summary.IterationIndex);
            Assert.Null(summary.LoopInstanceId);
        });
    }

    /// <summary>
    /// Das Editieren einer Iterations-Antwort trifft genau die angegebene Iteration und verwirft die
    /// nachgelagerten Antworten – die Grundlage der Erfolgsmeldung im Runner.
    /// </summary>
    [Fact]
    public async Task Das_Editieren_trifft_die_angegebene_Iteration()
    {
        await RunAsync(async (admin, runtime, _) =>
        {
            var graph = await DesignerTestHost.ArrangeLoopDialogAsync(admin);

            var started = await runtime.ExecuteAsync(
                (engine, token) => engine.StartDialogVersionAsync(graph.DialogId, "designer-test-1", token));
            Assert.True(started.Success, started.Error);
            var sessionId = started.Value!.SessionId;

            await SubmitAsync(runtime, sessionId, graph.PositionQuestionId, "\"Entwickler\"", "more");
            await SubmitAsync(runtime, sessionId, graph.MoreQuestionId, "\"yes\"", "position");
            await SubmitAsync(runtime, sessionId, graph.PositionQuestionId, "\"Architekt\"", "more");

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

            Assert.Equal(["\"Entwickler\"", "\"Tester\""], positions);
        });
    }

    // ---- Fehler-Mapping ----------------------------------------------------------------------

    /// <summary>
    /// Eine abgelehnte Antwort darf den Blazor-Circuit nicht reißen. Gemeldet werden die Einzelverstöße
    /// der Engine – ohne die rohe Frage-GUID, die die <c>Message</c> der Ausnahme mitführt.
    /// </summary>
    [Fact]
    public async Task Meldet_eine_ungueltige_Antwort_ohne_technische_Bezeichner()
    {
        await RunAsync(async (admin, runtime, _) =>
        {
            var graph = await DesignerTestHost.ArrangeLoopDialogAsync(admin);

            var started = await runtime.ExecuteAsync(
                (engine, token) => engine.StartDialogVersionAsync(graph.DialogId, "designer-test-1", token));
            Assert.True(started.Success, started.Error);
            var sessionId = started.Value!.SessionId;

            await SubmitAsync(runtime, sessionId, graph.PositionQuestionId, "\"Entwickler\"", "more");

            // "vielleicht" ist keine konfigurierte Option der Frage "more".
            var rejected = await runtime.ExecuteAsync((engine, token) =>
                engine.SubmitAnswerAsync(sessionId, graph.MoreQuestionId, "\"vielleicht\"", token));

            Assert.False(rejected.Success);
            Assert.StartsWith("Antwort ungültig:", rejected.Error, StringComparison.Ordinal);
            Assert.Contains("vielleicht", rejected.Error, StringComparison.Ordinal);
            Assert.DoesNotContain(graph.MoreQuestionId.ToString(), rejected.Error, StringComparison.Ordinal);
        });
    }

    /// <summary>Eine unbekannte Session wird als Meldung gezeigt, nicht als Ausnahme geworfen.</summary>
    [Fact]
    public async Task Meldet_eine_unbekannte_Session()
    {
        await RunAsync(async (_, runtime, _) =>
        {
            var result = await runtime.ExecuteAsync(
                (engine, token) => engine.ResumeDialogAsync(Guid.NewGuid(), token));

            Assert.False(result.Success);
            Assert.Contains("Session", result.Error, StringComparison.Ordinal);
        });
    }

    /// <summary>Eine unbekannte Dialogversion wird als Meldung gezeigt.</summary>
    [Fact]
    public async Task Meldet_eine_unbekannte_Dialogversion()
    {
        await RunAsync(async (_, runtime, _) =>
        {
            var result = await runtime.ExecuteAsync((engine, token) =>
                engine.StartDialogVersionAsync(Guid.NewGuid(), "designer-test-1", token));

            Assert.False(result.Success);
            Assert.Contains("Dialog", result.Error, StringComparison.Ordinal);
        });
    }

    /// <summary>Ohne aktives Connection-Profil meldet die Kontext-Factory verständlich.</summary>
    [Fact]
    public async Task Meldet_ein_fehlendes_Connection_Profil()
    {
        await DesignerTestHost.RunWithTempDbAsync(async (services, _) =>
        {
            // Bewusst KEIN Activate.
            var result = await services.GetRequiredService<FlirtyRuntimeGateway>().ExecuteAsync(
                (engine, token) => engine.StartDialogVersionAsync(Guid.NewGuid(), "designer-test-1", token));

            Assert.False(result.Success);
            Assert.Contains("Verbindungen", result.Error, StringComparison.Ordinal);
        });
    }

    // ---- Testaufbau --------------------------------------------------------------------------

    /// <summary>
    /// Führt den Testkörper mit aktiviertem Profil und beiden Gateways aus.
    /// </summary>
    /// <param name="test">Der Testkörper (Admin-Gateway, Runtime-Gateway, Trigger-Protokoll).</param>
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

    /// <summary>Reicht eine Antwort ein und prüft, welche Frage danach offen ist.</summary>
    /// <param name="runtime">Das Runtime-Gateway.</param>
    /// <param name="sessionId">Die laufende Session.</param>
    /// <param name="questionId">Die zu beantwortende Frage.</param>
    /// <param name="value">Der rohe JSON-Antwortwert.</param>
    /// <param name="expectedNextKey">Der Schlüssel der erwarteten Folgefrage.</param>
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
