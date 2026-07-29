using Flirty.Persistence;
using Flirty.Runtime;
using Flirty.Samples;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

// Pure console single-project setup: only the Flirty core, NO ASP.NET.
// Persistence: SQLite in-memory (shared cache). As long as the keep-alive connection is open, all
// DI-created FlirtyDbContext instances share the same in-memory database.
const string connectionString = "Data Source=FlirtySampleConsole;Mode=Memory;Cache=Shared";

using var keepAlive = new SqliteConnection(connectionString);
keepAlive.Open();

using var provider = new ServiceCollection()
    .AddLogging()
    .AddFlirty(options => options.UseSqlite(connectionString))
    // Target writer for the custom notification handler (in the app: the console).
    .AddSingleton<TextWriter>(Console.Out)
    // Custom in-process handler: reacts to the completion notification published by the engine.
    .AddFlirtyHandler<DialogCompletedNotification, ConsoleDialogCompletedHandler>()
    .BuildServiceProvider();

// Seed the sample dialog – programmatically via the DbContext (without the designer).
using (var seedScope = provider.CreateScope())
{
    var context = seedScope.ServiceProvider.GetRequiredService<FlirtyDbContext>();
    context.Database.EnsureCreated();
    context.Dialogs.Add(SampleDialogFactory.BuildOnboardingDialog());
    context.SaveChanges();
}

Console.WriteLine("=== Flirty Console-Sample ===");
Console.WriteLine("Beantworte die Fragen. Bei Auswahlfragen den Schlüssel in [] eingeben.");
Console.WriteLine();

// Play the dialog through via the facade; on completion the engine itself publishes the notification,
// which triggers the custom handler registered above.
using (var runScope = provider.CreateScope())
{
    var engine = runScope.ServiceProvider.GetRequiredService<IFlirtyEngine>();

    var runner = new ConsoleDialogRunner(engine, new ConsoleAnswerSource(), Console.Out);
    var result = await runner.RunAsync(SampleDialogFactory.DialogKey, "console-user");

    Console.WriteLine();
    Console.WriteLine(result.Completed ? "Dialog abgeschlossen." : "Dialog nicht abgeschlossen.");
}
