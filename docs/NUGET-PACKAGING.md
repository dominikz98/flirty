# NuGet Packaging

How the Flirty packages are built – which projects get packaged, where the metadata lives
and how versioning works. Implemented in issue **#15**, publishing in **#49**
(see [Publishing](#publishing-49)).

## What gets packaged?

Exactly **three** projects are publishable (`IsPackable=true`):

| Package | Project | Purpose |
|---|---|---|
| `Flirty` | `src/Flirty` | Core engine (ASP.NET-free). |
| `Flirty.AspNetCore` | `src/Flirty.AspNetCore` | Optional ASP.NET Core endpoints. |
| `Flirty.Mcp` | `src/Flirty.Mcp` | Optional Model Context Protocol server (#126). |

Each of the three is **opt-in on the one below it**: web is optional over the core
([ADR 0003](./adr/0003-aspnet-free-core.md)), and MCP is a package of its own rather than a folder in the
web package for the same reason one layer out – otherwise the MCP SDK and its transitive
`Microsoft.Extensions.AI.Abstractions` would become hard dependencies of an already published package
([ADR 0009](./adr/0009-mcp-as-its-own-opt-in-package.md)). `Flirty.Mcp` references **only** `Flirty`, not
`Flirty.AspNetCore`.

The remaining eight projects of the solution (`Flirty.Designer`, the three `Flirty.Migrations.*`,
`Flirty.Samples`, `Flirty.Samples.Web`, `Flirty.Tests`, `Flirty.E2E`) inherit `IsPackable=false` from
`Directory.Build.props` or set it explicitly and produce **no** package. The migration assemblies
are shipped nonetheless – but as a DLL **inside** the `Flirty` package, not as a package of their own (see
[Bundled migration DLLs](#bundled-migration-dlls-20)).

## Where does the metadata live?

Centrally, so all packages stay consistent – per `.csproj` only the package identity remains.

| Location | Content |
|---|---|
| `Directory.Build.props` | Shared metadata (`Authors`, `Copyright`, `PackageProjectUrl`, `RepositoryUrl`, `PackageLicenseExpression=MIT`, `PackageIcon`, `PublishRepositoryUrl`, `EmbedUntrackedSources`) and the SourceLink `PackageReference`. Set unconditionally – inert on non-package projects. |
| `Directory.Build.targets` | Package **behavior** gated on `IsPackable=='true'`: symbol packages (`IncludeSymbols`/`snupkg`), `PackageReadmeFile`, `ContinuousIntegrationBuild` (CI only), the **date-based version** as well as the README/icon pack includes. Gated in `.targets`, because `IsPackable` is not set until the `.csproj` (same pattern as the CS1591 enforcement). |
| `Directory.Packages.props` | Centrally pinned version of `Microsoft.SourceLink.GitHub` (CPM is on). |
| `src/Flirty*/**.csproj` | Only `IsPackable`, `PackageId`, `Description`, `PackageTags`. |

License file: `LICENSE` (MIT) in the repo root. With `PackageLicenseExpression`, the SPDX identifier
suffices for NuGet; the file itself is not placed into the package. Icon: `icon.png` (128×128) in the root,
packed as `icon.png`. README: the root `README.md`.

### The root README is the package page (#52)

`PackageReadmeFile` makes the root `README.md` the **description page of every package** on nuget.org –
so it is not only the GitHub start page. There is no repo root directory there, so two
rules apply when editing:

- **Absolute links only.** `](docs/RUNTIME.md)` resolves on nuget.org against the package page and runs
  into the void. Repo contents are therefore linked as `https://github.com/dominikz98/flirty/blob/main/…`
  (the display text stays the repo path, so the file still reads like a repo README on GitHub).
- **Images only from allowed hosts.** nuget.org renders images/badges exclusively from an
  [allowlist](https://learn.microsoft.com/en-us/nuget/nuget-org/package-readme-on-nuget-org#allowed-domains-for-images-and-badges)
  (among others `img.shields.io`, `github.com/.../workflows/.../badge.svg`); relative image paths are not
  rendered at all. The warning about it is seen **only by the package owner**, and it can only be corrected
  with the next published version – which is why
  `tests/Flirty.Tests/Docs/PackageReadmeTests.cs` nails both rules as a test.

## Versioning (date-based)

No MinVer, no Git tags. The **NuGet package version** is `YYYYMM.Revision`:

```
202604.1   ->  year 2026, month 04, revision 1
```

- Year/month come from `System.DateTime.UtcNow` (at build time).
- The revision defaults to `1` and is overridden at build:

  ```pwsh
  dotnet pack -c Release -p:BuildRevision=7   # -> Flirty.202604.7.0.nupkg
  ```

  In CI, pass the build/run number through, for example.

### The third segment: NuGet normalizes

The MSBuild property `Version` is two-part (`202604.7`), but a **NuGet version has at least
three segments**. NuGet therefore silently normalizes to `202604.7.0` – everywhere the
version becomes visible to the outside:

| Location | Value |
|---|---|
| MSBuild property `Version` | `202604.7` |
| File name | `Flirty.202604.7.0.nupkg` |
| `<version>` in the `.nuspec` | `202604.7.0` |
| Display on nuget.org | `202604.7.0` |

For consumers this has no consequence – `dotnet add package Flirty --version 202604.7` is normalized by
NuGet to the same value. When looking up a specific version (artifact name, release log,
package page), the third segment is there, though, and searching for `Flirty.202604.7.nupkg` runs into the void.

### Why a second version for the assembly?

`AssemblyVersion` and `FileVersion` consist of four `UInt16` segments (each max. **65535**).
`202604` exceeds that and would be invalid. The assembly version is therefore **decoupled**:

```
AssemblyVersion / FileVersion = year.month.revision.0   (e.g. 2026.4.1.0)
```

All segments stay ≤ 65535. The meaningful, date-based number is carried by the NuGet package
(`Version` → also `PackageVersion`/`InformationalVersion`).

## Building

```pwsh
dotnet pack -c Release -o artifacts
```

Produces in `artifacts/` per package a `.nupkg` **and** a `.snupkg`:

```
Flirty.202604.1.0.nupkg              Flirty.202604.1.0.snupkg
Flirty.AspNetCore.202604.1.0.nupkg   Flirty.AspNetCore.202604.1.0.snupkg
```

For the remaining eight projects nothing is produced.

> Note: `TreatWarningsAsErrors=true` applies repo-wide and also fires on NuGet pack warnings
> (NU5xxx). License, icon and README are therefore fully set – were they missing, `pack` would break.

## Bundled migration DLLs (#20)

The `Flirty` package ships the three provider-separated migration assemblies along, so that
package consumers can auto-migrate via `o.ApplyMigrations()` without referencing the (in-repo,
`IsPackable=false`) migration projects themselves:

```
lib/net10.0/Flirty.dll
lib/net10.0/Flirty.Migrations.Sqlite.dll
lib/net10.0/Flirty.Migrations.PostgreSql.dll
lib/net10.0/Flirty.Migrations.SqlServer.dll
```

A `ProjectReference` from `Flirty` to the migration projects is impossible (they already reference
`Flirty` → build-graph cycle, even with `ReferenceOutputAssembly=false`). Therefore a
pack target in `src/Flirty/Flirty.csproj` builds the three projects on demand via an `<MSBuild>` task (not part
of the static build graph) and feeds their output DLLs through
`TargetsForTfmSpecificBuildOutput` → `BuildOutputInPackage` into `lib/<tfm>/`. `Configuration` is
passed through explicitly, so that no debug DLLs land in the release package. At runtime, EF Core loads the
assembly chosen by name (`MigrationsAssembly("Flirty.Migrations.<Provider>")`) from the
consumer's probing path. Domain background: [PERSISTENCE.md](./PERSISTENCE.md).

## SourceLink & Debugging

`Microsoft.SourceLink.GitHub` embeds the GitHub source references; with `PublishRepositoryUrl` and
`EmbedUntrackedSources`, consumers can step into the package sources. The symbol packages (`.snupkg`)
carry the corresponding PDBs and can be pushed to the symbol server.

## Building in CI

The CI pipeline (#16, `.github/workflows/ci.yml`) handles build + test + `dotnet pack` and uploads both
`.nupkg` (+ `.snupkg`) as an artifact. The build/run number is passed through as `BuildRevision`,
so that every run gets a unique revision. Details: [CI.md](./CI.md).

## Publishing (#49)

The push lives in its **own** workflow `.github/workflows/release.yml` – not in the CI
(#16, `ci.yml`), which continues to only build, test, pack and upload artifacts.

The reason is irreversibility: a version published on NuGet.org cannot be
**deleted**, only *unlisted* – and even an unlisted version stays resolvable for everyone
who requests it explicitly (NuGet.org guarantees this deliberately, so that builds do not break).
A step that acts like this does not belong at every `main` push, but behind a deliberate approval.

### One-time prerequisites

Both must be set up **manually**, otherwise the push job fails:

1. **API key on nuget.org** (Account → *API Keys*):
   - Scope: *Push* → **Push new packages and package versions**
     (the IDs `Flirty`/`Flirty.AspNetCore`/`Flirty.Mcp` do not yet exist on the first run, so "only new
     versions" is not enough),
   - Glob pattern: `Flirty*` – covers all packages and future offshoots,
   - Note the expiry date; an expired key manifests as a `403` in the push step.
2. **GitHub → Settings → Environments → `nuget`**:
   - Secret **`NUGET_API_KEY`** with the key from above,
   - optionally *Required reviewers* – that is the approval gate.

### Triggering

Via *Actions → Release → Run workflow* (branch selectable, usually `main`) or:

```pwsh
gh workflow run release.yml                      # version YYYYMM.<run-number>.0
gh workflow run release.yml -f revision=7        # version YYYYMM.7.0
gh workflow run release.yml -f dry_run=true      # build + verify, do NOT publish
```

| Input | Meaning |
|---|---|
| `revision` | Overrides `BuildRevision`. Empty = **run number of this workflow**, i.e. its own, monotonically increasing release counter (`202607.1.0`, `202607.2.0`, …), independent of the CI runs, which never push. |
| `dry_run` | Skips the push job. The `build` job runs fully through – including verification and artifact upload. This lets you test the workflow without burning a version number on NuGet.org. |

### Flow

Two jobs, so that the approval gate sits **between** building and pushing and the finished artifact is
inspectable before approval:

```
build:  restore -> build -c Release -> test -> pack -> verify -> artifact "nupkg"
                                                                          |
                                                            [environment nuget: approval]
                                                                          v
push:   load artifact -> dotnet nuget push -> summary
```

- **The unit suite is tested**, not the E2E. The release run rebuilds the binaries (different
  version stamp), so exactly those must be tested – but the rationale from
  [CI.md § Coverage](./CI.md#coverage) applies unchanged: the E2E covers no additional path
  (measured: one branch out of 430) and costs a browser installation.
- **The verification step** is the hard lock before the push. It checks against the real files:
  per package `.nupkg` **and** `.snupkg` exist (the acceptance criterion "incl. symbols"), and in the
  `Flirty` package all **four** DLLs lie under `lib/net10.0/` (core + the three migration assemblies,
  see [above](#bundled-migration-dlls-20)). If something is missing, the run breaks **before** the push.

  > When editing: the glob `Flirty.*.nupkg` also matches `Flirty.AspNetCore.*.nupkg` and
  > `Flirty.Mcp.*.nupkg`. The core package is therefore isolated via `Flirty.[0-9]*` – the version always
  > starts with a digit.

  > **A new packable project needs its own pair added here.** The list is enumerated, not derived, so a
  > forgotten package does not break the run – it simply ships unpacked and unnoticed, and a version on
  > nuget.org cannot be taken back. `Flirty.Mcp` was the first project to walk into that trap (#126).

- **`permissions` stays `contents: read`.** The push needs no GitHub right, only the secret.
- `concurrency: release` **without** `cancel-in-progress`: do not cut off a running upload.

### What exactly is pushed

```bash
dotnet nuget push "artifacts/*.nupkg" \
  --source https://api.nuget.org/v3/index.json \
  --api-key "$NUGET_API_KEY" --skip-duplicate
```

- The glob stays **quoted**: `dotnet` resolves it itself.
- The **`.snupkg` are pushed along automatically**, because they lie next to the `.nupkg` and
  NuGet.org has a symbol server. A second push would be wrong (and would fail).
- `--skip-duplicate` turns a re-run with the same revision into a no-op instead of an error –
  important when the push has to be repeated after a partial upload.

After the push, NuGet.org validates **asynchronously**: the package appears in the search only after a
few minutes, and the symbol package is checked separately. A green push step means "accepted",
not "already listed".

### Scope: no Azure Artifacts

The original issue text (#49) named the feed as configurable (NuGet.org **or** Azure
Artifacts). Implemented is **only NuGet.org** – deliberately: Azure Artifacts does not accept symbol packages via
`dotnet nuget push`, so the path there would need `--no-symbols` and would fail exactly the
acceptance criterion "all packages **incl. symbols**". A second code path, never
exercised here, would be worse than none. Should an internal feed become necessary, the place is clear:
`--source` in the push step plus a second secret.
