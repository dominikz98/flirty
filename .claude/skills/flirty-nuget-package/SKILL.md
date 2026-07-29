---
name: flirty-nuget-package
description: Build, version and publish the Flirty NuGet packages (Flirty + Flirty.AspNetCore). Use for "dotnet pack", "NuGet package", "publish", "bump version", "make a new project packable", "bundle migration DLLs", "SourceLink/snupkg".
---

# NuGet packaging & publishing

Exactly **two** projects are publishable: `Flirty` (core) and `Flirty.AspNetCore`. All others inherit
`IsPackable=false`. Reference: `docs/NUGET-PACKAGING.md`, `docs/CI.md`, `Directory.Build.targets`.

## Key facts

- **Metadata centralized:** shared bits (`Authors`, license `MIT`, icon, `RepositoryUrl`, SourceLink) in
  `Directory.Build.props`; package **behavior** (symbol packages, README/icon includes, version) in
  `Directory.Build.targets`, gated on `IsPackable=='true'`. In the `.csproj` only `IsPackable`,
  `PackageId`, `Description`, `PackageTags`.
- **Date-based version** `YYYYMM.Revision` (e.g. `202607.1`), year/month from `UtcNow`, revision
  default `1`, overridable via `-p:BuildRevision=N`. `AssemblyVersion`/`FileVersion` are **decoupled**
  from it (`Year.Month.Revision.0`, because the segments are `UInt16` ≤ 65535). Do **not** bump manually
  in the `.csproj`.
- **`TreatWarningsAsErrors=true` also applies to pack (NU5xxx).** License, icon (`icon.png`, 128×128)
  and README must be complete, otherwise `pack` fails.
- **Migration DLLs bundled:** `Flirty` **cannot** reference the migration projects via `ProjectReference`
  (build-graph cycle). A pack target `IncludeFlirtyMigrationAssemblies` in `src/Flirty/Flirty.csproj`
  builds them via an `<MSBuild>` task and places the three DLLs into `lib/net10.0/` via
  `TargetsForTfmSpecificBuildOutput`/`BuildOutputInPackage`. At runtime EF Core loads them via
  `MigrationsAssembly("Flirty.Migrations.<Provider>")`.

## Build a package

```pwsh
dotnet pack -c Release -o artifacts                 # version YYYYMM.1
dotnet pack -c Release -o artifacts -p:BuildRevision=7   # -> Flirty.202607.7.nupkg
```

Expected result in `artifacts/` (per package `.nupkg` **and** `.snupkg`):

```
Flirty.<version>.nupkg / .snupkg
Flirty.AspNetCore.<version>.nupkg / .snupkg
```

Check that in the `Flirty` package under `lib/net10.0/` **all four** DLLs are present (`Flirty.dll` +
the three `Flirty.Migrations.*.dll`).

## Make a new project packable (rare)

Set `IsPackable=true`, `PackageId`, `Description`, `PackageTags` in the `.csproj`. This automatically
engages the CS1591 enforcement (English XML docs on all public API) and the package wiring from
`Directory.Build.targets`.

## Publishing (#49)

The push lives in `.github/workflows/release.yml` – **not** in `ci.yml` (which only builds/tests/packs).
The feed is **NuGet.org**, triggered exclusively manually.

```pwsh
gh workflow run release.yml -f dry_run=true   # build + verify, NO push (always first)
gh workflow run release.yml                   # version YYYYMM.<run number>.0
gh workflow run release.yml -f revision=7     # version YYYYMM.7.0
```

- **Two jobs:** `build` (restore → build → `dotnet test tests/Flirty.Tests` → pack → **verify** →
  artifact `nupkg`) and `push`, which hangs off the GitHub environment **`nuget`** (secret
  `NUGET_API_KEY` + optional reviewer gate). The gate deliberately sits *between* the two.
- The **verification step** before the push checks against the real files: per package `.nupkg` **and**
  `.snupkg` as well as all **four** DLLs under `lib/net10.0/` in the core package. When editing, mind
  that `Flirty.*.nupkg` also matches `Flirty.AspNetCore.*.nupkg` → isolate the core package via
  `Flirty.[0-9]*`.
- **`.snupkg` are pushed automatically** (they sit next to the `.nupkg`, NuGet.org has a symbol server).
  No second push.
- **No Azure Artifacts** – deliberately: it does not accept symbol packages via `dotnet nuget push` and
  would not satisfy the AC "incl. symbols".
- **Irreversible:** published versions can only be unlisted, not deleted. Hence always `dry_run=true`
  first.

Details, including the one-time setup (API key with glob `Flirty*`, environment `nuget`):
`docs/NUGET-PACKAGING.md` § Publishing.

## Pitfall: the third version segment

The MSBuild property `Version` is two-part (`202607.7`), NuGet normalizes to **three** segments. So the
file name, `.nuspec` and the display on nuget.org read `202607.7.0`. When searching for an artifact or a
package version, keep the `.0` in mind; `dotnet add package … --version 202607.7` still works (NuGet
normalizes the request).

## Verification

```pwsh
dotnet pack -c Release -o artifacts -p:BuildRevision=99
# expected: Flirty.<YYYYMM>.99.0.nupkg/.snupkg + Flirty.AspNetCore.<YYYYMM>.99.0.nupkg/.snupkg
Expand-Archive artifacts/Flirty.*.nupkg -DestinationPath artifacts/inspect -Force
Get-ChildItem artifacts/inspect/lib/net10.0   # expected: 4 DLLs (core + 3x migrations)
```
