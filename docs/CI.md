# CI-Pipeline

Wie die Continuous-Integration-Pipeline die Flirty-Pakete baut, testet und als Artefakte erzeugt.
Umgesetzt in Issue **#16**. Der Workflow liegt in `.github/workflows/ci.yml` (GitHub Actions).

## Wann läuft die Pipeline?

| Trigger | Bedingung |
|---|---|
| `push` | Commits auf `main` |
| `pull_request` | PRs gegen `main` |
| `workflow_dispatch` | manuell über den Actions-Tab |

Ein `concurrency`-Guard bricht überholte Läufe desselben Refs ab (`cancel-in-progress`). Die
Berechtigungen sind auf `contents: read` minimiert – die CI baut und testet nur, sie schreibt nichts
zurück.

## Ablauf

Läuft auf `ubuntu-latest`. Das .NET-SDK kommt über `actions/setup-dotnet` **aus `global.json`**
(Untergrenze `10.0.100`, `rollForward: latestFeature`) – so bleibt die CI mit dem lokalen Build synchron.
`10.0.100` ist bewusst nur die Untergrenze: `latestFeature` nimmt lokal jedes höhere installierte
10.0.x-SDK, während die CI reproduzierbar genau die angegebene Version installiert.

```
restore  ->  build -c Release  ->  test -c Release  ->  pack -c Release  ->  Artefakt-Upload
```

- `dotnet restore Flirty.sln`
- `dotnet build Flirty.sln -c Release --no-restore`
- `dotnet test tests/Flirty.Tests -c Release --no-build`
- `dotnet test tests/Flirty.E2E -c Release --no-build`
- `dotnet pack Flirty.sln -c Release --no-build -o artifacts`

Die Kette nutzt bewusst `--no-restore`/`--no-build`: jeder Schritt baut auf dem Output des vorherigen
auf. Dadurch wird **einmal** kompiliert, und die getesteten Binaries sind identisch mit den gepackten.

Der E2E-Schritt deckt beide Oberflächen ab: die Chat-UI der Web-Sample (sieben Tests, #45/#47) und den
Blazor-Designer (zwei Tests, #46). Beide Suiten hosten ihre App in-Prozess auf einem eigenen Kestrel-Port;
der vorgelagerte Schritt „Playwright-Browser installieren" liefert das Chromium dazu. Fehlt es,
überspringen sich die Tests (`SkippableFact`), statt zu scheitern. Aus demselben Grund wie bei den zwei
Test-Schritten laufen auch die beiden Suiten **innerhalb** der E2E-Assembly nacheinander
(`DisableTestParallelization` in `tests/Flirty.E2E/AssemblyInfo.cs`) – sonst konkurrierten zwei Kestrel
und zwei Browser um die zwei Kerne des Runners.

**Warum zwei Test-Schritte statt `dotnet test Flirty.sln`?** Die Solution-Variante startet beide
Test-Assemblies **parallel**. Die Playwright-E2E hostet ein echtes Kestrel und steuert einen Browser –
läuft parallel dazu die Unit-Suite (inklusive der Testcontainers-Tests für PostgreSQL/SQL Server), teilen
sich beide die zwei Kerne des Runners, und die E2E läuft in Playwright-Timeouts. Nacheinander ausgeführt
ist die E2E-Laufzeit unabhängig davon, wie groß die Unit-Suite gerade ist; die Abdeckung ändert sich nicht.

Da `pack` auf der **Solution** läuft und nur `Flirty` sowie `Flirty.AspNetCore` `IsPackable=true` tragen,
entstehen automatisch genau diese beiden Pakete – je ein `.nupkg` **und** ein `.snupkg` (Symbolpaket).
Siehe [NUGET-PACKAGING.md](./NUGET-PACKAGING.md).

## Versionierung im CI

Die datumsbasierte Version (`JJJJMM.Revision`, siehe [NUGET-PACKAGING.md](./NUGET-PACKAGING.md)) wird
über die **Build-/Run-Nummer** eindeutig gemacht. Der Workflow setzt dazu die Environment-Variable
`BuildRevision: ${{ github.run_number }}`.

MSBuild liest Environment-Variablen als Properties, deshalb greift dieser Wert **ohne** zusätzliches
`-p:BuildRevision=…` an jedem Befehl – und zwar konsistent für `build` **und** `pack`, sodass
Assembly-Version und Paketversion dieselbe Revision tragen.

`ContinuousIntegrationBuild` (deterministische Pfade für SourceLink) aktiviert sich automatisch, weil
GitHub Actions `CI=true` setzt.

> `TreatWarningsAsErrors=true` gilt repo-weit und **CS1591=Error** für die packbaren Libraries – fehlt
> öffentliche XML-Doku oder gibt es eine `pack`-Warnung (NU5xxx), bricht die Pipeline. Die
> Doku-Pflicht aus der Definition of Done wird also bereits vom Build erzwungen.

## Artefakte

Der letzte Schritt lädt beide Pakete unter dem Artifact-Namen **`nupkg`** hoch:

```
artifacts/*.nupkg      Flirty.202607.<run>.nupkg      Flirty.AspNetCore.202607.<run>.nupkg
artifacts/*.snupkg     Flirty.202607.<run>.snupkg     Flirty.AspNetCore.202607.<run>.snupkg
```

`if-no-files-found: error` lässt die Pipeline fehlschlagen, falls kein Paket entsteht – damit ist das
Akzeptanzkriterium „Artefakte = beide `.nupkg`" hart abgesichert.

## Abgrenzung

- **Kein** `dotnet nuget push`: Das Veröffentlichen auf NuGet.org/Azure Artifacts ist bewusst nicht Teil
  dieses Stubs und folgt in **#49**.
- **Kein** Coverage-Report: Der Coverage-Collector (`coverlet.collector`) ist eingebunden, der
  CI-Report kommt aber erst mit **#48**.
