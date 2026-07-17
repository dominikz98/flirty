using Flirty.Persistence;
using Flirty.Runtime;
using Flirty.Samples;
using Mediator;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Flirty.Tests.Samples;

/// <summary>
/// Prüft das Console-Single-Project-Sample (#44) end-to-end: reines Console-Setup ohne ASP.NET, ein
/// programmatisch geseedeter Dialog wird über die Facade durchgespielt (inkl. Branching) und der
/// eigene <see cref="INotificationHandler{TNotification}"/> reagiert auf die von der Engine (seit #31)
/// publizierte Abschluss-Notification.
/// </summary>
public sealed class ConsoleSampleTests
{
    /// <summary>
    /// Der Sample-Runner spielt den dev-Zweig durch und schließt den Dialog ab; die Engine publiziert
    /// beim Abschluss die Notification, sodass der eigene Handler die Abschluss-Zusammenfassung schreibt
    /// (Beleg, dass <c>Publish</c> ihn ausgelöst hat).
    /// </summary>
    [Fact]
    public async Task Sample_spielt_Dialog_durch_und_loest_eigenen_NotificationHandler_aus()
    {
        // Shared-Cache-in-memory: solange die keep-alive-Verbindung offen ist, teilen sich alle
        // DI-erzeugten FlirtyDbContext-Instanzen dieselbe in-memory-Datenbank.
        const string connectionString = "Data Source=FlirtyConsoleSampleTest;Mode=Memory;Cache=Shared";
        using var keepAlive = new SqliteConnection(connectionString);
        keepAlive.Open();

        var handlerOutput = new StringWriter();

        using var provider = new ServiceCollection()
            .AddLogging()
            .AddFlirty(options => options.UseSqlite(connectionString))
            .AddSingleton<TextWriter>(handlerOutput)
            .AddScoped<INotificationHandler<DialogCompletedNotification>, ConsoleDialogCompletedHandler>()
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

        // Dialog abgeschlossen.
        Assert.True(result.Completed);

        // Branching nahm den dev-Pfad: nach 'role' wurde 'language' (nicht 'product') gefragt.
        Assert.Equal(new[] { "role", "language" }, result.AskedQuestionKeys);

        // Beleg, dass der eigene INotificationHandler per Publish ausgelöst wurde.
        var output = handlerOutput.ToString();
        Assert.Contains("Dialog 'onboarding' abgeschlossen", output);
        Assert.Contains("role = \"dev\"", output);
        Assert.Contains("language = \"C#\"", output);
    }
}
