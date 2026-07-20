# Getting Started – WebAPI (Flirty.AspNetCore)

> Stand: Issue #35. Dieser Guide zeigt, wie man die Flirty-Engine als HTTP-API bereitstellt – über das
> optionale Paket [`Flirty.AspNetCore`](../src/Flirty.AspNetCore) und die Erweiterungsmethode
> `MapFlirtyEndpoints`. Die Endpunkte sind eine **dünne Schicht über die Mediator-Commands**: Sie senden
> die Runtime-Commands direkt per `ISender` und mappen die Ergebnisse auf serialisierbare DTOs. Der Core
> (`src/Flirty`) bleibt dabei bewusst ASP.NET-frei (siehe [ARCHITECTURE.md](./ARCHITECTURE.md)).

## Projekt-Setup

`Flirty.AspNetCore` bringt die Endpunkte mit und referenziert das ASP.NET-Core-Shared-Framework über
einen `FrameworkReference` (nicht als NuGet-Paket). Eine Host-App braucht nur die beiden Referenzen:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <ItemGroup>
    <!-- EF Core, SQLite/SqlServer/PostgreSQL-Provider, Mediator kommen transitiv über den Core. -->
    <ProjectReference Include="..\Flirty\Flirty.csproj" />
    <ProjectReference Include="..\Flirty.AspNetCore\Flirty.AspNetCore.csproj" />
  </ItemGroup>
</Project>
```

> Als NuGet-Konsument stattdessen `dotnet add package Flirty` **und** `dotnet add package Flirty.AspNetCore`.

## 1. Registrierung (`AddFlirty` + `MapFlirtyEndpoints`)

`AddFlirty(o => …)` verdrahtet den kompletten Stack (Mediator, Runtime, Persistenz, Expression-Engine,
Validierung). `MapFlirtyEndpoints(prefix)` registriert die HTTP-Endpunkte als Minimal-API-Route-Gruppe.
Beide Bausteine sind ohne zusätzliches `using` sichtbar (die Extensions liegen in
`Microsoft.Extensions.DependencyInjection` bzw. `Microsoft.AspNetCore.Builder`):

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddFlirty(o =>
{
    o.UseSqlServer(builder.Configuration.GetConnectionString("Flirty")!);
    o.ApplyMigrations();                 // optional: Auto-Migration beim Start
});

var app = builder.Build();

app.MapFlirtyEndpoints("/flirty");       // Standard-Präfix ist ebenfalls "/flirty"

app.Run();
```

`MapFlirtyEndpoints` gibt die erzeugte `RouteGroupBuilder` zurück – so lässt sich die Gruppe weiter
konfigurieren, z. B. `app.MapFlirtyEndpoints("/flirty").RequireAuthorization();`.

## 2. Endpunkte

Alle Endpunkte bilden 1:1 die Runtime-Commands/Query ab (siehe [RUNTIME.md](./RUNTIME.md) und
[MEDIATOR.md](./MEDIATOR.md)). `sessionId`/`questionId` stehen in der Route, die restlichen Felder im
Body. Antwortwerte sind **roher JSON-Text** (Format je Fragetyp, z. B. `"dev"` für eine Auswahl).

| Methode & Route | Command/Query | Erfolg |
|---|---|---|
| `POST /flirty/sessions` | `StartDialogCommand` | `201 Created` + `Location`, `StartSessionResponse` |
| `GET /flirty/sessions/{id}` | `ResumeDialogQuery` | `200 OK`, `SessionStateResponse` |
| `POST /flirty/sessions/{id}/answers` | `SubmitAnswerCommand` | `200 OK`, `SubmitAnswerResponse` |
| `PUT /flirty/sessions/{id}/answers/{questionId}` | `EditAnswerCommand` | `200 OK`, `EditAnswerResponse` |

### Dialog starten (oder fortsetzen)

```http
POST /flirty/sessions
Content-Type: application/json

{ "dialogKey": "onboarding", "externalUserKey": "user-42" }
```

```jsonc
// 201 Created, Location: /flirty/sessions/8f3e…
{
  "sessionId": "8f3e…",
  "isResumed": false,
  "currentQuestion": {
    "id": "1a2b…", "key": "role", "text": "Welche Rolle?", "type": 0,
    "options": [ { "id": "…", "key": "dev", "label": "Entwickler", "value": "dev" } ]
  }
}
```

Existiert für den Anwender bereits eine laufende Session der aktuellen Dialogversion, wird sie
fortgesetzt (`isResumed: true`) statt eine neue anzulegen.

### Antwort einreichen

```http
POST /flirty/sessions/8f3e…/answers
Content-Type: application/json

{ "questionId": "1a2b…", "value": "\"dev\"" }
```

```jsonc
// 200 OK
{ "sessionId": "8f3e…", "isCompleted": false, "nextQuestion": { "key": "devDetail", … } }
```

Ist der Dialog nach der Antwort abgeschlossen, liefert die Response `"isCompleted": true` und
`"nextQuestion": null`.

### Zustand lesen (Resume nach Reload)

```http
GET /flirty/sessions/8f3e…
```

```jsonc
// 200 OK
{
  "sessionId": "8f3e…", "status": 0,          // 0 = InProgress, 1 = Completed, 2 = Abandoned
  "currentQuestion": { "key": "devDetail", … },
  "answers": [ { "questionKey": "role", "value": "\"dev\"", "sequence": 0, … } ]
}
```

### Frühere Antwort editieren

```http
PUT /flirty/sessions/8f3e…/answers/1a2b…
Content-Type: application/json

{ "value": "\"pm\"" }
```

```jsonc
// 200 OK – nachgelagerte Antworten werden verworfen, der Pfad neu berechnet
{ "sessionId": "8f3e…", "isCompleted": false, "nextQuestion": { "key": "pmDetail", … }, "invalidatedAnswers": 1 }
```

Der optionale Body-Wert `iterationIndex` editiert innerhalb einer Schleife gezielt die Antwort einer
bestimmten Iteration (siehe [LOOPS.md](./LOOPS.md)).

## 3. Fehler-Mapping

Von der Engine geworfene Ausnahmen werden über einen Endpunkt-Filter einheitlich auf `ProblemDetails`
abgebildet – die Host-App braucht dafür **keine** eigene Exception-Middleware:

| Situation | Ausnahme | Status |
|---|---|---|
| Kein veröffentlichter Dialog zum Schlüssel | `DialogNotFoundException` | `404 Not Found` |
| Unbekannte Session-Id | `SessionNotFoundException` | `404 Not Found` |
| Antwort verletzt Typ/Regeln der Frage | `AnswerValidationException` | `400 Bad Request` (`ValidationProblem`) |
| Pflichtfeld fehlt (`[Required]`) | `ValidationException` | `400 Bad Request` |
| Session nicht offen / falsche Frage / Fehlkonfiguration | `InvalidOperationException` | `409 Conflict` |

## Verifikation

Die Endpunkte sind über einen In-Process-`TestServer` (echte HTTP-Aufrufe, SQLite in-memory,
Docker-frei) end-to-end abgesichert:
`tests/Flirty.Tests/AspNetCore/MapFlirtyEndpointsTests.cs`.

```pwsh
dotnet test Flirty.sln -c Release
```
