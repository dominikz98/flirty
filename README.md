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
| `src/Flirty.Samples` | Beispielanwendung(en). Lauffähiges **Console-Sample** (nur Core, kein ASP.NET) → [`docs/GETTING-STARTED-Console.md`](docs/GETTING-STARTED-Console.md). |
| `tests/Flirty.Tests` | Unit-/Integrationstests (xUnit). |
| `tests/Flirty.E2E` | Playwright-E2E-Tests. |

## Quickstart (Console)

```csharp
var services = new ServiceCollection();
services.AddFlirty(o =>
{
    o.UseSqlite("Data Source=flirty.db");
    o.ApplyMigrations();
});
// Eigene Reaktion auf Dialog-Abschluss "reinhängen":
services.AddScoped<INotificationHandler<DialogCompletedNotification>, MyDoneHandler>();
```

> Vollständiges, lauffähiges Beispiel (Setup, Seeding ohne Designer, Facade-Durchlauf, eigener
> `INotificationHandler`): [`docs/GETTING-STARTED-Console.md`](docs/GETTING-STARTED-Console.md).
> Engine-getriebenes Publizieren der Notifications folgt in EPIC 4; bis dahin löst das Sample seinen
> Handler nach dem Abschluss selbst aus.

## Quickstart (Web / Endpunkte)

```csharp
builder.Services.AddFlirty(o => o.UseSqlServer(conn).ApplyMigrations());
// ...
app.MapFlirtyEndpoints("/flirty"); // Paket Flirty.AspNetCore
```

## Dokumentation

- Architektur: [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md)
- Getting Started (Console): [`docs/GETTING-STARTED-Console.md`](docs/GETTING-STARTED-Console.md)
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
