---
name: flirty-runtime-command
description: Neue Engine-Operation (Mediator-Command/Query) in der Flirty-Runtime hinzufügen – inkl. Handler, DI-Registrierung, Test und optionalem ASP.NET-Endpunkt. Verwenden bei "neuer Command", "neue Query", "neue Runtime-Operation", "IFlirtyEngine erweitern", "neuen Endpunkt für eine Engine-Aktion".
---

# Neuen Runtime-Command/Query hinzufügen

Kanonischer Erweiterungspfad der Engine. Alle Engine-Operationen sind **Mediator-Commands/Queries**
und liegen im **Core** (`src/Flirty`), weil der Mediator-Source-Generator (martinothamar) Handler nur
in derselben Compilation entdeckt. Referenz: `docs/RUNTIME.md`, `docs/MEDIATOR.md`.

## Vorbilder (vor dem Schreiben lesen)

- `src/Flirty/Runtime/SubmitAnswerCommand.cs` – Command mit Ergebnis-Record, Persistenz + Branching.
- `src/Flirty/Runtime/ResumeDialogQuery.cs` – rein lesende Query (kein `SaveChangesAsync`).
- `src/Flirty/Runtime/TransitionResolver.cs` – geteilte Branching-Logik (nicht duplizieren).
- `src/Flirty/Runtime/IFlirtyEngine.cs` + `FlirtyEngine.cs` – Facade über `ISender`.
- `src/Flirty/DependencyInjection/FlirtyServiceCollectionExtensions.cs` – DI-Verdrahtung.

## Schritte

1. **Command/Query + Ergebnis** in `src/Flirty/Runtime/` anlegen:
   ```csharp
   public sealed record DoThingCommand(
       [property: Required] Guid SessionId,
       [property: Required] string Value) : ICommand<DoThingResult>;   // Query: : IQuery<TResult>

   public sealed record DoThingResult(Guid SessionId, bool IsCompleted);
   ```
   - `[Required]` greift über das `ValidationPipelineBehavior` gegen `null`/leere Strings, **nicht**
     gegen `Guid.Empty` (Werttyp) – leere Ids fachlich im Handler behandeln.
   - Rückgabe- und Frage-Sichten schlank halten (siehe `QuestionView`/`SessionAnswerView` in `RUNTIME.md`).

2. **Handler** (`internal sealed`, im selben Ordner):
   ```csharp
   internal sealed class DoThingCommandHandler(IDialogStore store, IExpressionEvaluator evaluator)
       : ICommandHandler<DoThingCommand, DoThingResult>
   {
       public async ValueTask<DoThingResult> Handle(DoThingCommand cmd, CancellationToken ct) { … }
   }
   ```
   - Persistenz **ausschließlich** über `IDialogStore` (nie `FlirtyDbContext` direkt im Handler).
   - Branching über den geteilten `TransitionResolver` auswerten, nicht neu implementieren.
   - Bekannte Fehlertypen wiederverwenden: `SessionNotFoundException`, `DialogNotFoundException`,
     `ConfigurationNotFoundException`, sonst `InvalidOperationException` bei Fehlkonfiguration.
   - Schreibende Handler: am Ende `SaveChangesAsync()`; Notifications **nach** dem Speichern publizieren
     (siehe Skill `flirty-trigger-notification`).

3. **Facade** (optional, wenn die Operation typisiert erreichbar sein soll): Methode in
   `IFlirtyEngine.cs` ergänzen und in `FlirtyEngine.cs` als dünnen `ISender.Send(...)`-Aufruf umsetzen.

4. **DI:** In der Regel **nichts** zu tun – Command-/Query-Handler registriert der Source-Generator
   automatisch. Nur ein neues **Pipeline-Behavior** oder eine **geschlossene** Behavior-Registrierung
   (wie `AnswerValidationPipelineBehavior` für Submit/Edit) muss manuell in
   `FlirtyServiceCollectionExtensions.cs` ergänzt werden.

5. **Test** in `tests/Flirty.Tests/Runtime/`: gegen SQLite in-memory durch die volle Pipeline via
   `IFlirtyEngine`. Deutsche, snake_case-artige Testnamen. Erfolgs- **und** Fehlerfälle abdecken.

6. **Endpunkt** (optional, nur wenn HTTP nötig): siehe Abschnitt unten.

## Optionaler ASP.NET-Endpunkt (`Flirty.AspNetCore`)

- Request-/Response-**DTO** in `src/Flirty.AspNetCore/Dtos/` (Admin: `Dtos/Admin/`).
- Route in `FlirtyEndpointRouteBuilderExtensions.cs` (Admin: `FlirtyAdminEndpointRouteBuilderExtensions.cs`)
  ergänzen: DTO → Command mappen, `ISender.Send(...)`, Ergebnis → Response mappen.
- Mapping in `src/Flirty.AspNetCore/Mapping/` (kein Auto-Mapper – Handmapping wie im Bestand).
- Engine-Ausnahmen werden vom `FlirtyExceptionEndpointFilter` auf `ProblemDetails` (404/400/409)
  abgebildet – neue Ausnahmetypen dort ergänzen, falls ein anderer Statuscode gewünscht ist.
- Endpunkt-Test in `tests/Flirty.Tests/AspNetCore/` über `FlirtyTestHost`.

## Definition of Done

Deutsche XML-Docs auf aller neuen public API (CS1591 ist Fehler in den packbaren Projekten) · Tests grün
· `docs/RUNTIME.md` (und ggf. `docs/GETTING-STARTED-WebApi.md`) aktualisiert.

## Verifikation

```pwsh
dotnet build Flirty.sln
dotnet test tests/Flirty.Tests
```
