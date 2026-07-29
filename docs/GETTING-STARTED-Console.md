# Getting Started – Console (Single-Project)

> As of: issue #44. This guide shows how to use the Flirty engine in a pure console app –
> **the core only** (`src/Flirty`), **no ASP.NET**. A dialog is seeded programmatically, played
> through via the facade `IFlirtyEngine`, and completion fires a **custom `INotificationHandler`**.
> The runnable code lives under [`src/Flirty.Samples`](../src/Flirty.Samples).

## Project setup

A console single-project needs only a reference to the core plus the concrete
`Microsoft.Extensions.*` implementations for the DI container and logging (the core ships only the
`*.Abstractions` of those):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <!-- EF Core, the SQLite provider, Mediator.Abstractions, DynamicExpresso come transitively. -->
    <ProjectReference Include="..\Flirty\Flirty.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" />
    <PackageReference Include="Microsoft.Extensions.Logging" />
  </ItemGroup>
</Project>
```

No `FrameworkReference Microsoft.AspNetCore.App`, no `Flirty.AspNetCore` – the core is deliberately
ASP.NET-free (see [ARCHITECTURE.md](./ARCHITECTURE.md)).

## 1. Registration (DI without a host)

`AddFlirty(o => …)` wires up the complete stack (Mediator, runtime facade, persistence,
expression engine, validation). For persistence, **SQLite in-memory** (shared cache) is enough here:
as long as one keep-alive connection stays open, all DI-created `FlirtyDbContext` instances share the
same in-memory database.

```csharp
const string connectionString = "Data Source=FlirtySampleConsole;Mode=Memory;Cache=Shared";

using var keepAlive = new SqliteConnection(connectionString);
keepAlive.Open();

using var provider = new ServiceCollection()
    .AddLogging()
    .AddFlirty(options => options.UseSqlite(connectionString))
    .AddSingleton<TextWriter>(Console.Out)                       // target for the custom handler
    .AddFlirtyHandler<DialogCompletedNotification, ConsoleDialogCompletedHandler>()
    .BuildServiceProvider();
```

> For a **file-based** DB (`o.UseSqlite("Data Source=flirty.db")`) with auto-migration
> (`o.ApplyMigrations()`), a Generic Host is required, because `ApplyMigrations()` registers an
> `IHostedService`. For the single-project sample, the in-memory route with
> `context.Database.EnsureCreated()` is enough (see below).

## 2. Seed a dialog programmatically (without the designer)

Without the Blazor designer, you create the dialog directly via the `FlirtyDbContext`. For
`StartDialogAsync(dialogKey, …)` to find it, the dialog must be **published**
(`IsPublished = true`) and have a `StartQuestionId`:

```csharp
using (var seedScope = provider.CreateScope())
{
    var context = seedScope.ServiceProvider.GetRequiredService<FlirtyDbContext>();
    context.Database.EnsureCreated();                            // schema for in-memory without migrations
    context.Dialogs.Add(SampleDialogFactory.BuildOnboardingDialog());
    context.SaveChanges();
}
```

`SampleDialogFactory` builds a branching example: the entry question `role` (SingleChoice `dev`/`pm`)
branches via `role == "dev"` to the free-text question `language`, otherwise by default to `product`;
both detail questions are terminal and complete the dialog.

## 3. Play the dialog through via the facade

`IFlirtyEngine` encapsulates the runtime commands. Answer values are passed as **raw JSON text**
(the format depends on the question type, e.g. `"dev"` for a choice, `"C#"` for free text):

```csharp
var start = await engine.StartDialogAsync("onboarding", "console-user");
var current = start.CurrentQuestion;

while (true)
{
    // display current.Text + current.Options, read the answer …
    var result = await engine.SubmitAnswerAsync(start.SessionId, current.Id, value);
    if (result.IsCompleted || result.NextQuestion is null)
        break;
    current = result.NextQuestion;
}
```

The history so far can be retrieved read-only at any time (e.g. after a reload):

```csharp
var state = await engine.ResumeDialogAsync(start.SessionId);
// state.Status, state.CurrentQuestion, state.Answers
```

In the sample, `ConsoleDialogRunner` encapsulates this loop and separates input/output via the
`IAnswerSource` abstraction (`ConsoleAnswerSource` reads interactively from the console,
`ScriptedAnswerSource` supplies fixed answers for tests).

> **The value format is the host app's concern.** The engine expects raw JSON text; what the user
> types is not. In the sample, `AnswerEncoder` handles the conversion per `QuestionType`: free text and
> choice are **quoted** (`Dev` → `"Dev"`), `MultiChoice` becomes a JSON array, `Number` an
> **invariant** numeric literal (decimal point, no comma) and `Boolean` becomes `true`/`false` (also from
> "yes"/"y"/"1"). If the value does not match the type or the question's `ValidationRules`,
> `SubmitAnswerAsync` throws an `AnswerValidationException` – see [VALIDATION.md](./VALIDATION.md).

## 4. A custom `INotificationHandler` (in-process back channel)

The back channel is a `Mediator.INotificationHandler<T>`. In the sample,
`ConsoleDialogCompletedHandler` reacts to a `DialogCompletedNotification` and writes a
summary:

```csharp
public sealed class ConsoleDialogCompletedHandler : INotificationHandler<DialogCompletedNotification>
{
    private readonly TextWriter _output;
    public ConsoleDialogCompletedHandler(TextWriter output) => _output = output;

    public ValueTask Handle(DialogCompletedNotification notification, CancellationToken ct)
    {
        _output.WriteLine($"Dialog '{notification.DialogKey}' completed …");
        return ValueTask.CompletedTask;
    }
}
```

Since **#31** the **engine itself** publishes the notification: on dialog completion the
`SubmitAnswerCommandHandler` fires `DialogCompletedNotification` via `IPublisher`, which automatically
invokes all registered `INotificationHandler<T>`. Registration via DI (section 1) is enough –
conveniently through the helper `AddFlirtyHandler<DialogCompletedNotification, ConsoleDialogCompletedHandler>()`
(since #32); the `ConsoleDialogRunner` no longer has to resolve or call **anything** manually. The handler
itself stays unchanged.

The `DialogCompletedNotification` belongs – like all four trigger contracts (`DialogStarted`,
`AnswerSubmitted`, `QuestionAnswered`, `DialogCompleted`) – to the **core** (namespace `Flirty.Runtime`),
because the martinothamar Mediator only knows notification types within the core compilation. Details and
the full firing/scope mapping are in [TRIGGERS.md](./TRIGGERS.md).

## Running

```pwsh
dotnet run --project src/Flirty.Samples
```

Example output (dev branch):

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

The end-to-end run (incl. branching and handler firing) is secured by a test:
`tests/Flirty.Tests/Samples/ConsoleSampleTests.cs`.
