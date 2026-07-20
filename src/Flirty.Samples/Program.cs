using Flirty.Persistence;
using Flirty.Runtime;
using Flirty.Samples;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

// Reines Console-Single-Project-Setup: nur der Flirty-Core, KEIN ASP.NET.
// Persistenz: SQLite in-memory (Shared-Cache). Solange die keep-alive-Verbindung offen ist, teilen
// sich alle DI-erzeugten FlirtyDbContext-Instanzen dieselbe in-memory-Datenbank.
const string connectionString = "Data Source=FlirtySampleConsole;Mode=Memory;Cache=Shared";

using var keepAlive = new SqliteConnection(connectionString);
keepAlive.Open();

using var provider = new ServiceCollection()
    .AddLogging()
    .AddFlirty(options => options.UseSqlite(connectionString))
    // Ziel-Writer für den eigenen Notification-Handler (in der App: die Konsole).
    .AddSingleton<TextWriter>(Console.Out)
    // Eigener In-Process-Handler: reagiert auf die von der Engine publizierte Abschluss-Notification.
    .AddFlirtyHandler<DialogCompletedNotification, ConsoleDialogCompletedHandler>()
    .BuildServiceProvider();

// Beispiel-Dialog seeden – programmatisch über den DbContext (ohne Designer).
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

// Dialog über die Facade durchspielen; die Engine publiziert beim Abschluss selbst die Notification,
// wodurch der oben registrierte eigene Handler ausgelöst wird.
using (var runScope = provider.CreateScope())
{
    var engine = runScope.ServiceProvider.GetRequiredService<IFlirtyEngine>();

    var runner = new ConsoleDialogRunner(engine, new ConsoleAnswerSource(), Console.Out);
    var result = await runner.RunAsync(SampleDialogFactory.DialogKey, "console-user");

    Console.WriteLine();
    Console.WriteLine(result.Completed ? "Dialog abgeschlossen." : "Dialog nicht abgeschlossen.");
}
