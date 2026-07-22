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

## Veröffentlichen (#49)

Der Push liegt in `.github/workflows/release.yml` – **nicht** in `ci.yml` (die baut/testet/packt nur).
Feed ist **NuGet.org**, ausgelöst wird ausschließlich manuell.

```pwsh
gh workflow run release.yml -f dry_run=true   # bauen + verifizieren, KEIN Push (immer zuerst)
gh workflow run release.yml                   # Version JJJJMM.<Run-Nummer>.0
gh workflow run release.yml -f revision=7     # Version JJJJMM.7.0
```

- **Zwei Jobs:** `build` (restore → build → `dotnet test tests/Flirty.Tests` → pack → **verifizieren** →
  Artefakt `nupkg`) und `push`, der an der GitHub-Environment **`nuget`** hängt (Secret
  `NUGET_API_KEY` + optionales Reviewer-Gate). Das Gate sitzt bewusst *zwischen* beiden.
- **Verifikationsschritt** vor dem Push prüft an den realen Dateien: je Paket `.nupkg` **und**
  `.snupkg` sowie alle **vier** DLLs unter `lib/net10.0/` im Core-Paket. Beim Bearbeiten beachten:
  `Flirty.*.nupkg` matcht auch `Flirty.AspNetCore.*.nupkg` → das Core-Paket über `Flirty.[0-9]*`
  isolieren.
- **`.snupkg` werden automatisch mitgepusht** (liegen neben den `.nupkg`, NuGet.org hat einen
  Symbol-Server). Kein zweiter Push.
- **Kein Azure Artifacts** – bewusst: es nimmt Symbolpakete über `dotnet nuget push` nicht an und
  erfüllte das AC „inkl. Symbols" nicht.
- **Unwiderruflich:** Veröffentlichte Versionen lassen sich nur unlisten, nicht löschen. Deshalb
  immer erst `dry_run=true`.

Details, inklusive der einmaligen Einrichtung (API-Key mit Glob `Flirty*`, Environment `nuget`):
`docs/NUGET-PACKAGING.md` § Publizieren.

## Fallstrick: die dritte Versionsstelle

Die MSBuild-Property `Version` ist zweistellig (`202607.7`), NuGet normalisiert auf **drei** Segmente.
Dateiname, `.nuspec` und die Anzeige auf nuget.org lauten also `202607.7.0`. Beim Suchen nach einem
Artefakt oder einer Paketversion die `.0` mitdenken; `dotnet add package … --version 202607.7`
funktioniert trotzdem (NuGet normalisiert die Anfrage).

## Verifikation

```pwsh
dotnet pack -c Release -o artifacts -p:BuildRevision=99
# erwartet: Flirty.<JJJJMM>.99.0.nupkg/.snupkg + Flirty.AspNetCore.<JJJJMM>.99.0.nupkg/.snupkg
Expand-Archive artifacts/Flirty.*.nupkg -DestinationPath artifacts/inspect -Force
Get-ChildItem artifacts/inspect/lib/net10.0   # erwartet: 4 DLLs (Core + 3x Migrations)
```
