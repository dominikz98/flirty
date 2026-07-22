# NuGet-Packaging

Wie die Flirty-Pakete gebaut werden – welche Projekte paketiert werden, wo die Metadaten liegen
und wie die Versionierung funktioniert. Umgesetzt in Issue **#15**, das Veröffentlichen in **#49**
(siehe [Publizieren](#publizieren-49)).

## Was wird paketiert?

Genau **zwei** Projekte sind veröffentlichbar (`IsPackable=true`):

| Package | Projekt | Zweck |
|---|---|---|
| `Flirty` | `src/Flirty` | Core-Engine (ASP.NET-frei). |
| `Flirty.AspNetCore` | `src/Flirty.AspNetCore` | Optionale ASP.NET-Core-Endpunkte. |

Die übrigen acht Projekte der Solution (`Flirty.Designer`, die drei `Flirty.Migrations.*`,
`Flirty.Samples`, `Flirty.Samples.Web`, `Flirty.Tests`, `Flirty.E2E`) erben `IsPackable=false` aus
`Directory.Build.props` bzw. setzen es explizit und erzeugen **kein** Paket. Die Migrations-Assemblies
werden trotzdem ausgeliefert – aber als DLL **im** `Flirty`-Paket, nicht als eigenes Paket (siehe
[Mitgebündelte Migrations-DLLs](#mitgebündelte-migrations-dlls-20)).

## Wo liegen die Metadaten?

Zentral, damit beide Pakete konsistent bleiben – pro `.csproj` steht nur noch die Paket-Identität.

| Ort | Inhalt |
|---|---|
| `Directory.Build.props` | Gemeinsame Metadaten (`Authors`, `Copyright`, `PackageProjectUrl`, `RepositoryUrl`, `PackageLicenseExpression=MIT`, `PackageIcon`, `PublishRepositoryUrl`, `EmbedUntrackedSources`) und die SourceLink-`PackageReference`. Unbedingt gesetzt – auf Nicht-Paket-Projekten inert. |
| `Directory.Build.targets` | Paket-**Verhalten** gated auf `IsPackable=='true'`: Symbolpakete (`IncludeSymbols`/`snupkg`), `PackageReadmeFile`, `ContinuousIntegrationBuild` (nur CI), die **datumsbasierte Version** sowie die README-/Icon-Pack-Includes. Gated in `.targets`, weil `IsPackable` erst im `.csproj` gesetzt wird (gleiches Muster wie die CS1591-Erzwingung). |
| `Directory.Packages.props` | Zentral gepinnte Version von `Microsoft.SourceLink.GitHub` (CPM ist an). |
| `src/Flirty*/**.csproj` | Nur `IsPackable`, `PackageId`, `Description`, `PackageTags`. |

Lizenzdatei: `LICENSE` (MIT) im Repo-Root. Bei `PackageLicenseExpression` genügt NuGet die
SPDX-Kennung; die Datei selbst wird nicht ins Paket gelegt. Icon: `icon.png` (128×128) im Root,
gepackt als `icon.png`. README: die Root-`README.md`.

## Versionierung (datumsbasiert)

Kein MinVer, keine Git-Tags. Die **NuGet-Paketversion** ist `JJJJMM.Revision`:

```
202604.1   ->  Jahr 2026, Monat 04, Revision 1
```

- Jahr/Monat kommen aus `System.DateTime.UtcNow` (zum Build-Zeitpunkt).
- Die Revision ist standardmäßig `1` und wird beim Bauen überschrieben:

  ```pwsh
  dotnet pack -c Release -p:BuildRevision=7   # -> Flirty.202604.7.0.nupkg
  ```

  In der CI z.B. die Build-/Run-Nummer durchreichen.

### Die dritte Stelle: NuGet normalisiert

Die MSBuild-Property `Version` ist zweistellig (`202604.7`), eine **NuGet-Version hat aber mindestens
drei Segmente**. NuGet normalisiert deshalb still auf `202604.7.0` – und zwar überall dort, wo die
Version nach außen sichtbar wird:

| Ort | Wert |
|---|---|
| MSBuild-Property `Version` | `202604.7` |
| Dateiname | `Flirty.202604.7.0.nupkg` |
| `<version>` in der `.nuspec` | `202604.7.0` |
| Anzeige auf nuget.org | `202604.7.0` |

Für Konsumenten ist das folgenlos – `dotnet add package Flirty --version 202604.7` normalisiert
NuGet auf denselben Wert. Beim Nachschlagen einer konkreten Version (Artefaktname, Release-Log,
Paketseite) ist die dritte Stelle aber da, und ein Suchen nach `Flirty.202604.7.nupkg` läuft ins Leere.

### Warum eine zweite Version für die Assembly?

`AssemblyVersion` und `FileVersion` bestehen aus vier `UInt16`-Segmenten (jeweils max. **65535**).
`202604` überschreitet das und wäre ungültig. Deshalb ist die Assembly-Version **entkoppelt**:

```
AssemblyVersion / FileVersion = Jahr.Monat.Revision.0   (z.B. 2026.4.1.0)
```

Alle Segmente bleiben ≤ 65535. Die aussagekräftige, datumsbasierte Nummer trägt das NuGet-Paket
(`Version` → auch `PackageVersion`/`InformationalVersion`).

## Bauen

```pwsh
dotnet pack -c Release -o artifacts
```

Erzeugt in `artifacts/` je Package ein `.nupkg` **und** ein `.snupkg`:

```
Flirty.202604.1.0.nupkg              Flirty.202604.1.0.snupkg
Flirty.AspNetCore.202604.1.0.nupkg   Flirty.AspNetCore.202604.1.0.snupkg
```

Für die übrigen acht Projekte entsteht nichts.

> Hinweis: `TreatWarningsAsErrors=true` gilt repo-weit und greift auch bei NuGet-Pack-Warnungen
> (NU5xxx). Lizenz, Icon und README sind deshalb vollständig gesetzt – fehlten sie, bräche `pack`.

## Mitgebündelte Migrations-DLLs (#20)

Das `Flirty`-Paket liefert die drei provider-getrennten Migrations-Assemblies mit, damit
Paket-Konsumenten via `o.ApplyMigrations()` auto-migrieren können, ohne die (In-Repo-,
`IsPackable=false`) Migrations-Projekte selbst zu referenzieren:

```
lib/net10.0/Flirty.dll
lib/net10.0/Flirty.Migrations.Sqlite.dll
lib/net10.0/Flirty.Migrations.PostgreSql.dll
lib/net10.0/Flirty.Migrations.SqlServer.dll
```

Ein `ProjectReference` von `Flirty` auf die Migrations-Projekte ist unmöglich (sie referenzieren
`Flirty` bereits → Build-Graph-Zyklus, auch mit `ReferenceOutputAssembly=false`). Deshalb baut ein
Pack-Target in `src/Flirty/Flirty.csproj` die drei Projekte per `<MSBuild>`-Task on-demand (nicht Teil
des statischen Build-Graphen) und speist ihre Output-DLLs über
`TargetsForTfmSpecificBuildOutput` → `BuildOutputInPackage` nach `lib/<tfm>/`. `Configuration` wird
explizit durchgereicht, damit im Release-Paket keine Debug-DLLs landen. Zur Laufzeit lädt EF Core die
per Name gewählte Assembly (`MigrationsAssembly("Flirty.Migrations.<Provider>")`) aus dem
Probing-Pfad des Konsumenten. Fachlicher Hintergrund: [PERSISTENCE.md](./PERSISTENCE.md).

## SourceLink & Debugging

`Microsoft.SourceLink.GitHub` bettet die GitHub-Quellverweise ein; mit `PublishRepositoryUrl` und
`EmbedUntrackedSources` können Konsumenten in die Paketquellen steppen. Die Symbolpakete (`.snupkg`)
tragen die zugehörigen PDBs und lassen sich zum Symbol-Server pushen.

## Bauen im CI

Die CI-Pipeline (#16, `.github/workflows/ci.yml`) übernimmt build + test + `dotnet pack` und lädt beide
`.nupkg` (+ `.snupkg`) als Artefakt hoch. Die Build-/Run-Nummer wird als `BuildRevision` durchgereicht,
sodass jeder Lauf eine eindeutige Revision erhält. Details: [CI.md](./CI.md).

## Publizieren (#49)

Der Push liegt in einem **eigenen** Workflow `.github/workflows/release.yml` – nicht in der CI
(#16, `ci.yml`), die weiterhin nur baut, testet, packt und Artefakte hochlädt.

Der Grund ist die Unwiderruflichkeit: Eine auf NuGet.org veröffentlichte Version lässt sich
**nicht löschen**, nur *unlisten* – und selbst eine ungelistete Version bleibt für alle auflösbar,
die sie explizit anfordern (das garantiert NuGet.org bewusst, damit Builds nicht zerbrechen).
Ein Schritt, der so wirkt, gehört nicht an jeden `main`-Push, sondern hinter eine bewusste Freigabe.

### Einmalige Voraussetzungen

Beides muss **manuell** eingerichtet sein, sonst scheitert der Push-Job:

1. **API-Key auf nuget.org** (Account → *API Keys*):
   - Scope: *Push* → **Push new packages and package versions**
     (die IDs `Flirty`/`Flirty.AspNetCore` existieren beim ersten Lauf noch nicht, „nur neue
     Versionen" reicht also nicht),
   - Glob-Pattern: `Flirty*` – deckt beide Pakete und künftige Ableger ab,
   - Ablaufdatum notieren; ein abgelaufener Key äußert sich als `403` im Push-Schritt.
2. **GitHub → Settings → Environments → `nuget`**:
   - Secret **`NUGET_API_KEY`** mit dem Key von oben,
   - optional *Required reviewers* – das ist das Freigabe-Gate.

### Auslösen

Über *Actions → Release → Run workflow* (Branch wählbar, i.d.R. `main`) oder:

```pwsh
gh workflow run release.yml                      # Version JJJJMM.<Run-Nummer>.0
gh workflow run release.yml -f revision=7        # Version JJJJMM.7.0
gh workflow run release.yml -f dry_run=true      # bauen + verifizieren, NICHT veröffentlichen
```

| Input | Bedeutung |
|---|---|
| `revision` | Überschreibt `BuildRevision`. Leer = **Run-Nummer dieses Workflows**, also ein eigener, monoton steigender Release-Zähler (`202607.1.0`, `202607.2.0`, …), unabhängig von den CI-Läufen, die ja nie pushen. |
| `dry_run` | Lässt den Push-Job aus. Der `build`-Job läuft vollständig durch – inklusive Verifikation und Artefakt-Upload. Damit lässt sich der Workflow testen, ohne eine Versionsnummer auf NuGet.org zu verbrennen. |

### Ablauf

Zwei Jobs, damit das Freigabe-Gate **zwischen** Bauen und Pushen sitzt und das fertige Artefakt vor
der Freigabe einsehbar ist:

```
build:  restore -> build -c Release -> test -> pack -> verifizieren -> Artefakt "nupkg"
                                                                          |
                                                            [Environment nuget: Freigabe]
                                                                          v
push:   Artefakt laden -> dotnet nuget push -> Zusammenfassung
```

- **Getestet wird die Unit-Suite**, nicht die E2E. Der Release-Lauf baut die Binaries neu (anderer
  Versionsstempel), deshalb müssen genau diese getestet werden – aber die Begründung aus
  [CI.md § Coverage](./CI.md#coverage) gilt unverändert: die E2E deckt keinen Pfad zusätzlich ab
  (gemessen: ein Zweig von 430) und kostet Browser-Installation.
- **Der Verifikationsschritt** ist der harte Riegel vor dem Push. Er prüft an den realen Dateien:
  je Paket existiert `.nupkg` **und** `.snupkg` (das Akzeptanzkriterium „inkl. Symbols"), und im
  `Flirty`-Paket liegen unter `lib/net10.0/` alle **vier** DLLs (Core + die drei Migrations-Assemblies,
  siehe [oben](#mitgebündelte-migrations-dlls-20)). Fehlt etwas, bricht der Lauf **vor** dem Push.

  > Beim Bearbeiten: Das Glob `Flirty.*.nupkg` matcht auch `Flirty.AspNetCore.*.nupkg`. Das Core-Paket
  > wird deshalb über `Flirty.[0-9]*` isoliert – die Version beginnt immer mit einer Ziffer.

- **`permissions` bleibt `contents: read`.** Der Push braucht kein GitHub-Recht, nur das Secret.
- `concurrency: release` **ohne** `cancel-in-progress`: einen laufenden Upload nicht abschneiden.

### Was genau gepusht wird

```bash
dotnet nuget push "artifacts/*.nupkg" \
  --source https://api.nuget.org/v3/index.json \
  --api-key "$NUGET_API_KEY" --skip-duplicate
```

- Das Glob bleibt **gequotet**: `dotnet` löst es selbst auf.
- Die **`.snupkg` werden automatisch mitgeschoben**, weil sie neben den `.nupkg` liegen und
  NuGet.org einen Symbol-Server hat. Ein zweiter Push wäre falsch (und schlüge fehl).
- `--skip-duplicate` macht einen Wiederholungslauf mit derselben Revision zum No-op statt zum Fehler –
  wichtig, wenn der Push nach einem Teilupload wiederholt werden muss.

Nach dem Push validiert NuGet.org **asynchron**: das Paket erscheint erst nach ein paar Minuten in
der Suche, und das Symbolpaket wird separat geprüft. Ein grüner Push-Schritt heißt „angenommen",
nicht „bereits gelistet".

### Abgrenzung: kein Azure Artifacts

Der ursprüngliche Issue-Text (#49) nannte den Feed als konfigurierbar (NuGet.org **oder** Azure
Artifacts). Umgesetzt ist **nur NuGet.org** – bewusst: Azure Artifacts nimmt Symbolpakete über
`dotnet nuget push` nicht entgegen, der Weg dorthin bräuchte also `--no-symbols` und erfüllte das
Akzeptanzkriterium „beide Packages **inkl. Symbols**" gerade nicht. Ein zweiter, hier nie
ausgeführter Codepfad wäre schlechter als keiner. Wird ein interner Feed nötig, ist die Stelle klar:
`--source` im Push-Schritt plus ein zweites Secret.
