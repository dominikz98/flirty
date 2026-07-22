# ADR 0003 – ASP.NET-freier Core, Web als opt-in-Paket

- **Status:** Akzeptiert
- **Kontext-Issue:** #13 – Projekt-Skelette + Solution-Verdrahtung (umgesetzt in #35/#36)
- **Betroffen:** `src/Flirty`, `src/Flirty.AspNetCore`, `src/Flirty.Samples`, `src/Flirty.Samples.Web`

## Kontext

Flirty soll in einer Console-/Worker-App genauso laufen wie hinter einer WebAPI – ein
Onboarding-Dialog in einem Hintergrunddienst ist ein ebenso gültiger Anwendungsfall wie ein
Chat-Widget im Browser. Gleichzeitig sollen Konsumenten, die *doch* HTTP wollen, keine
Endpunkte von Hand schreiben müssen.

Eine ASP.NET-Referenz im Core hätte zwei Wirkungen, die beide beim Konsumenten anfallen: Jede
Console-App zöge das Shared Framework `Microsoft.AspNetCore.App` mit, und die Fehlersemantik der
Engine würde HTTP-gefärbt (Status-Codes statt Ausnahmen), obwohl es dort kein HTTP gibt.

## Entscheidung

`Flirty` ist eine **reine Class-Library ohne ASP.NET-Abhängigkeit**. Alles Web-Spezifische liegt im
**separaten, opt-in-Paket `Flirty.AspNetCore`**, das als einziges Projekt
`<FrameworkReference Include="Microsoft.AspNetCore.App" />` setzt.

Die Web-Schicht bleibt bewusst **dünn**: `MapFlirtyEndpoints` (Runtime) und
`MapFlirtyAdminEndpoints` (Admin-CRUD, opt-in und per `RequireAuthorization()` absicherbar) senden
die Commands/Queries direkt per `ISender` und mappen nur auf Request-/Response-DTOs. Fachliche
Ausnahmen der Engine übersetzt ein einziger Endpunkt-Filter
(`src/Flirty.AspNetCore/FlirtyExceptionEndpointFilter.cs`) nach `ProblemDetails` – 404 (nicht
gefunden), 400 (Validierung), 409 (Zustandskonflikt). **Es gibt keine Logik, die nur über HTTP
erreichbar ist.**

## Verworfene Alternativen

- **Ein Paket mit ASP.NET-Referenz.** Console-/Worker-Konsumenten zögen das ASP.NET-Shared-Framework
  mit, obwohl sie es nie nutzen. Schwerer wiegt der Entwurfsschaden: Die Engine würde in
  HTTP-Begriffen antworten (Status-Codes, `ProblemDetails`), und diese Semantik müsste in der
  Console künstlich zurückübersetzt werden.
- **Ein Paket, Endpunkte per `#if` oder zusätzlichem Target-Framework.** Der Konsument müsste die
  Variante zur **Bauzeit** wählen, die Testmatrix verdoppelt sich, und die öffentliche API des
  Pakets wäre je nach Build eine andere – für ein NuGet-Paket die schlechteste aller Varianten.
- **ASP.NET „weich" referenzieren** (z. B. `PrivateAssets`). Löst nichts: Sobald Endpunkt-Typen in
  öffentlichen Signaturen auftauchen (`IEndpointRouteBuilder`), ist die Abhängigkeit real.
- **Gar keine Endpunkte ausliefern**, jeder baut sie selbst. Genau die repetitive Arbeit, die Flirty
  abnehmen soll – Endpunkt-Mapping, DTOs und Fehlerabbildung sind für jeden Konsumenten dieselben.

## Konsequenzen

**Positiv**

- Die Invariante wird bei **jedem Build** überprüft, nicht nur behauptet: `Flirty.Samples` ist ein
  lauffähiges Console-Sample, dessen einzige Projektreferenz `Flirty` ist. Schliche eine
  ASP.NET-Abhängigkeit in den Core, fiele es dort auf.
- Die Fehlersemantik der Engine ist **transportneutral** (Ausnahmen), die Übersetzung nach HTTP
  liegt an genau einer Stelle.
- Verstärkt durch [ADR 0002](./0002-mediator-als-in-process-bus.md): Weil der Mediator-Source-
  Generator nur die Core-Compilation sieht, **kann** `Flirty.AspNetCore` keine Handler beitragen –
  die Schicht bleibt technisch erzwungen dünn.

**Negativ**

- Zwei Pakete sind zu versionieren, zu paketieren und zu veröffentlichen (gemeinsame,
  datumsbasierte Version, siehe [NUGET-PACKAGING.md](../NUGET-PACKAGING.md)).
- Die DTO-/Mapping-Schicht in `Flirty.AspNetCore` ist zusätzlicher Code, der bei jeder Änderung an
  einem Runtime- oder Admin-Command mitgezogen werden muss.

Details: [ARCHITECTURE.md](../ARCHITECTURE.md), [GETTING-STARTED-WebApi.md](../GETTING-STARTED-WebApi.md),
Console-Seite in [GETTING-STARTED-Console.md](../GETTING-STARTED-Console.md).
