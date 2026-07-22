# CI-Pipeline

Wie die Continuous-Integration-Pipeline die Flirty-Pakete baut, testet und als Artefakte erzeugt.
Umgesetzt in Issue **#16**. Der Workflow liegt in `.github/workflows/ci.yml` (GitHub Actions).

Das Repo hat **zwei** Workflows: `ci.yml` (dieser hier – baut, testet, packt, lädt hoch) und
`release.yml` (#49 – veröffentlicht, manuell und hinter einem Freigabe-Gate, siehe
[unten](#der-zweite-workflow-release-49)).

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
restore  ->  build -c Release  ->  test -c Release  ->  Coverage-Report  ->  pack -c Release  ->  Artefakt-Upload
```

- `dotnet restore Flirty.sln`
- `dotnet tool restore` (lokale Tools aus `.config/dotnet-tools.json`, hier `reportgenerator`)
- `dotnet build Flirty.sln -c Release --no-restore`
- `dotnet test tests/Flirty.Tests -c Release --no-build --collect:"XPlat Code Coverage" --settings coverage.runsettings --results-directory artifacts/coverage/unit`
- `dotnet test tests/Flirty.E2E -c Release --no-build`
- `dotnet reportgenerator …` (siehe [Coverage](#coverage))
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

## Coverage

Umgesetzt in **#48**. Gemessen wird mit `coverlet.collector` (XPlat-Collector des VSTest-Hosts),
aufbereitet mit **ReportGenerator**, das als lokales Tool in `.config/dotnet-tools.json` gepinnt ist –
gleiches Muster wie `dotnet ef`, damit die Report-Version im Repo steht und der Report lokal 1:1
reproduzierbar ist.

### Was gemessen wird

Die Filter stehen zentral in **`coverage.runsettings`** im Repo-Root, damit CI und lokaler Lauf
dieselben Zahlen liefern. Gemessen werden **nur die beiden NuGet-Pakete**:

| Assembly | im Report | Warum |
|---|---|---|
| `Flirty` | **ja** | wird ausgeliefert |
| `Flirty.AspNetCore` | **ja** | wird ausgeliefert |
| `Flirty.Migrations.*` | nein | generierter EF-Code, keine Aussagekraft |
| `Flirty.Samples`, `Flirty.Samples.Web` | nein | Demo-Anwendungen, nicht das Produkt |
| `Flirty.Designer` | nein | eigene App, kein Paket |

Zusätzlich aus der Quote heraus: compiler-generierte und als `[Obsolete]` markierte Member
(`ExcludeByAttribute`) sowie Auto-Properties (`SkipAutoProps`) – letztere haben keinen eigenen
Code-Pfad und würden die Zahl bei den vielen `sealed record`s im Domänenmodell künstlich aufblähen.

Der Collector wird in der Runsettings **nicht** aktiviert, sondern bleibt an `--collect` gebunden:
ein gewöhnliches `dotnet test` soll nicht ungefragt instrumentieren.

> **Beim Bearbeiten der Runsettings:** XML-Kommentare dürfen keine zwei aufeinanderfolgenden
> Bindestriche enthalten. VSTest lehnt die Datei sonst mit „Settings file provided does not conform
> to required format" ab – ein `--collect` im Kommentar reicht schon.

### Warum nur die Unit-Suite instrumentiert wird

Coverage wird **nur** aus `tests/Flirty.Tests` gesammelt, nicht aus der E2E-Suite. Zwei Gründe, beide
gemessen:

1. **Sie bringt nichts.** Die E2E-Suite treibt den Core zwar durch die Gateways des Designers und die
   Endpunkte der Web-Sample – aber jeden dieser Pfade deckt die Unit-Suite (463 Tests) schon ab.
   Zusammengeführt hob die E2E die Branch-Coverage von 368 auf 369 von 430 Zweigen.
2. **Sie ist dort unzuverlässig.** Im E2E-Ausgabeverzeichnis scheitert coverlet an
   `Flirty.dll` (`Unable to instrument module`): sein Assembly-Resolver findet
   `Microsoft.Extensions.DependencyInjection.Abstractions` nicht, die in dieser Komposition allein aus
   dem Shared Framework `Microsoft.AspNetCore.App` kommt. Der Core fehlte im E2E-Teilreport also
   **stillschweigend**. Ein Report, der eine Assembly unbemerkt verliert, ist schlechter als keiner.

Dazu kommt: Instrumentierung kostet Laufzeit, und die E2E ist der Lauf, der auf den zwei Kernen des
Runners ohnehin am ehesten in Playwright-Timeouts kippt (siehe oben).

### Version des Collectors

`coverlet.collector` ist auf **10.0.1** gepinnt und muss der .NET-Linie folgen. Die aus dem
xUnit-Template stammende **6.0.4** (.NET-8-Ära) konnte das `net10.0`-kompilierte `Flirty.dll` nicht
instrumentieren und lieferte einen Report **ohne den Core** – also ohne genau das, was gemessen
werden soll. Der Fehlerfall ist tückisch, weil der Lauf grün bleibt und nur eine Assembly fehlt:
Beim Anheben des TFM lohnt der Blick, ob die Zeile „Assemblies: 2" im `Summary.txt` noch stimmt.

### Report und Veröffentlichung

```pwsh
dotnet reportgenerator `
  -reports:artifacts/coverage/unit/**/coverage.cobertura.xml `
  -targetdir:artifacts/coverage/report `
  -reporttypes:"Html;Cobertura;MarkdownSummaryGithub;TextSummary" `
  -sourcedirs:<Repo-Root> -title:"Flirty"
```

`-sourcedirs` ist nötig, weil `Directory.Build.targets` für die packbaren Projekte im CI
`ContinuousIntegrationBuild=true` setzt: zusammen mit SourceLink werden die Quellpfade auf `/_/…`
normalisiert. (`UseSourceLink` steht in der Runsettings bewusst auf `false` – eingeschaltet schreibt
coverlet `raw.githubusercontent`-URLs auf den Commit von `HEAD` in den Report, die ReportGenerator
nicht nachlädt; der HTML-Report zeigte dann keinen Quellcode.)

Veröffentlicht wird auf zwei Wegen, beide ohne zusätzliche Rechte:

- **Job Summary** – `SummaryGithub.md` wird an `$GITHUB_STEP_SUMMARY` angehängt und steht damit
  direkt auf der Übersichtsseite des Actions-Laufs.
- **Artefakt `coverage`** – der vollständige HTML-Report plus die zusammengeführte `Cobertura.xml`
  zum Herunterladen.

`permissions` bleibt dabei auf `contents: read`.

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

Ein Lauf lädt zwei Artefakte hoch: **`coverage`** (HTML-Report, siehe [Coverage](#coverage)) und
**`nupkg`** mit beiden Paketen:

```
artifacts/*.nupkg      Flirty.202607.<run>.0.nupkg      Flirty.AspNetCore.202607.<run>.0.nupkg
artifacts/*.snupkg     Flirty.202607.<run>.0.snupkg     Flirty.AspNetCore.202607.<run>.0.snupkg
```

`if-no-files-found: error` lässt die Pipeline fehlschlagen, falls kein Paket entsteht – damit ist das
Akzeptanzkriterium „Artefakte = beide `.nupkg`" hart abgesichert. Der Coverage-Upload trägt dieselbe
Einstellung: bleibt der Report leer, fällt es auf, statt still zu verschwinden.

## Der zweite Workflow: Release (#49)

Neben `ci.yml` liegt `.github/workflows/release.yml` – der **einzige** Workflow, der etwas nach außen
schreibt (`dotnet nuget push` auf NuGet.org). Er wird nur manuell ausgelöst (`workflow_dispatch`) und
sein Push-Job hängt an der Environment `nuget` (Secret + optionales Reviewer-Gate). Der vollständige
Ablauf steht in [NUGET-PACKAGING.md § Publizieren](./NUGET-PACKAGING.md#publizieren-49).

Ein Unterschied ist hier relevant: Der Release-Lauf fährt **nur die Unit-Suite** (`tests/Flirty.Tests`,
ohne Coverage), nicht die E2E. Er baut die Binaries neu – deshalb muss er *diese* testen –, aber die
Begründung aus [„Warum nur die Unit-Suite instrumentiert wird"](#warum-nur-die-unit-suite-instrumentiert-wird)
trägt: die E2E deckt keinen Pfad der beiden Pakete zusätzlich ab (gemessen: ein Zweig von 430) und
kostet Browser-Installation. Die volle Abdeckung inklusive E2E hat der `ci.yml`-Lauf auf demselben
Commit bereits geliefert.

## Abgrenzung

- **Kein** `dotnet nuget push` **in der CI**: Das Veröffentlichen liegt seit **#49** in einem eigenen Workflow
  `.github/workflows/release.yml` – manuell ausgelöst und hinter einem Freigabe-Gate, weil eine auf
  NuGet.org veröffentlichte Version unwiderruflich ist. Die CI baut, testet und packt weiterhin nur.
  Details: [NUGET-PACKAGING.md § Publizieren](./NUGET-PACKAGING.md#publizieren-49).
- **Kein Schwellwert-Gate:** Die Pipeline berichtet die Coverage, bricht aber (noch) nicht unterhalb
  einer Quote ab. Ein Boden-Wert soll auf dem real gemessenen Stand beruhen, nicht auf einer Schätzung
  – sonst bricht die Pipeline am Tag der Einführung an einer geratenen Zahl.
- **Kein Coverage-Badge und kein externer Dienst** (Codecov o. ä.): Ein Badge bräuchte einen Commit
  zurück ins Repo (`contents: write`), ein externer Dienst ein Secret und den Versand der Daten nach
  außen. Beides widerspricht „die CI baut und testet nur, sie schreibt nichts zurück".
- **Kein PR-Kommentar:** bräuchte `pull-requests: write` und funktionierte bei Fork-PRs mit dem
  Default-Token ohnehin nicht. Die Job Summary leistet dasselbe rechtefrei.
