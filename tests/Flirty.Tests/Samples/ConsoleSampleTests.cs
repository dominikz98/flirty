using Flirty.Persistence;
using Flirty.Runtime;
using Flirty.Samples;
using Mediator;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Flirty.Tests.Samples;

/// <summary>
/// Checks the console single-project sample (#44) end-to-end: a pure console setup without ASP.NET,
/// a programmatically seeded dialog is played through via the facade (incl. branching), and the
/// host's own <see cref="INotificationHandler{TNotification}"/> reacts to the completion notification
/// published by the engine (since #31).
/// </summary>
public sealed class ConsoleSampleTests
{
    /// <summary>
    /// The sample runner plays the dev branch through and completes the dialog; on completion the
    /// engine publishes the notification, so the host's own handler writes the completion summary
    /// (proof that <c>Publish</c> triggered it).
    /// </summary>
    [Fact]
    public async Task Sample_plays_the_dialog_through_and_fires_the_hosts_own_NotificationHandler()
    {
        // Shared-cache in-memory: as long as the keep-alive connection stays open, all
        // DI-created FlirtyDbContext instances share the same in-memory database.
        const string connectionString = "Data Source=FlirtyConsoleSampleTest;Mode=Memory;Cache=Shared";
        using var keepAlive = new SqliteConnection(connectionString);
        keepAlive.Open();

        var handlerOutput = new StringWriter();

        using var provider = new ServiceCollection()
            .AddLogging()
            .AddFlirty(options => options.UseSqlite(connectionString))
            .AddSingleton<TextWriter>(handlerOutput)
            .AddFlirtyHandler<DialogCompletedNotification, ConsoleDialogCompletedHandler>()
            .BuildServiceProvider();

        using (var seedScope = provider.CreateScope())
        {
            var context = seedScope.ServiceProvider.GetRequiredService<FlirtyDbContext>();
            context.Database.EnsureCreated();
            context.Dialogs.Add(SampleDialogFactory.BuildOnboardingDialog());
            context.SaveChanges();
        }

        DialogRunResult result;
        using (var runScope = provider.CreateScope())
        {
            var engine = runScope.ServiceProvider.GetRequiredService<IFlirtyEngine>();

            var answers = new ScriptedAnswerSource(new Dictionary<string, string>
            {
                ["role"] = "dev",
                ["language"] = "C#",
            });

            var runner = new ConsoleDialogRunner(engine, answers, TextWriter.Null);
            result = await runner.RunAsync(SampleDialogFactory.DialogKey, "test-user");
        }

        // Dialog completed.
        Assert.True(result.Completed);

        // Branching took the dev path: after 'role' the question asked was 'language', not 'product'.
        Assert.Equal(new[] { "role", "language" }, result.AskedQuestionKeys);

        // Proof that the host's own INotificationHandler was triggered via Publish.
        var output = handlerOutput.ToString();
        Assert.Contains("Dialog 'onboarding' completed", output);
        Assert.Contains("role = \"dev\"", output);
        Assert.Contains("language = \"C#\"", output);
    }
}
