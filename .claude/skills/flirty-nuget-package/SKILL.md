---
name: flirty-nuget-package
description: Flirty-NuGet-Pakete bauen, versionieren und veröffentlichen (Flirty + Flirty.AspNetCore). Verwenden bei "dotnet pack", "NuGet-Paket", "veröffentlichen/publish", "Version erhöhen", "neues Paket paketierbar machen", "Migrations-DLLs bündeln", "SourceLink/snupkg".
---

# NuGet-Packaging & Veröffentlichung

Genau **zwei** Projekte sind veröffentlichbar: `Flirty` (Core) und `Flirty.AspNetCore`. Alle anderen
erben `IsPackable=false`. Referenz: `docs/NUGET-PACKAGING.md`, `docs/CI.md`, `Directory.Build.targets`.

## Wichtige Fakten

- **Metadaten zentral:** Gemeinsames (`Authors`, Lizenz `MIT`, Icon, `RepositoryUrl`, SourceLink) in
  `Directory.Build.props`; Paket-**Verhalten** (Symbolpakete, README/Icon-Includes, Version) in
  `Directory.Build.targets`, gated auf `IsPackable=='true'`. Im `.csproj` stehen nur `IsPackable`,
  `PackageId`, `Description`, `PackageTags`.
- **Datumsbasierte Version** `JJJJMM.Revision` (z. B. `202607.1`), Jahr/Monat aus `UtcNow`, Revision
  default `1`, überschreibbar per `-p:BuildRevision=N`. `AssemblyVersion`/`FileVersion` sind davon
  **entkoppelt** (`Jahr.Monat.Revision.0`, weil die Segmente `UInt16` ≤ 65535 sind). **Nicht** manuell
  in den `.csproj` hochzählen.
- **`TreatWarningsAsErrors=true` gilt auch für Pack (NU5xxx).** Lizenz, Icon (`icon.png`, 128×128) und
  README müssen vollständig sein, sonst bricht `pack`.
- **Migrations-DLLs gebündelt:** `Flirty` kann die Migrations-Projekte **nicht** per `ProjectReference`
  einbinden (Build-Graph-Zyklus). Ein Pack-Target `IncludeFlirtyMigrationAssemblies` in
  `src/Flirty/Flirty.csproj` baut sie per `<MSBuild>`-Task und legt die drei DLLs via
  `TargetsForTfmSpecificBuildOutput`/`BuildOutputInPackage` nach `lib/net10.0/`. Zur Laufzeit lädt EF
  Core sie über `MigrationsAssembly("Flirty.Migrations.<Provider>")`.

## Paket bauen

```pwsh
dotnet pack -c Release -o artifacts                 # Version JJJJMM.1
dotnet pack -c Release -o artifacts -p:BuildRevision=7   # -> Flirty.202607.7.nupkg
```

Erwartetes Ergebnis in `artifacts/` (je Paket `.nupkg` **und** `.snupkg`):

```
Flirty.<version>.nupkg / .snupkg
Flirty.AspNetCore.<version>.nupkg / .snupkg
```

Prüfen, dass im `Flirty`-Paket unter `lib/net10.0/` **alle vier** DLLs liegen (`Flirty.dll` +
die drei `Flirty.Migrations.*.dll`).

## Ein neues Projekt paketierbar machen (selten)

`IsPackable=true`, `PackageId`, `Description`, `PackageTags` im `.csproj` setzen. Damit greift automatisch
die CS1591-Erzwingung (deutsche XML-Docs auf aller public API) und die Paket-Verdrahtung aus
`Directory.Build.targets`.

## Veröffentlichen (#49, noch offen)

Der Push (`dotnet nuget push` auf NuGet.org oder Azure Artifacts) ist **bewusst nicht** Teil der CI
(`.github/workflows/ci.yml` baut/testet/packt nur und lädt die Artefakte hoch). Der Publish-Weg wird mit
Issue #49 definiert – Feed und API-Key sind noch festzulegen. Beim Umsetzen `docs/NUGET-PACKAGING.md`
(Abschnitt „Publizieren") und `docs/CI.md` aktualisieren.

## Verifikation

```pwsh
dotnet pack -c Release -o artifacts
# Optional Paketinhalt inspizieren (z. B. mit `dotnet nuget verify` oder Entpacken der .nupkg als .zip)
```
