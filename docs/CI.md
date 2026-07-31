# CI Pipeline

How the continuous-integration pipeline builds and tests the Flirty packages and produces them as artifacts.
Implemented in issue **#16**. The workflow lives in `.github/workflows/ci.yml` (GitHub Actions).

The repo has **two** workflows: `ci.yml` (this one here – builds, tests, packs, uploads) and
`release.yml` (#49 – publishes, manually and behind an approval gate, see
[below](#the-second-workflow-release-49)).

## When does the pipeline run?

| Trigger | Condition |
|---|---|
| `push` | commits on `main` |
| `pull_request` | PRs against `main` |
| `workflow_dispatch` | manually via the Actions tab |

A `concurrency` guard cancels overtaken runs of the same ref (`cancel-in-progress`). The
permissions are minimized to `contents: read` – the CI only builds and tests, it writes nothing
back.

## Flow

Runs on `ubuntu-latest`. The .NET SDK comes via `actions/setup-dotnet` **from `global.json`**
(lower bound `10.0.100`, `rollForward: latestFeature`) – so the CI stays in sync with the local build.
`10.0.100` is deliberately only the lower bound: `latestFeature` takes locally any higher installed
10.0.x SDK, while the CI reproducibly installs exactly the specified version.

```
restore  ->  build -c Release  ->  test -c Release  ->  coverage report  ->  pack -c Release  ->  artifact upload
```

- `dotnet restore Flirty.sln`
- `dotnet tool restore` (local tools from `.config/dotnet-tools.json`, here `reportgenerator`)
- `dotnet build Flirty.sln -c Release --no-restore`
- `dotnet test tests/Flirty.Tests -c Release --no-build --collect:"XPlat Code Coverage" --settings coverage.runsettings --results-directory artifacts/coverage/unit`
- `dotnet test tests/Flirty.E2E -c Release --no-build`
- `dotnet reportgenerator …` (see [Coverage](#coverage))
- `dotnet pack Flirty.sln -c Release --no-build -o artifacts`

The chain deliberately uses `--no-restore`/`--no-build`: each step builds on the output of the previous
one. That way it is compiled **once**, and the tested binaries are identical with the packed ones.

The E2E step covers both surfaces: the chat UI of the web sample (#45/#47) and the
Blazor designer (#46 for the forms, #101–#105 for the graph canvas). Deliberately without test counts – the
suite grows, a number here goes stale silently. Both suites host their app in-process on their own Kestrel port;
the upstream step "install Playwright browsers" supplies the Chromium for it. If it is missing, the
tests skip themselves (`SkippableFact`) instead of failing. For the same reason as with the two
test steps, the two suites **within** the E2E assembly also run sequentially
(`DisableTestParallelization` in `tests/Flirty.E2E/AssemblyInfo.cs`) – otherwise two Kestrels
and two browsers would compete for the two cores of the runner.

**Why two test steps instead of `dotnet test Flirty.sln`?** The solution variant starts both
test assemblies **in parallel**. The Playwright E2E hosts a real Kestrel and drives a browser –
if the unit suite runs in parallel with it (including the Testcontainers tests for PostgreSQL/SQL Server), both
share the two cores of the runner, and the E2E runs into Playwright timeouts. Run sequentially,
the E2E runtime is independent of how large the unit suite currently is; the coverage does not change.

Since `pack` runs on the **solution** and only `Flirty`, `Flirty.AspNetCore` and `Flirty.Mcp` carry
`IsPackable=true`, exactly those three packages arise automatically – each a `.nupkg` **and** a `.snupkg`
(symbol package). See [NUGET-PACKAGING.md](./NUGET-PACKAGING.md).

## Coverage

Implemented in **#48**. Measured with `coverlet.collector` (the XPlat collector of the VSTest host),
processed with **ReportGenerator**, which is pinned as a local tool in `.config/dotnet-tools.json` –
the same pattern as `dotnet ef`, so that the report version stands in the repo and the report is
reproducible 1:1 locally.

### What gets measured

The filters are centralized in **`coverage.runsettings`** at the repo root, so that CI and local run
deliver the same numbers. Measured are **only the NuGet packages**:

| Assembly | in the report | Why |
|---|---|---|
| `Flirty` | **yes** | is shipped |
| `Flirty.AspNetCore` | **yes** | is shipped |
| `Flirty.Mcp` | **yes** | is shipped (#126) |
| `Flirty.Migrations.*` | no | generated EF code, no significance |
| `Flirty.Samples`, `Flirty.Samples.Web` | no | demo applications, not the product |
| `Flirty.Designer` | no | own app, not a package |

**A new packable project must be added to `<Include>`.** There is no gate for this: an unlisted assembly
is simply not instrumented, so it is missing from the job summary and the HTML report without a single
warning – it reads as "not covered by any test" being invisible rather than as zero. `Flirty.Mcp` was the
first project to walk into that trap (#126).

Additionally out of the quota: compiler-generated and `[Obsolete]`-marked members
(`ExcludeByAttribute`) as well as auto-properties (`SkipAutoProps`) – the latter have no code path
of their own and would artificially inflate the number given the many `sealed record`s in the domain model.

The collector is **not** activated in the runsettings but stays bound to `--collect`:
an ordinary `dotnet test` should not instrument unasked.

> **When editing the runsettings:** XML comments must not contain two consecutive
> hyphens. VSTest otherwise rejects the file with "Settings file provided does not conform
> to required format" – a `--collect` in a comment is already enough.

### Why only the unit suite is instrumented

Coverage is collected **only** from `tests/Flirty.Tests`, not from the E2E suite. Two reasons, both
measured:

1. **It brings nothing.** The E2E suite does drive the core through the designer's gateways and the
   endpoints of the web sample – but each of those paths is already covered by the unit suite (463 tests).
   Merged, the E2E raised the branch coverage from 368 to 369 of 430 branches.
2. **It is unreliable there.** In the E2E output directory coverlet fails on
   `Flirty.dll` (`Unable to instrument module`): its assembly resolver does not find
   `Microsoft.Extensions.DependencyInjection.Abstractions`, which in this composition comes solely from
   the shared framework `Microsoft.AspNetCore.App`. The core was therefore missing from the E2E partial report
   **silently**. A report that unnoticedly loses an assembly is worse than none.

On top of that: instrumentation costs runtime, and the E2E is the run that on the two cores of the
runner is the most likely to tip into Playwright timeouts anyway (see above).

### Collector version

`coverlet.collector` is pinned to **10.0.1** and must follow the .NET line. The **6.0.4** originating
from the xUnit template (.NET 8 era) could not instrument the `net10.0`-compiled `Flirty.dll` and
delivered a report **without the core** – so without exactly what is meant to be measured.
The failure case is treacherous, because the run stays green and only one assembly is missing:
when raising the TFM it is worth checking whether the line "Assemblies: 2" in `Summary.txt` still holds.

### Report and publication

```pwsh
dotnet reportgenerator `
  -reports:artifacts/coverage/unit/**/coverage.cobertura.xml `
  -targetdir:artifacts/coverage/report `
  -reporttypes:"Html;Cobertura;MarkdownSummaryGithub;TextSummary" `
  -sourcedirs:<repo root> -title:"Flirty"
```

`-sourcedirs` is necessary because `Directory.Build.targets` sets `ContinuousIntegrationBuild=true` for
the packable projects in CI: together with SourceLink the source paths are normalized to `/_/…`.
(`UseSourceLink` is deliberately set to `false` in the runsettings – switched on, coverlet writes
`raw.githubusercontent` URLs on the commit of `HEAD` into the report, which ReportGenerator does not
fetch; the HTML report then showed no source code.)

Publication happens in two ways, both without additional permissions:

- **Job summary** – `SummaryGithub.md` is appended to `$GITHUB_STEP_SUMMARY` and thus stands
  directly on the overview page of the Actions run.
- **Artifact `coverage`** – the full HTML report plus the merged `Cobertura.xml`
  for download.

`permissions` stays at `contents: read` throughout.

## Versioning in CI

The date-based version (`YYYYMM.Revision`, see [NUGET-PACKAGING.md](./NUGET-PACKAGING.md)) is
made unique via the **build/run number**. For that the workflow sets the environment variable
`BuildRevision: ${{ github.run_number }}`.

MSBuild reads environment variables as properties, so this value takes effect **without** an
additional `-p:BuildRevision=…` on every command – and consistently for `build` **and** `pack`, so that
the assembly version and package version carry the same revision.

`ContinuousIntegrationBuild` (deterministic paths for SourceLink) activates automatically, because
GitHub Actions sets `CI=true`.

> `TreatWarningsAsErrors=true` applies repo-wide and **CS1591=Error** for the packable libraries – if
> public XML docs are missing or there is a `pack` warning (NU5xxx), the pipeline breaks. The
> documentation obligation from the Definition of Done is thus already enforced by the build.

## Artifacts

A run uploads two artifacts: **`coverage`** (HTML report, see [Coverage](#coverage)) and
**`nupkg`** with all packages:

```
artifacts/*.nupkg      Flirty.202607.<run>.0.nupkg      Flirty.AspNetCore.202607.<run>.0.nupkg      Flirty.Mcp.202607.<run>.0.nupkg
artifacts/*.snupkg     Flirty.202607.<run>.0.snupkg     Flirty.AspNetCore.202607.<run>.0.snupkg     Flirty.Mcp.202607.<run>.0.snupkg
```

`if-no-files-found: error` makes the pipeline fail if no package arises – with that the
acceptance criterion "artifacts = the `.nupkg`" is hardly secured. The coverage upload carries the same
setting: if the report stays empty, it is noticed instead of vanishing silently.

## The second workflow: Release (#49)

Next to `ci.yml` lies `.github/workflows/release.yml` – the **only** workflow that writes anything
outward (`dotnet nuget push` to NuGet.org). It is triggered only manually (`workflow_dispatch`) and
its push job hangs off the environment `nuget` (secret + optional reviewer gate). The full
flow stands in [NUGET-PACKAGING.md § Publishing](./NUGET-PACKAGING.md#publishing-49).

One difference is relevant here: the release run runs **only the unit suite** (`tests/Flirty.Tests`,
without coverage), not the E2E. It rebuilds the binaries – therefore it must test *those* –, but the
rationale from ["Why only the unit suite is instrumented"](#why-only-the-unit-suite-is-instrumented)
holds: the E2E covers no path of the packages additionally (measured: one branch of 430) and
costs a browser installation. The full coverage including E2E was already delivered by the `ci.yml` run on the same
commit.

## Scope / boundaries

- **No** `dotnet nuget push` **in the CI**: publishing lives since **#49** in its own workflow
  `.github/workflows/release.yml` – triggered manually and behind an approval gate, because a version
  published on NuGet.org is irreversible. The CI still only builds, tests and packs.
  Details: [NUGET-PACKAGING.md § Publishing](./NUGET-PACKAGING.md#publishing-49).
- **No threshold gate:** the pipeline reports the coverage but does not (yet) break below
  a quota. A floor value should rest on the really measured state, not on an estimate
  – otherwise the pipeline breaks on the day of introduction at a guessed number.
- **No coverage badge and no external service** (Codecov or similar): a badge would need a commit
  back into the repo (`contents: write`), an external service a secret and the transmission of the data
  outward. Both contradict "the CI only builds and tests, it writes nothing back".
- **No PR comment:** would need `pull-requests: write` and would not work for fork PRs with the
  default token anyway. The job summary does the same thing permission-free.
