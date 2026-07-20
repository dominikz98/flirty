# Getting Started – Web-Sample (Minimal-API + Chat-UI)

> Stand: Issue #45. Dieser Guide zeigt die lauffähige **Web-Sample** unter
> [`src/Flirty.Samples.Web`](../src/Flirty.Samples.Web): ein Minimal-API-Host, der die Flirty-Endpunkte
> hostet **und** über eine statische Chat-UI (HTML + Vanilla JS) **konsumiert**. Demonstriert werden
> **Resume**, **Edit**, **Branching**, **Loop über Liste** und **Trigger** – mit einem eigenen
> In-Process-**Handler** und einem Inbound-**Webhook-Empfänger**. Grundlagen: die Endpunkte in
> [GETTING-STARTED-WebApi.md](./GETTING-STARTED-WebApi.md), die Trigger in [TRIGGERS.md](./TRIGGERS.md),
> die Schleifen in [LOOPS.md](./LOOPS.md).

## Ausführen

```pwsh
dotnet run --project src/Flirty.Samples.Web
```

Danach [`http://localhost:5080`](http://localhost:5080) öffnen. Die App legt beim Start einen Demo-Dialog
an (siehe unten), die Chat-UI startet automatisch eine Session. Spiele den Dialog durch (Rollen-Auswahl →
Detailfrage → mehrere Fähigkeiten über die Schleife → Abschluss); rechts zeigen Panels die gesammelten
Fähigkeiten, die ausgelösten In-Process-Trigger und die empfangenen Webhooks. **Reload** stellt die Session
wieder her (Resume), das **✏️** an einer Antwort editiert sie (Edit).

## Projekt-Setup

Der Host ist ein `Microsoft.NET.Sdk.Web`-Projekt und referenziert nur den Core, das Endpunkt-Paket und –
für `o.ApplyMigrations()` mit SQLite – die SQLite-Migrations-Assembly. ASP.NET Core kommt über das SDK,
**keine** zusätzlichen NuGet-Pakete:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <ItemGroup>
    <ProjectReference Include="..\Flirty\Flirty.csproj" />
    <ProjectReference Include="..\Flirty.AspNetCore\Flirty.AspNetCore.csproj" />
    <ProjectReference Include="..\Flirty.Migrations.Sqlite\Flirty.Migrations.Sqlite.csproj" />
  </ItemGroup>
</Project>
```

## 1. Registrierung & Endpunkte

Die Komposition liegt in [`WebSampleApp`](../src/Flirty.Samples.Web/WebSampleApp.cs) (geteilt von
`Program.cs` und den Integrationstests). `AddFlirty(…)` verdrahtet den Stack; der eigene In-Process-Handler
wird per `AddFlirtyHandler<…>()` registriert, das Loopback-Ziel des Outbound-Webhooks per `o.AddWebhook(…)`:

```csharp
builder.Services.AddFlirty(o =>
{
    o.UseSqlite(connectionString);
    o.ApplyMigrations();
    o.AddWebhook(TriggerScope.OnDialogCompleted, baseUrl + "/demo/webhooks/flirty"); // Loopback-Demo
});
builder.Services.AddFlirtyHandler<DialogCompletedNotification, DemoDialogCompletedHandler>();
```

```csharp
app.UseDefaultFiles();
app.UseStaticFiles();                         // statische Chat-UI aus wwwroot
app.MapFlirtyEndpoints("/flirty");            // Laufzeit – von der Chat-UI konsumiert
app.MapFlirtyAdminEndpoints("/flirty/admin"); // Konfiguration – baut den Demo-Dialog
```

> **Sicherheit:** Die Admin-Endpunkte sind im Sample bewusst **ohne** `RequireAuthorization()` gemappt, damit
> das Provisioning und die UI ohne Auth-Setup laufen. In Produktion die Admin-Fläche zwingend absichern
> (siehe [GETTING-STARTED-WebApi.md](./GETTING-STARTED-WebApi.md) §4).

Steuerbare Konfiguration (Defaults in `appsettings.json`): `ConnectionStrings:Flirty`, `Flirty:BaseUrl`,
`Flirty:ApplyMigrations`, `Flirty:EnableOutboundWebhook`, `Flirty:AutoProvision`.

## 2. Demo-Dialog: Aufbau über die Admin-CRUD-API

Der Demo-Dialog `web-onboarding` wird beim Start idempotent über die **Admin-CRUD-API** aufgebaut
([`DemoDialogProvisioner`](../src/Flirty.Samples.Web/DemoDialogProvisioner.cs), getrieben vom
[`DemoProvisioningHostedService`](../src/Flirty.Samples.Web/DemoProvisioningHostedService.cs)). Ablauf:
`POST /dialogs` → `POST …/questions` (+ `…/options`) → `PUT /dialogs/{id}` (StartQuestionId) →
`POST …/transitions` → `POST …/dialogs/{id}/publish`.

Dialogfluss (Branching **und** Loop über Liste):

```text
role (SingleChoice: dev|pm)                     ← Branching (Startfrage)
   ├─ role=="dev"  → language (FreeText)
   └─ default      → product  (FreeText)
language|product → skill (FreeText)             ← Loop-Entry (CollectionKey "skills")
skill            → more  (SingleChoice: yes|no) ← Breaking Question
   ├─ more=="yes" → skill  (Loop-Back, Priority 0)
   └─ default     → summary (Exit, Priority 1)
summary (Boolean, terminal)                     → Abschluss → Trigger
```

> **Bewusste Ausnahme (Loop-Marker):** Die Admin-CRUD-API deckt **kein** Loop-CRUD ab (nur Dialog/Frage/
> Option/Übergang, siehe [GETTING-STARTED-WebApi.md](./GETTING-STARTED-WebApi.md) §4). Der Zyklus entsteht
> über die Loop-Back-`Transition` (`more == "yes"` → `skill`), die eigentliche
> [`LoopDefinition`](../src/Flirty/Domain/LoopDefinition.cs) (`CollectionKey="skills"`, Entry `skill`,
> Breaking `more`) hängt der Provisioner **einmalig direkt über den `FlirtyDbContext`** an – erst dadurch
> sammelt die Laufzeit die `skill`-Antworten je Iteration statt sie zu überschreiben (siehe
> [LOOPS.md](./LOOPS.md)).

## 3. Chat-UI (`wwwroot`)

Die UI ([`wwwroot/app.js`](../src/Flirty.Samples.Web/wwwroot/app.js)) spricht ausschließlich die
HTTP-Endpunkte an und hält keinen Server-Zustand:

- **Start/Resume:** `externalUserKey` und `sessionId` liegen im `localStorage`. Beim Laden wird der Verlauf
  über `GET /flirty/sessions/{id}` rekonstruiert (Resume nach Reload); ohne gespeicherte Session wird per
  `POST /flirty/sessions` neu gestartet.
- **Antworten:** `POST /flirty/sessions/{id}/answers` mit dem `value` als **rohem JSON-Text** je Fragetyp
  (SingleChoice/FreeText → JSON-String, Boolean → `true`/`false`).
- **Edit:** `PUT /flirty/sessions/{id}/answers/{questionId}` (bei Loop-Antworten mit `iterationIndex`);
  die Anzahl verworfener Folgeantworten wird angezeigt.
- **Loop/Branching:** ergeben sich aus dem gerenderten `currentQuestion`-Fluss; die gesammelten `skills`
  zeigt ein Seitenpanel.

## 4. Trigger: Handler + Webhook-Empfänger

- **In-Process-Handler:** [`DemoDialogCompletedHandler`](../src/Flirty.Samples.Web/DemoDialogCompletedHandler.cs)
  (`INotificationHandler<DialogCompletedNotification>`) protokolliert jeden Abschluss in eine Senke, die
  `GET /demo/triggers` anzeigt.
- **Inbound-Webhook-Empfänger:** `POST /demo/webhooks/flirty` nimmt den ausgehenden HTTP-`POST` der Engine
  entgegen, liest den Header `X-Flirty-Event` und den JSON-Body und legt beides für `GET /demo/webhooks` ab.
  Weil das Sample per `o.AddWebhook(OnDialogCompleted, …/demo/webhooks/flirty)` an sich selbst zustellt,
  ist der komplette **Outbound→Inbound-Rundlauf** live im Trigger-Panel sichtbar.

## Verifikation

```pwsh
dotnet test tests/Flirty.Tests -c Release   # In-Process-TestServer: Branching/Loop/Resume/Edit/Handler/Inbound
```

Der Integrationstest [`WebSampleTests`](../tests/Flirty.Tests/Samples/WebSampleTests.cs) hostet die echte
Sample-Komposition über einen In-Process-`TestServer` (SQLite in-memory) und spielt sie end-to-end durch.
Der volle Outbound→Inbound-Webhook-Rundlauf braucht echtes Kestrel und ist im Browser abgesichert:

```pwsh
pwsh tests/Flirty.E2E/bin/Release/net10.0/playwright.ps1 install chromium  # einmalig
dotnet test tests/Flirty.E2E -c Release
```

[`WebSampleE2ETests`](../tests/Flirty.E2E/WebSampleE2ETests.cs) startet die App auf echtem Kestrel und treibt
die Chat-UI im Browser (Durchlauf inkl. Loop + Trigger-Rundlauf, Reload→Resume, Edit). Fehlen die
Playwright-Browser, überspringen sich die E2E-Tests, statt zu scheitern.
