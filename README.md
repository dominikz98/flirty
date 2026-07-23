# Flirty

[![CI](https://github.com/dominikz98/flirty/actions/workflows/ci.yml/badge.svg)](https://github.com/dominikz98/flirty/actions/workflows/ci.yml)
[![NuGet: Flirty](https://img.shields.io/nuget/v/Flirty?label=NuGet%3A%20Flirty)](https://www.nuget.org/packages/Flirty)
[![NuGet: Flirty.AspNetCore](https://img.shields.io/nuget/v/Flirty.AspNetCore?label=NuGet%3A%20Flirty.AspNetCore)](https://www.nuget.org/packages/Flirty.AspNetCore)

Wiederverwendbare **Chatbot-/Dialog-Engine für .NET**. Du baust nur die UI – Flirty übernimmt
Persistenz, Antwort-Validierung, **Branching**, **Loops**, **Resume**, editierbare Antworten und
**Trigger** (Rückkanäle in deine App). Dialoge werden über einen **Blazor-Designer** konfiguriert
(auch von nicht-technischen Nutzern).

Der Core ist eine reine Class-Library **ohne ASP.NET-Abhängigkeit** und läuft unverändert in
Console-, Worker- und Web-Anwendungen. HTTP-Endpunkte sind opt-in über das Zusatzpaket
`Flirty.AspNetCore`.

## Features

| Feature | Umsetzung |
|---|---|
| Resume innerhalb eines Dialogs | Session hält die aktuelle Frage; Wiederaufnahme über die Session-Id |
| Antworten nachträglich editieren | `EditAnswerAsync` + Neuberechnung des Folgepfads |
| Branching (mehrere Zweige) | Übergänge mit gesandboxten Bedingungsausdrücken (Default: DynamicExpresso) |
| Loops (Liste bis zur Abbruchfrage) | Branching-Zyklus + Schleifen-Marker, Iterationen als Collection im Kontext |
| Trigger nach Antwort / Abschluss | In-Process-Notifications (Mediator) **und** Outbound-Webhooks |
| Antwort-Validierung | Typprüfung + `ValidationRules` je Frage, vor dem Handler in der Pipeline |
| Multi-DB + Auto-Migration | SQLite / PostgreSQL / SQL Server, Migrationen pro Provider |
| Einfache Integration | `services.AddFlirty(o => …)`, optional `app.MapFlirtyEndpoints()` |

## Projekte

| Projekt | Zweck |
|---|---|
| `src/Flirty` | Core-Engine (Domain, Runtime, EF-Core-Persistenz, Mediator, DI-Extensions). **Kein ASP.NET** → auch in Console/Worker nutzbar. NuGet-Package. |
| `src/Flirty.AspNetCore` | Optionale WebAPI-Endpunkte (`MapFlirtyEndpoints`, `MapFlirtyAdminEndpoints`). NuGet-Package. |
| `src/Flirty.Designer` | Blazor Web App zum Konfigurieren von Dialogen/Fragen/Antworten/Branching/Loops/Triggern, inkl. Test-Runner. Multi-DB → [`docs/DESIGNER.md`](https://github.com/dominikz98/flirty/blob/main/docs/DESIGNER.md). |
| `src/Flirty.Migrations.*` | EF-Migrationen je Provider (SQLite, PostgreSQL, SQL Server); in das `Flirty`-Paket gebündelt. |
| `src/Flirty.Samples` | Lauffähiges **Console-Sample** (nur Core, kein ASP.NET) → [`docs/GETTING-STARTED-Console.md`](https://github.com/dominikz98/flirty/blob/main/docs/GETTING-STARTED-Console.md). |
| `src/Flirty.Samples.Web` | Lauffähiges **Web-Sample** (Minimal-API + statische Chat-UI): Resume/Edit/Branching/Loop/Trigger + Webhook-Empfänger → [`docs/GETTING-STARTED-Sample-Web.md`](https://github.com/dominikz98/flirty/blob/main/docs/GETTING-STARTED-Sample-Web.md). |
| `tests/Flirty.Tests` | Unit-/Integrationstests (xUnit). |
| `tests/Flirty.E2E` | Playwright-E2E-Tests (Web-Sample-Chat-UI und Blazor-Designer). |

## Installation

```pwsh
dotnet add package Flirty                 # Core-Engine (ohne ASP.NET nutzbar)
dotnet add package Flirty.AspNetCore      # optional: fertige WebAPI-Endpunkte
```

> Zielframework ist **.NET 10**. Das Versionsschema ist datumsbasiert (`JJJJMM.Revision.0`, z. B.
> `202607.3.0`) – kein SemVer-Signal, siehe
> [`docs/NUGET-PACKAGING.md`](https://github.com/dominikz98/flirty/blob/main/docs/NUGET-PACKAGING.md).

## Quickstart (Console)

`AddFlirty(o => …)` verdrahtet den kompletten Stack (Mediator, Runtime-Facade, Persistenz,
Expression-Engine, Validierung). Ausschnitt aus dem Console-Sample
([`src/Flirty.Samples/Program.cs`](https://github.com/dominikz98/flirty/blob/main/src/Flirty.Samples/Program.cs)):

```csharp
// SQLite in-memory (Shared-Cache): solange die keep-alive-Verbindung offen ist, teilen sich
// alle DI-erzeugten FlirtyDbContext-Instanzen dieselbe Datenbank.
const string connectionString = "Data Source=FlirtyQuickstart;Mode=Memory;Cache=Shared";

using var keepAlive = new SqliteConnection(connectionString);
keepAlive.Open();

using var provider = new ServiceCollection()
    .AddLogging()
    .AddFlirty(o => o.UseSqlite(connectionString))
    // Eigener Rückkanal: die Engine ruft ihn beim Dialog-Abschluss auf.
    .AddFlirtyHandler<DialogCompletedNotification, MyDoneHandler>()
    .BuildServiceProvider();

using var scope = provider.CreateScope();
var engine = scope.ServiceProvider.GetRequiredService<IFlirtyEngine>();

var start = await engine.StartDialogAsync("onboarding", "user-1");
var result = await engine.SubmitAnswerAsync(start.SessionId, start.CurrentQuestion.Id, "\"dev\"");
// result.NextQuestion / result.IsCompleted steuern den weiteren Ablauf.
```

> **Achtung bei der Migration:** `o.ApplyMigrations()` registriert einen `IHostedService` und wirkt
> deshalb nur in einem **Generic Host**. Im reinen `ServiceCollection`-Setup wie oben legt man das
> Schema stattdessen mit `context.Database.EnsureCreated()` an.

Vollständiges, lauffähiges Beispiel (Projekt-Setup, Dialog ohne Designer seeden, Facade-Durchlauf,
eigener `INotificationHandler`):
[`docs/GETTING-STARTED-Console.md`](https://github.com/dominikz98/flirty/blob/main/docs/GETTING-STARTED-Console.md).

## Quickstart (Web / Endpunkte)

Im Web-Host ist `o.ApplyMigrations()` die bequeme Wahl – der `WebApplicationBuilder` ist ein Generic
Host und startet den Migrations-Service beim Hochfahren. Ausschnitt aus dem Web-Sample
([`src/Flirty.Samples.Web/WebSampleApp.cs`](https://github.com/dominikz98/flirty/blob/main/src/Flirty.Samples.Web/WebSampleApp.cs)):

```csharp
builder.Services.AddFlirty(o =>
{
    o.UseSqlite(connectionString);        // oder UsePostgreSql(...) / UseSqlServer(...)
    o.ApplyMigrations();                  // Auto-Migration beim Start
    o.AddWebhook(TriggerScope.OnDialogCompleted, baseUrl + "/demo/webhooks/flirty");
});

// Eigener In-Process-Handler als Rückkanal in die App.
builder.Services.AddFlirtyHandler<DialogCompletedNotification, DemoDialogCompletedHandler>();

var app = builder.Build();

app.MapFlirtyEndpoints("/flirty");        // Paket Flirty.AspNetCore
app.Run();
```

`MapFlirtyEndpoints` registriert vier Endpunkte (dünne Schicht über die Mediator-Commands) und gibt die
`RouteGroupBuilder` zurück – z. B. für `.RequireAuthorization()`:

| Methode & Route | Bedeutung |
|---|---|
| `POST /flirty/sessions` | Dialog starten (oder bestehende Session fortsetzen) |
| `GET /flirty/sessions/{id}` | Zustand lesen (Resume nach Reload) |
| `POST /flirty/sessions/{id}/answers` | Antwort einreichen |
| `PUT /flirty/sessions/{id}/answers/{questionId}` | Frühere Antwort editieren |

Ergänzend registriert `app.MapFlirtyAdminEndpoints("/flirty/admin")` das Konfigurations-CRUD
(Dialoge, Fragen, Antwortoptionen, Übergänge, Schleifen, Trigger) – dieselben Operationen, die auch der
Designer nutzt. **In Produktion unbedingt absichern** (z. B. `.RequireAuthorization()`).

Vollständiger Guide (Setup, Request/Response-Beispiele, Fehler-Mapping, Admin-CRUD):
[`docs/GETTING-STARTED-WebApi.md`](https://github.com/dominikz98/flirty/blob/main/docs/GETTING-STARTED-WebApi.md).

## Designer starten

```pwsh
dotnet run --project src/Flirty.Designer
```

Danach [`http://localhost:5016`](http://localhost:5016) öffnen. Zuerst unter **Verbindungen**
(`/connections`) ein Connection-Profil anlegen, testen und aktivieren (Provider + Verbindungszeichenfolge,
inkl. „Migrieren"); anschließend unter **Dialoge** (`/dialogs`) Dialoge, Fragen, Antwortoptionen,
Übergänge, Schleifen und Trigger konfigurieren. Der **Test-Runner** (`/dialogs/{id}/test`) spielt auch
unveröffentlichte Entwürfe mit der echten Engine durch. Details:
[`docs/DESIGNER.md`](https://github.com/dominikz98/flirty/blob/main/docs/DESIGNER.md).

## Samples ausführen

```pwsh
dotnet run --project src/Flirty.Samples        # Console-Dialog im Terminal
dotnet run --project src/Flirty.Samples.Web    # Chat-UI unter http://localhost:5080
```

Das Web-Sample legt beim Start einen Demo-Dialog an und zeigt Branching, eine Schleife über eine Liste,
Resume nach Reload, das Editieren einzelner Antworten sowie ausgelöste Trigger und empfangene Webhooks.

## Dokumentation

**Einstieg**

- Getting Started (Console): [`docs/GETTING-STARTED-Console.md`](https://github.com/dominikz98/flirty/blob/main/docs/GETTING-STARTED-Console.md)
- Getting Started (WebAPI): [`docs/GETTING-STARTED-WebApi.md`](https://github.com/dominikz98/flirty/blob/main/docs/GETTING-STARTED-WebApi.md)
- Getting Started (Web-Sample / Chat-UI): [`docs/GETTING-STARTED-Sample-Web.md`](https://github.com/dominikz98/flirty/blob/main/docs/GETTING-STARTED-Sample-Web.md)
- Designer (Blazor): [`docs/DESIGNER.md`](https://github.com/dominikz98/flirty/blob/main/docs/DESIGNER.md)

**Konzepte**

- Architektur-Gesamtbild: [`docs/ARCHITECTURE.md`](https://github.com/dominikz98/flirty/blob/main/docs/ARCHITECTURE.md)
- Domänenmodell & EF-Konfiguration: [`docs/DOMAIN-MODEL.md`](https://github.com/dominikz98/flirty/blob/main/docs/DOMAIN-MODEL.md)
- Runtime (Start/Resume/Submit/Edit): [`docs/RUNTIME.md`](https://github.com/dominikz98/flirty/blob/main/docs/RUNTIME.md)
- Persistenz & Migrationen: [`docs/PERSISTENCE.md`](https://github.com/dominikz98/flirty/blob/main/docs/PERSISTENCE.md)
- Mediator-Setup: [`docs/MEDIATOR.md`](https://github.com/dominikz98/flirty/blob/main/docs/MEDIATOR.md)
- Branching / Expressions: [`docs/BRANCHING-EXPRESSIONS.md`](https://github.com/dominikz98/flirty/blob/main/docs/BRANCHING-EXPRESSIONS.md)
- Loops: [`docs/LOOPS.md`](https://github.com/dominikz98/flirty/blob/main/docs/LOOPS.md)
- Trigger (Notifications + Webhooks): [`docs/TRIGGERS.md`](https://github.com/dominikz98/flirty/blob/main/docs/TRIGGERS.md)
- Antwort-Validierung: [`docs/VALIDATION.md`](https://github.com/dominikz98/flirty/blob/main/docs/VALIDATION.md)

**Projekt & Betrieb**

- NuGet-Packaging: [`docs/NUGET-PACKAGING.md`](https://github.com/dominikz98/flirty/blob/main/docs/NUGET-PACKAGING.md)
- CI-Pipeline: [`docs/CI.md`](https://github.com/dominikz98/flirty/blob/main/docs/CI.md)
- Roadmap / Backlog: [`docs/ROADMAP.md`](https://github.com/dominikz98/flirty/blob/main/docs/ROADMAP.md), [`docs/BACKLOG.md`](https://github.com/dominikz98/flirty/blob/main/docs/BACKLOG.md)
- Entscheidungen (ADRs): [`docs/adr/`](https://github.com/dominikz98/flirty/blob/main/docs/adr/README.md) – warum Mediator, warum ASP.NET-freier Core, warum eine gesandboxte Expression-Engine, warum Migrationen pro Provider

## Build & Test

```pwsh
dotnet build Flirty.sln
dotnet test tests/Flirty.Tests             # Unit-/Integrationstests
dotnet test tests/Flirty.E2E               # Playwright-E2E (Browser nötig, siehe unten)
dotnet pack -c Release -o artifacts        # Flirty.*.nupkg + Flirty.AspNetCore.*.nupkg (+ .snupkg)
```

> Die beiden Test-Projekte werden bewusst **getrennt** gestartet: `dotnet test` ohne Ziel führt sie
> parallel aus, wodurch die browsergetriebenen E2E mit der Unit-Suite um dieselben Kerne konkurrieren.
> Die E2E brauchen Chromium
> (`pwsh tests/Flirty.E2E/bin/Release/net10.0/playwright.ps1 install chromium`); fehlt er, überspringen
> sie sich. Die PostgreSQL-/SQL-Server-Tests brauchen Docker und werden ohne Docker ebenfalls
> übersprungen. Zielframework ist **.NET 10** (SDK erforderlich).

> Veröffentlicht wird über den Workflow `release.yml` – manuell und hinter einem Freigabe-Gate:
> [`docs/NUGET-PACKAGING.md` § Publizieren](https://github.com/dominikz98/flirty/blob/main/docs/NUGET-PACKAGING.md#publizieren-49).

## Lizenz & Feedback

MIT – siehe [`LICENSE`](https://github.com/dominikz98/flirty/blob/main/LICENSE).
Fragen, Fehler und Wünsche gerne als [GitHub-Issue](https://github.com/dominikz98/flirty/issues).
