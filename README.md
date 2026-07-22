# Flirty

[![CI](https://github.com/dominikz98/flirty/actions/workflows/ci.yml/badge.svg)](https://github.com/dominikz98/flirty/actions/workflows/ci.yml)

Chatbot-/Dialog-Engine für .NET. Du baust nur die UI – Flirty übernimmt Persistenz,
Antwort-Validierung, **Branching**, **Loops**, **Resume**, editierbare Antworten und
**Trigger** (Rückkanäle in deine App). Dialoge werden über einen **Blazor-Designer**
konfiguriert (auch von nicht-technischen Nutzern).

## Projekte

| Projekt | Zweck |
|---|---|
| `src/Flirty` | Core-Engine (Domain, Runtime, EF-Core-Persistenz, Mediator, DI-Extensions). **Kein ASP.NET** → auch in Console/Worker nutzbar. NuGet-Package. |
| `src/Flirty.AspNetCore` | Optionale WebAPI-Endpunkte (`MapFlirtyEndpoints`). NuGet-Package. |
| `src/Flirty.Designer` | Blazor Web App zum Konfigurieren von Dialogen/Fragen/Antworten/Branching/Loops/Triggern. Multi-DB. |
| `src/Flirty.Samples` | Lauffähiges **Console-Sample** (nur Core, kein ASP.NET) → [`docs/GETTING-STARTED-Console.md`](docs/GETTING-STARTED-Console.md). |
| `src/Flirty.Samples.Web` | Lauffähiges **Web-Sample** (Minimal-API + statische Chat-UI): Resume/Edit/Branching/Loop/Trigger + Webhook-Empfänger → [`docs/GETTING-STARTED-Sample-Web.md`](docs/GETTING-STARTED-Sample-Web.md). |
| `tests/Flirty.Tests` | Unit-/Integrationstests (xUnit). |
| `tests/Flirty.E2E` | Playwright-E2E-Tests (Web-Sample-Chat-UI und Blazor-Designer). |

## Quickstart (Console)

```csharp
var services = new ServiceCollection();
services.AddFlirty(o =>
{
    o.UseSqlite("Data Source=flirty.db");
    o.ApplyMigrations();
});
// Eigene Reaktion auf Dialog-Abschluss "reinhängen":
services.AddFlirtyHandler<DialogCompletedNotification, MyDoneHandler>();
```

> Vollständiges, lauffähiges Beispiel (Setup, Seeding ohne Designer, Facade-Durchlauf, eigener
> `INotificationHandler`): [`docs/GETTING-STARTED-Console.md`](docs/GETTING-STARTED-Console.md).
> Die Engine publiziert die Notifications selbst (seit #31); der per `AddFlirtyHandler<T, THandler>()`
> registrierte Handler wird beim Dialog-Abschluss automatisch aufgerufen.

## Quickstart (Web / Endpunkte)

```csharp
builder.Services.AddFlirty(o => o.UseSqlServer(conn).ApplyMigrations());
// ...
app.MapFlirtyEndpoints("/flirty"); // Paket Flirty.AspNetCore
```

Registriert vier Endpunkte (dünne Schicht über die Mediator-Commands):
`POST /flirty/sessions` (Start/Resume), `GET /flirty/sessions/{id}` (Zustand),
`POST /flirty/sessions/{id}/answers` (Antwort), `PUT /flirty/sessions/{id}/answers/{questionId}` (Edit).

> Vollständiger Guide (Setup, Request/Response-Beispiele, Fehler-Mapping):
> [`docs/GETTING-STARTED-WebApi.md`](docs/GETTING-STARTED-WebApi.md). Eine lauffähige Web-App mit Chat-UI,
> die diese Endpunkte konsumiert (Resume/Edit/Branching/Loop/Trigger + Webhook-Empfänger), zeigt das
> Web-Sample: [`docs/GETTING-STARTED-Sample-Web.md`](docs/GETTING-STARTED-Sample-Web.md).

## Dokumentation

- Architektur: [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md)
- Getting Started (Console): [`docs/GETTING-STARTED-Console.md`](docs/GETTING-STARTED-Console.md)
- Getting Started (WebAPI): [`docs/GETTING-STARTED-WebApi.md`](docs/GETTING-STARTED-WebApi.md)
- Getting Started (Web-Sample / Chat-UI): [`docs/GETTING-STARTED-Sample-Web.md`](docs/GETTING-STARTED-Sample-Web.md)
- Backlog / Issues: [`docs/BACKLOG.md`](docs/BACKLOG.md)
- CI-Pipeline: [`docs/CI.md`](docs/CI.md)
- NuGet-Packaging: [`docs/NUGET-PACKAGING.md`](docs/NUGET-PACKAGING.md)

## Build & Test

```pwsh
dotnet build Flirty.sln
dotnet test
dotnet pack -c Release   # erzeugt Flirty.*.nupkg + Flirty.AspNetCore.*.nupkg
```

> Zielframework: **.NET 10**. Erfordert das .NET 10 SDK.
