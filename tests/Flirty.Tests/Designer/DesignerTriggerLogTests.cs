using Flirty.Designer.Services;
using Flirty.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Flirty.Tests.Designer;

/// <summary>
/// Tests für das Trigger-Protokoll des Test-Runners (#43). Kernprobe ist die Scope-Übergabe: Das
/// <see cref="FlirtyRuntimeGateway"/> führt jeden Engine-Schritt in einem <b>frischen</b> DI-Scope aus,
/// in dem auch die Notification-Handler konstruiert werden. Ohne
/// <see cref="DesignerTriggerLog.Adopt"/> schrieben sie in eine Wegwerf-Instanz und der Runner zeigte
/// dauerhaft ein leeres Protokoll.
/// </summary>
public sealed class DesignerTriggerLogTests
{
    /// <summary>
    /// Ein Durchlauf über das Gateway landet im Protokoll des aufrufenden Circuits – über alle
    /// Kind-Scopes hinweg, in der Reihenfolge der Publikation.
    /// </summary>
    [Fact]
    public async Task Der_Lauf_landet_im_Protokoll_des_Circuits()
    {
        await DesignerTestHost.RunWithTempDbAsync(async (services, profile) =>
        {
            services.GetRequiredService<ActiveConnectionProfile>().Activate(profile);
            var admin = services.GetRequiredService<FlirtyAdminGateway>();
            var runtime = services.GetRequiredService<FlirtyRuntimeGateway>();
            var log = services.GetRequiredService<DesignerTriggerLog>();

            var graph = await DesignerTestHost.ArrangeLoopDialogAsync(admin);
            Assert.Empty(log.Entries);

            var started = await runtime.ExecuteAsync(
                (engine, token) => engine.StartDialogVersionAsync(graph.DialogId, "designer-test-1", token));
            Assert.True(started.Success, started.Error);

            // Der Start meldet sich – aus einem bereits geschlossenen Kind-Scope heraus.
            var startEntry = Assert.Single(log.Entries);
            Assert.Equal(TriggerScope.OnDialogStarted, startEntry.Scope);
            Assert.Equal(graph.PositionQuestionId, startEntry.QuestionId);

            var sessionId = started.Value!.SessionId;
            var answered = await runtime.ExecuteAsync((engine, token) =>
                engine.SubmitAnswerAsync(sessionId, graph.PositionQuestionId, "\"Entwickler\"", token));
            Assert.True(answered.Success, answered.Error);

            // Eine Antwort publiziert AfterAnswer und AfterQuestion – in dieser Reihenfolge.
            Assert.Equal(
                [TriggerScope.OnDialogStarted, TriggerScope.AfterAnswer, TriggerScope.AfterQuestion],
                log.Entries.Select(entry => entry.Scope));
            Assert.All(
                log.Entries.Skip(1),
                entry => Assert.Equal(graph.PositionQuestionId, entry.QuestionId));
        });
    }

    /// <summary>Der Abschluss des Dialogs wird als eigener Zeitpunkt protokolliert.</summary>
    [Fact]
    public async Task Der_Abschluss_wird_protokolliert()
    {
        await DesignerTestHost.RunWithTempDbAsync(async (services, profile) =>
        {
            services.GetRequiredService<ActiveConnectionProfile>().Activate(profile);
            var admin = services.GetRequiredService<FlirtyAdminGateway>();
            var runtime = services.GetRequiredService<FlirtyRuntimeGateway>();
            var log = services.GetRequiredService<DesignerTriggerLog>();

            var graph = await DesignerTestHost.ArrangeLoopDialogAsync(admin);

            var started = await runtime.ExecuteAsync(
                (engine, token) => engine.StartDialogVersionAsync(graph.DialogId, "designer-test-1", token));
            Assert.True(started.Success, started.Error);
            var sessionId = started.Value!.SessionId;

            foreach (var (questionId, value) in new[]
            {
                (graph.PositionQuestionId, "\"Entwickler\""),
                (graph.MoreQuestionId, "\"no\""),
                (graph.SummaryQuestionId, "\"fertig\""),
            })
            {
                var result = await runtime.ExecuteAsync(
                    (engine, token) => engine.SubmitAnswerAsync(sessionId, questionId, value, token));
                Assert.True(result.Success, result.Error);
            }

            var completion = Assert.Single(
                log.Entries, entry => entry.Scope == TriggerScope.OnDialogCompleted);

            Assert.Null(completion.QuestionId);
            Assert.Contains("3", completion.Detail, StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// „Neuen Lauf starten" leert das Protokoll, damit die Ereignisse zweier Läufe nicht vermischt
    /// erscheinen.
    /// </summary>
    [Fact]
    public async Task Clear_leert_das_Protokoll_fuer_den_naechsten_Lauf()
    {
        await DesignerTestHost.RunWithTempDbAsync(async (services, profile) =>
        {
            services.GetRequiredService<ActiveConnectionProfile>().Activate(profile);
            var admin = services.GetRequiredService<FlirtyAdminGateway>();
            var runtime = services.GetRequiredService<FlirtyRuntimeGateway>();
            var log = services.GetRequiredService<DesignerTriggerLog>();

            var graph = await DesignerTestHost.ArrangeLoopDialogAsync(admin);

            _ = await runtime.ExecuteAsync(
                (engine, token) => engine.StartDialogVersionAsync(graph.DialogId, "designer-test-1", token));
            Assert.NotEmpty(log.Entries);

            log.Clear();
            Assert.Empty(log.Entries);

            // Nach dem Leeren muss die Scope-Übergabe weiterhin greifen: Clear() tauscht die Liste aus.
            _ = await runtime.ExecuteAsync(
                (engine, token) => engine.StartDialogVersionAsync(graph.DialogId, "designer-test-2", token));

            Assert.Single(log.Entries);
        });
    }

    /// <summary>Admin-Operationen publizieren keine Laufzeit-Notifications und lassen das Protokoll leer.</summary>
    [Fact]
    public async Task Admin_Operationen_protokollieren_nichts()
    {
        await DesignerTestHost.RunWithTempDbAsync(async (services, profile) =>
        {
            services.GetRequiredService<ActiveConnectionProfile>().Activate(profile);

            _ = await DesignerTestHost.ArrangeLoopDialogAsync(services.GetRequiredService<FlirtyAdminGateway>());

            Assert.Empty(services.GetRequiredService<DesignerTriggerLog>().Entries);
        });
    }
}
