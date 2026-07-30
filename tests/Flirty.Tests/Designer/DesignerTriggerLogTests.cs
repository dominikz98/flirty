using Flirty.Designer.Services;
using Flirty.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Flirty.Tests.Designer;

/// <summary>
/// Tests for the test runner's trigger log (#43). The core check is the scope handover: the
/// <see cref="FlirtyRuntimeGateway"/> runs every engine step in a <b>fresh</b> DI scope, in which the
/// notification handlers are constructed as well. Without <see cref="DesignerTriggerLog.Adopt"/> they
/// would write into a throwaway instance and the runner would permanently show an empty log.
/// </summary>
public sealed class DesignerTriggerLogTests
{
    /// <summary>
    /// A run over the gateway lands in the calling circuit's log – across all child scopes, in
    /// publication order.
    /// </summary>
    [Fact]
    public async Task The_run_lands_in_the_log_of_the_circuit()
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

            // The start reports in – out of an already closed child scope.
            var startEntry = Assert.Single(log.Entries);
            Assert.Equal(TriggerScope.OnDialogStarted, startEntry.Scope);
            Assert.Equal(graph.PositionQuestionId, startEntry.QuestionId);

            var sessionId = started.Value!.SessionId;
            var answered = await runtime.ExecuteAsync((engine, token) =>
                engine.SubmitAnswerAsync(sessionId, graph.PositionQuestionId, "\"Developer\"", token));
            Assert.True(answered.Success, answered.Error);

            // One answer publishes AfterAnswer and AfterQuestion – in that order.
            Assert.Equal(
                [TriggerScope.OnDialogStarted, TriggerScope.AfterAnswer, TriggerScope.AfterQuestion],
                log.Entries.Select(entry => entry.Scope));
            Assert.All(
                log.Entries.Skip(1),
                entry => Assert.Equal(graph.PositionQuestionId, entry.QuestionId));
        });
    }

    /// <summary>The dialog's completion is logged as a point in time of its own.</summary>
    [Fact]
    public async Task The_completion_is_logged()
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
                (graph.PositionQuestionId, "\"Developer\""),
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
    /// "Start a new run" empties the log, so that the events of two runs do not appear mixed
    /// together.
    /// </summary>
    [Fact]
    public async Task Clear_empties_the_log_for_the_next_run()
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

            // After the clear the scope handover has to keep working: Clear() swaps the list out.
            _ = await runtime.ExecuteAsync(
                (engine, token) => engine.StartDialogVersionAsync(graph.DialogId, "designer-test-2", token));

            Assert.Single(log.Entries);
        });
    }

    /// <summary>Admin operations publish no runtime notifications and leave the log empty.</summary>
    [Fact]
    public async Task Admin_operations_log_nothing()
    {
        await DesignerTestHost.RunWithTempDbAsync(async (services, profile) =>
        {
            services.GetRequiredService<ActiveConnectionProfile>().Activate(profile);

            _ = await DesignerTestHost.ArrangeLoopDialogAsync(services.GetRequiredService<FlirtyAdminGateway>());

            Assert.Empty(services.GetRequiredService<DesignerTriggerLog>().Entries);
        });
    }
}
