# Getting Started – Console (Single-Project)

> Stand: Issue #44. Dieser Guide zeigt, wie man die Flirty-Engine in einer reinen Console-App nutzt –
> **nur der Core** (`src/Flirty`), **kein ASP.NET**. Ein Dialog wird programmatisch geseedet, über die
> Facade `IFlirtyEngine` durchgespielt und der Abschluss löst einen **eigenen `INotificationHandler`**
> aus. Der lauffähige Code liegt unter [`src/Flirty.Samples`](../src/Flirty.Samples).

## Projekt-Setup

Ein Console-Single-Project braucht nur eine Referenz auf den Core plus die konkreten
`Microsoft.Extensions.*`-Implementierungen für den DI-Container und Logging (der Core liefert davon
nur die `*.Abstractions`):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <!-- EF Core, SQLite-Provider, Mediator.Abstractions, DynamicExpresso kommen transitiv. -->
    <ProjectReference Include="..\Flirty\Flirty.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" />
    <PackageReference Include="Microsoft.Extensions.Logging" />
  </ItemGroup>
</Project>
```

Kein `FrameworkReference Microsoft.AspNetCore.App`, kein `Flirty.AspNetCore` – der Core ist bewusst
ASP.NET-frei (siehe [ARCHITECTURE.md](./ARCHITECTURE.md)).

## 1. Registrierung (DI ohne Host)

`AddFlirty(o => …)` verdrahtet den kompletten Stack (Mediator, Runtime-Facade, Persistenz,
Expression-Engine, Validierung). Für die Persistenz genügt hier **SQLite in-memory** (Shared-Cache):
solange eine keep-alive-Verbindung offen bleibt, teilen sich alle DI-erzeugten
`FlirtyDbContext`-Instanzen dieselbe in-memory-Datenbank.

```csharp
const string connectionString = "Data Source=FlirtySampleConsole;Mode=Memory;Cache=Shared";

using var keepAlive = new SqliteConnection(connectionString);
keepAlive.Open();

using var provider = new ServiceCollection()
    .AddLogging()
    .AddFlirty(options => options.UseSqlite(connectionString))
    .AddSingleton<TextWriter>(Console.Out)                       // Ziel für den eigenen Handler
    .AddFlirtyHandler<DialogCompletedNotification, ConsoleDialogCompletedHandler>()
    .BuildServiceProvider();
```

> Für eine **dateibasierte** DB (`o.UseSqlite("Data Source=flirty.db")`) mit Auto-Migration
> (`o.ApplyMigrations()`) wird ein Generic Host benötigt, weil `ApplyMigrations()` einen
> `IHostedService` registriert. Für das Single-Project-Sample reicht der in-memory-Weg mit
> `context.Database.EnsureCreated()` (siehe unten).

## 2. Dialog programmatisch seeden (ohne Designer)

Ohne den Blazor-Designer legt man den Dialog direkt über den `FlirtyDbContext` an. Damit
`StartDialogAsync(dialogKey, …)` ihn findet, muss der Dialog **veröffentlicht** sein
(`IsPublished = true`) und eine `StartQuestionId` besitzen:

```csharp
using (var seedScope = provider.CreateScope())
{
    var context = seedScope.ServiceProvider.GetRequiredService<FlirtyDbContext>();
    context.Database.EnsureCreated();                            // Schema bei in-memory ohne Migrationen
    context.Dialogs.Add(SampleDialogFactory.BuildOnboardingDialog());
    context.SaveChanges();
}
```

`SampleDialogFactory` baut ein Branching-Beispiel: die Startfrage `role` (SingleChoice `dev`/`pm`)
verzweigt per `role == "dev"` auf die Freitext-Frage `language`, sonst per Default auf `product`;
beide Detailfragen sind terminal und schließen den Dialog ab.

## 3. Dialog über die Facade durchspielen

`IFlirtyEngine` kapselt die Runtime-Commands. Antwortwerte werden als **roher JSON-Text** übergeben
(Format je Fragetyp, z. B. `"dev"` für eine Auswahl, `"C#"` für Freitext):

```csharp
var start = await engine.StartDialogAsync("onboarding", "console-user");
var current = start.CurrentQuestion;

while (true)
{
    // current.Text + current.Options anzeigen, Antwort einlesen …
    var result = await engine.SubmitAnswerAsync(start.SessionId, current.Id, value);
    if (result.IsCompleted || result.NextQuestion is null)
        break;
    current = result.NextQuestion;
}
```

Der bisherige Verlauf lässt sich jederzeit rein lesend abrufen (z. B. nach einem Reload):

```csharp
var state = await engine.ResumeDialogAsync(start.SessionId);
// state.Status, state.CurrentQuestion, state.Answers
```

Im Sample kapselt `ConsoleDialogRunner` diese Schleife und trennt Ein-/Ausgabe über die
`IAnswerSource`-Abstraktion (`ConsoleAnswerSource` liest interaktiv von der Konsole,
`ScriptedAnswerSource` liefert feste Antworten für Tests).

## 4. Eigener `INotificationHandler` (In-Process-Rückkanal)

Der Rückkanal ist ein `Mediator.INotificationHandler<T>`. Im Sample reagiert
`ConsoleDialogCompletedHandler` auf eine `DialogCompletedNotification` und schreibt eine
Zusammenfassung:

```csharp
public sealed class ConsoleDialogCompletedHandler : INotificationHandler<DialogCompletedNotification>
{
    private readonly TextWriter _output;
    public ConsoleDialogCompletedHandler(TextWriter output) => _output = output;

    public ValueTask Handle(DialogCompletedNotification notification, CancellationToken ct)
    {
        _output.WriteLine($"Dialog '{notification.DialogKey}' abgeschlossen …");
        return ValueTask.CompletedTask;
    }
}
```

Seit **#31** publiziert die **Engine selbst** die Notification: Der `SubmitAnswerCommandHandler` löst beim
Dialog-Abschluss `DialogCompletedNotification` per `IPublisher` aus, wodurch alle registrierten
`INotificationHandler<T>` automatisch aufgerufen werden. Die Registrierung per DI (Abschnitt 1) genügt –
komfortabel über den Helper `AddFlirtyHandler<DialogCompletedNotification, ConsoleDialogCompletedHandler>()`
(seit #32); der `ConsoleDialogRunner` muss **nichts** mehr manuell auflösen oder aufrufen. Der Handler
selbst bleibt unverändert.

Der `DialogCompletedNotification` gehört – wie alle vier Trigger-Contracts (`DialogStarted`,
`AnswerSubmitted`, `QuestionAnswered`, `DialogCompleted`) – zum **Core** (Namespace `Flirty.Runtime`),
weil der martinothamar-Mediator Notification-Typen nur innerhalb der Core-Compilation kennt. Details und
das vollständige Auslöse-/Scope-Mapping stehen in [TRIGGERS.md](./TRIGGERS.md).

## Ausführen

```pwsh
dotnet run --project src/Flirty.Samples
```

Beispielausgabe (dev-Zweig):

```text
=== Flirty Console-Sample ===
Welche Rolle hast du?
  [dev] Entwickler
  [pm] Product Manager
Welche Programmiersprache nutzt du am liebsten?
[Handler] Dialog 'onboarding' abgeschlossen (Session …).
[Handler]   role = "dev"
[Handler]   language = "C#"

Dialog abgeschlossen.
```

Der end-to-end-Durchlauf (inkl. Branching und Handler-Auslösung) ist als Test abgesichert:
`tests/Flirty.Tests/Samples/ConsoleSampleTests.cs`.
