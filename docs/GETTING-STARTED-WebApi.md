# Getting Started – WebAPI (Flirty.AspNetCore)

> Stand: Issues #35 (Laufzeit-Endpunkte) & #36 (Admin-CRUD). Dieser Guide zeigt, wie man die Flirty-Engine als HTTP-API bereitstellt – über das
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

## 4. Admin-CRUD (optional)

Neben den Laufzeit-Endpunkten stellt das Paket optionale **Admin-CRUD-Endpunkte** bereit, um den
Konfigurationsgraphen (Dialoge, Fragen, Optionen, Übergänge) per HTTP zu pflegen. Sie werden über eine
**eigene, opt-in** Erweiterungsmethode registriert – so lässt sich die Admin-Fläche gezielt absichern,
ohne die öffentlichen Laufzeit-Endpunkte einzuschränken:

```csharp
app.MapFlirtyEndpoints("/flirty");                       // Laufzeit (Sessions)
app.MapFlirtyAdminEndpoints("/flirty/admin")             // Konfiguration (Admin)
   .RequireAuthorization();                              // dringend empfohlen
```

Alle Endpunkte sind – wie die Laufzeit-Seite – eine dünne Schicht über Mediator-Commands (`ISender`)
und teilen sich denselben Fehler-Filter. Kind-Ressourcen sind hierarchisch unter dem Dialog adressiert
und werden über `GET {prefix}/dialogs/{id}` (kompletter Graph) gelesen.

| Methode & Route | Zweck | Erfolg |
|---|---|---|
| `POST /flirty/admin/dialogs` | Dialog anlegen (Version 1, unveröffentlicht) | `201 Created` + `Location`, `DialogResponse` |
| `GET /flirty/admin/dialogs` | Dialoge auflisten (Metadaten) | `200 OK`, `DialogResponse[]` |
| `GET /flirty/admin/dialogs/{id}` | Dialog samt Graph lesen | `200 OK`, `DialogDetailResponse` |
| `PUT /flirty/admin/dialogs/{id}` | Metadaten/`StartQuestionId` ändern | `200 OK`, `DialogResponse` |
| `DELETE /flirty/admin/dialogs/{id}` | Dialog + Graph löschen | `204 No Content` |
| `POST /flirty/admin/dialogs/{id}/publish` \| `/unpublish` | Veröffentlichung steuern | `200 OK`, `DialogResponse` |
| `POST /flirty/admin/dialogs/{dialogId}/questions` | Frage anlegen | `201 Created`, `QuestionResponse` |
| `PUT` \| `DELETE .../questions/{questionId}` | Frage ändern/löschen | `200 OK` \| `204 No Content` |
| `POST .../questions/{questionId}/options` | Option anlegen | `201 Created`, `AnswerOptionResponse` |
| `PUT` \| `DELETE .../options/{optionId}` | Option ändern/löschen | `200 OK` \| `204 No Content` |
| `POST /flirty/admin/dialogs/{dialogId}/transitions` | Übergang anlegen | `201 Created`, `TransitionResponse` |
| `PUT` \| `DELETE .../transitions/{transitionId}` | Übergang ändern/löschen | `200 OK` \| `204 No Content` |

### Ablauf: Dialog aufbauen und veröffentlichen

Die Laufzeit startet nur **veröffentlichte** Dialoge. Ein per API aufgebauter Dialog wird also so
startbar:

1. `POST /flirty/admin/dialogs` – Dialog anlegen.
2. `POST .../questions` (+ `.../options` für Auswahl-Typen) – Fragen/Optionen ergänzen.
3. `PUT /flirty/admin/dialogs/{id}` – `startQuestionId` auf die Einstiegsfrage setzen.
4. `POST .../transitions` – Verzweigungen ergänzen (optional bei nur einer, terminalen Frage).
5. `POST /flirty/admin/dialogs/{id}/publish` – veröffentlichen; danach `POST /flirty/sessions` startbar.

### Fehler-Mapping (Admin)

Zusätzlich zum obigen Mapping gelten für das Admin-CRUD:

| Situation | Ausnahme | Status |
|---|---|---|
| Unbekannte Dialog-/Frage-/Options-/Übergang-Id (oder Kind fremd zum Eltern) | `ConfigurationNotFoundException` | `404 Not Found` |
| Doppelter Schlüssel (`Key` je Dialog / `(DialogId,Key)` / `(QuestionId,Key)`) | `InvalidOperationException` | `409 Conflict` |
| Veröffentlichen ohne gesetzte Einstiegsfrage | `InvalidOperationException` | `409 Conflict` |
| Fehlendes Pflichtfeld (`[Required]`) | `ValidationException` | `400 Bad Request` |

> **Hinweise / bewusste Grenzen:** Anlegen erzeugt stets `Version = 1` je Schlüssel; Editieren erfolgt
> In-Place (publizierte Dialoge vor Änderungen entpublishen). `Transition`-Verweise
> (`FromQuestionId`/`TargetQuestionId`) sind – dem FK-losen Domänenmodell entsprechend – rohe
> Frage-Verweise ohne Existenzprüfung; das Löschen einer Frage bereinigt jedoch verweisende Übergänge
> und setzt eine darauf zeigende `StartQuestionId` zurück. Versionierung/Copy-on-Write sowie
> Loop-/Trigger-CRUD sind nicht Teil dieses Endpunkt-Sets.

## Verifikation

Die Endpunkte sind über einen In-Process-`TestServer` (echte HTTP-Aufrufe, SQLite in-memory,
Docker-frei) end-to-end abgesichert – die Laufzeit-Endpunkte in
`tests/Flirty.Tests/AspNetCore/MapFlirtyEndpointsTests.cs`, das Admin-CRUD in
`tests/Flirty.Tests/AspNetCore/MapFlirtyAdminEndpointsTests.cs` (inkl. eines End-to-End-Tests, der
einen rein per API aufgebauten, veröffentlichten Dialog anschließend über `POST /flirty/sessions`
startet). Beide teilen sich den Test-Host `tests/Flirty.Tests/AspNetCore/FlirtyTestHost.cs`.

```pwsh
dotnet test Flirty.sln -c Release
```
