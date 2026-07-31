# Flirty

[![CI](https://github.com/dominikz98/flirty/actions/workflows/ci.yml/badge.svg)](https://github.com/dominikz98/flirty/actions/workflows/ci.yml)
[![NuGet: Flirty](https://img.shields.io/nuget/v/Flirty?label=NuGet%3A%20Flirty)](https://www.nuget.org/packages/Flirty)
[![NuGet: Flirty.AspNetCore](https://img.shields.io/nuget/v/Flirty.AspNetCore?label=NuGet%3A%20Flirty.AspNetCore)](https://www.nuget.org/packages/Flirty.AspNetCore)

Reusable **chatbot/dialog engine for .NET**. You only build the UI – Flirty handles
persistence, answer validation, **branching**, **loops**, **resume**, editable answers and
**triggers** (back channels into your app). Dialogs are configured via a **Blazor designer**
(also by non-technical users).

The core is a pure class library **without an ASP.NET dependency** and runs unchanged in
console, worker and web applications. HTTP endpoints are opt-in via the add-on package
`Flirty.AspNetCore`.

## Features

| Feature | Implementation |
|---|---|
| Resume within a dialog | The session holds the current question; resumption via the session id |
| Editing answers after the fact | `EditAnswerAsync` + recomputation of the downstream path |
| Branching (multiple branches) | Transitions with sandboxed condition expressions (default: DynamicExpresso) |
| Loops (list up to the exit question) | Branching cycle + loop marker, iterations as a collection in the context |
| Trigger after an answer / on completion | In-process notifications (Mediator) **and** outbound webhooks |
| Answer validation | Type check + `ValidationRules` per question, before the handler in the pipeline |
| Dialog versioning | Published versions are immutable; changes go through a new version, running sessions stay on theirs |
| Multi-DB + auto-migration | SQLite / PostgreSQL / SQL Server, migrations per provider |
| Simple integration | `services.AddFlirty(o => …)`, optionally `app.MapFlirtyEndpoints()` |

## Projects

| Project | Purpose |
|---|---|
| `src/Flirty` | Core engine (domain, runtime, EF Core persistence, Mediator, DI extensions). **No ASP.NET** → usable in console/worker too. NuGet package. |
| `src/Flirty.AspNetCore` | Optional WebAPI endpoints (`MapFlirtyEndpoints`, `MapFlirtyAdminEndpoints`). NuGet package. |
| `src/Flirty.Mcp` | Optional **MCP server** (`MapFlirtyMcp`): the engine operations as Model Context Protocol tools → [`docs/MCP.md`](https://github.com/dominikz98/flirty/blob/main/docs/MCP.md). NuGet package. |
| `src/Flirty.Designer` | Blazor Web App for configuring dialogs/questions/answers/branching/loops/triggers, incl. test runner. Multi-DB → [`docs/DESIGNER.md`](https://github.com/dominikz98/flirty/blob/main/docs/DESIGNER.md). |
| `src/Flirty.Migrations.*` | EF migrations per provider (SQLite, PostgreSQL, SQL Server); bundled into the `Flirty` package. |
| `src/Flirty.Samples` | Runnable **console sample** (core only, no ASP.NET) → [`docs/GETTING-STARTED-Console.md`](https://github.com/dominikz98/flirty/blob/main/docs/GETTING-STARTED-Console.md). |
| `src/Flirty.Samples.Web` | Runnable **web sample** (minimal API + static chat UI): resume/edit/branching/loop/trigger + webhook receiver, plus the MCP server at `/mcp` → [`docs/GETTING-STARTED-Sample-Web.md`](https://github.com/dominikz98/flirty/blob/main/docs/GETTING-STARTED-Sample-Web.md). |
| `tests/Flirty.Tests` | Unit/integration tests (xUnit). |
| `tests/Flirty.E2E` | Playwright E2E tests (web-sample chat UI and Blazor designer). |

## Installation

```pwsh
dotnet add package Flirty                 # Core engine (usable without ASP.NET)
dotnet add package Flirty.AspNetCore      # optional: ready-made WebAPI endpoints
dotnet add package Flirty.Mcp             # optional: MCP server (independent of the above)
```

> The target framework is **.NET 10**. The version scheme is date-based (`YYYYMM.Revision.0`, e.g.
> `202607.3.0`) – not a SemVer signal, see
> [`docs/NUGET-PACKAGING.md`](https://github.com/dominikz98/flirty/blob/main/docs/NUGET-PACKAGING.md).

## Quickstart (Console)

`AddFlirty(o => …)` wires up the complete stack (Mediator, runtime facade, persistence,
expression engine, validation). Excerpt from the console sample
([`src/Flirty.Samples/Program.cs`](https://github.com/dominikz98/flirty/blob/main/src/Flirty.Samples/Program.cs)):

```csharp
// SQLite in-memory (shared cache): as long as the keep-alive connection stays open,
// all DI-created FlirtyDbContext instances share the same database.
const string connectionString = "Data Source=FlirtyQuickstart;Mode=Memory;Cache=Shared";

using var keepAlive = new SqliteConnection(connectionString);
keepAlive.Open();

using var provider = new ServiceCollection()
    .AddLogging()
    .AddFlirty(o => o.UseSqlite(connectionString))
    // Your own back channel: the engine calls it when the dialog completes.
    .AddFlirtyHandler<DialogCompletedNotification, MyDoneHandler>()
    .BuildServiceProvider();

using var scope = provider.CreateScope();
var engine = scope.ServiceProvider.GetRequiredService<IFlirtyEngine>();

var start = await engine.StartDialogAsync("onboarding", "user-1");
var result = await engine.SubmitAnswerAsync(start.SessionId, start.CurrentQuestion.Id, "\"dev\"");
// result.NextQuestion / result.IsCompleted drive the rest of the flow.
```

> **Careful with the migration:** `o.ApplyMigrations()` registers an `IHostedService` and therefore
> only takes effect inside a **Generic Host**. In a plain `ServiceCollection` setup as above, create the
> schema instead with `context.Database.EnsureCreated()`.

A complete, runnable example (project setup, seeding a dialog without the designer, a facade run,
your own `INotificationHandler`):
[`docs/GETTING-STARTED-Console.md`](https://github.com/dominikz98/flirty/blob/main/docs/GETTING-STARTED-Console.md).

## Quickstart (Web / Endpoints)

In a web host `o.ApplyMigrations()` is the convenient choice – the `WebApplicationBuilder` is a Generic
Host and starts the migration service on boot. Excerpt from the web sample
([`src/Flirty.Samples.Web/WebSampleApp.cs`](https://github.com/dominikz98/flirty/blob/main/src/Flirty.Samples.Web/WebSampleApp.cs)):

```csharp
builder.Services.AddFlirty(o =>
{
    o.UseSqlite(connectionString);        // or UsePostgreSql(...) / UseSqlServer(...)
    o.ApplyMigrations();                  // auto-migration on start
    o.AddWebhook(TriggerScope.OnDialogCompleted, baseUrl + "/demo/webhooks/flirty");
});

// Your own in-process handler as a back channel into the app.
builder.Services.AddFlirtyHandler<DialogCompletedNotification, DemoDialogCompletedHandler>();

var app = builder.Build();

app.MapFlirtyEndpoints("/flirty");        // package Flirty.AspNetCore
app.Run();
```

`MapFlirtyEndpoints` registers four endpoints (a thin layer over the Mediator commands) and returns the
`RouteGroupBuilder` – e.g. for `.RequireAuthorization()`:

| Method & route | Meaning |
|---|---|
| `POST /flirty/sessions` | Start a dialog (or resume an existing session) |
| `GET /flirty/sessions/{id}` | Read the state (resume after a reload) |
| `POST /flirty/sessions/{id}/answers` | Submit an answer |
| `PUT /flirty/sessions/{id}/answers/{questionId}` | Edit an earlier answer |

Additionally, `app.MapFlirtyAdminEndpoints("/flirty/admin")` registers the configuration CRUD
(dialogs, questions, answer options, transitions, loops, triggers) – the same operations the
designer uses. **Be sure to secure this in production** (e.g. `.RequireAuthorization()`).

Full guide (setup, request/response examples, error mapping, admin CRUD):
[`docs/GETTING-STARTED-WebApi.md`](https://github.com/dominikz98/flirty/blob/main/docs/GETTING-STARTED-WebApi.md).

## Start the designer

```pwsh
dotnet run --project src/Flirty.Designer
```

Then open [`http://localhost:5016`](http://localhost:5016). First, under **Connections**
(`/connections`), create, test and activate a connection profile (provider + connection string,
incl. "Migrate"); then, under **Dialogs** (`/dialogs`), configure dialogs, questions, answer options,
transitions, loops and triggers. The **test runner** (`/dialogs/{id}/test`) plays even
unpublished drafts through with the real engine. Details:
[`docs/DESIGNER.md`](https://github.com/dominikz98/flirty/blob/main/docs/DESIGNER.md).

## Running the samples

```pwsh
dotnet run --project src/Flirty.Samples        # console dialog in the terminal
dotnet run --project src/Flirty.Samples.Web    # chat UI at http://localhost:5080
```

The web sample creates a demo dialog on start and shows branching, a loop over a list,
resume after a reload, editing individual answers as well as fired triggers and received webhooks.

## Documentation

**Getting started**

- Getting Started (Console): [`docs/GETTING-STARTED-Console.md`](https://github.com/dominikz98/flirty/blob/main/docs/GETTING-STARTED-Console.md)
- Getting Started (WebAPI): [`docs/GETTING-STARTED-WebApi.md`](https://github.com/dominikz98/flirty/blob/main/docs/GETTING-STARTED-WebApi.md)
- Getting Started (web sample / chat UI): [`docs/GETTING-STARTED-Sample-Web.md`](https://github.com/dominikz98/flirty/blob/main/docs/GETTING-STARTED-Sample-Web.md)
- Designer (Blazor): [`docs/DESIGNER.md`](https://github.com/dominikz98/flirty/blob/main/docs/DESIGNER.md)

**Concepts**

- Architecture overview: [`docs/ARCHITECTURE.md`](https://github.com/dominikz98/flirty/blob/main/docs/ARCHITECTURE.md)
- Domain model & EF configuration: [`docs/DOMAIN-MODEL.md`](https://github.com/dominikz98/flirty/blob/main/docs/DOMAIN-MODEL.md)
- Runtime (start/resume/submit/edit): [`docs/RUNTIME.md`](https://github.com/dominikz98/flirty/blob/main/docs/RUNTIME.md)
- Persistence & migrations: [`docs/PERSISTENCE.md`](https://github.com/dominikz98/flirty/blob/main/docs/PERSISTENCE.md)
- Mediator setup: [`docs/MEDIATOR.md`](https://github.com/dominikz98/flirty/blob/main/docs/MEDIATOR.md)
- Branching / expressions: [`docs/BRANCHING-EXPRESSIONS.md`](https://github.com/dominikz98/flirty/blob/main/docs/BRANCHING-EXPRESSIONS.md)
- Loops: [`docs/LOOPS.md`](https://github.com/dominikz98/flirty/blob/main/docs/LOOPS.md)
- Triggers (notifications + webhooks): [`docs/TRIGGERS.md`](https://github.com/dominikz98/flirty/blob/main/docs/TRIGGERS.md)
- Answer validation: [`docs/VALIDATION.md`](https://github.com/dominikz98/flirty/blob/main/docs/VALIDATION.md)
- MCP server (tools for an MCP client): [`docs/MCP.md`](https://github.com/dominikz98/flirty/blob/main/docs/MCP.md)

**Project & operations**

- NuGet packaging: [`docs/NUGET-PACKAGING.md`](https://github.com/dominikz98/flirty/blob/main/docs/NUGET-PACKAGING.md)
- CI pipeline: [`docs/CI.md`](https://github.com/dominikz98/flirty/blob/main/docs/CI.md)
- Roadmap / backlog: [`docs/ROADMAP.md`](https://github.com/dominikz98/flirty/blob/main/docs/ROADMAP.md), [`docs/BACKLOG.md`](https://github.com/dominikz98/flirty/blob/main/docs/BACKLOG.md)
- Decisions (ADRs): [`docs/adr/`](https://github.com/dominikz98/flirty/blob/main/docs/adr/README.md) – why Mediator, why an ASP.NET-free core, why a sandboxed expression engine, why migrations per provider, why published dialog versions are immutable, why the designer canvas is built in-house, why the canvas layout lives in its own table, why the MCP server is a package of its own

## Build & Test

```pwsh
dotnet build Flirty.sln
dotnet test tests/Flirty.Tests             # unit/integration tests
dotnet test tests/Flirty.E2E               # Playwright E2E (browser required, see below)
dotnet pack -c Release -o artifacts        # Flirty.*.nupkg + Flirty.AspNetCore.*.nupkg (+ .snupkg)
```

> The two test projects are deliberately started **separately**: `dotnet test` without a target runs them
> in parallel, which makes the browser-driven E2E compete with the unit suite for the same cores.
> The E2E need Chromium
> (`pwsh tests/Flirty.E2E/bin/Release/net10.0/playwright.ps1 install chromium`); if it is missing, they
> skip themselves. The PostgreSQL/SQL Server tests need Docker and are likewise
> skipped without Docker. The target framework is **.NET 10** (SDK required).

> Publishing goes through the `release.yml` workflow – manually and behind an approval gate:
> [`docs/NUGET-PACKAGING.md` § Publishing](https://github.com/dominikz98/flirty/blob/main/docs/NUGET-PACKAGING.md#publishing-49).

## License & Feedback

MIT – see [`LICENSE`](https://github.com/dominikz98/flirty/blob/main/LICENSE).
Questions, bugs and requests are welcome as a [GitHub issue](https://github.com/dominikz98/flirty/issues).
