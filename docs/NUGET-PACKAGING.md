# NuGet-Packaging

Wie die Flirty-Pakete gebaut werden – welche Projekte paketiert werden, wo die Metadaten liegen
und wie die Versionierung funktioniert. Umgesetzt in Issue **#15**.

## Was wird paketiert?

Genau **zwei** Projekte sind veröffentlichbar (`IsPackable=true`):

| Package | Projekt | Zweck |
|---|---|---|
| `Flirty` | `src/Flirty` | Core-Engine (ASP.NET-frei). |
| `Flirty.AspNetCore` | `src/Flirty.AspNetCore` | Optionale ASP.NET-Core-Endpunkte. |

Alle übrigen Projekte (`Flirty.Designer`, `Flirty.Samples`, `Flirty.Tests`, `Flirty.E2E`) erben
`IsPackable=false` aus `Directory.Build.props` bzw. setzen es explizit und erzeugen **kein** Paket.

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
  dotnet pack -c Release -p:BuildRevision=7   # -> Flirty.202604.7.nupkg
  ```

  In der CI z.B. die Build-/Run-Nummer durchreichen.

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
Flirty.202604.1.nupkg              Flirty.202604.1.snupkg
Flirty.AspNetCore.202604.1.nupkg   Flirty.AspNetCore.202604.1.snupkg
```

Für die übrigen vier Projekte entsteht nichts.

> Hinweis: `TreatWarningsAsErrors=true` gilt repo-weit und greift auch bei NuGet-Pack-Warnungen
> (NU5xxx). Lizenz, Icon und README sind deshalb vollständig gesetzt – fehlten sie, bräche `pack`.

## SourceLink & Debugging

`Microsoft.SourceLink.GitHub` bettet die GitHub-Quellverweise ein; mit `PublishRepositoryUrl` und
`EmbedUntrackedSources` können Konsumenten in die Paketquellen steppen. Die Symbolpakete (`.snupkg`)
tragen die zugehörigen PDBs und lassen sich zum Symbol-Server pushen.

## Bauen im CI

Die CI-Pipeline (#16, `.github/workflows/ci.yml`) übernimmt build + test + `dotnet pack` und lädt beide
`.nupkg` (+ `.snupkg`) als Artefakt hoch. Die Build-/Run-Nummer wird als `BuildRevision` durchgereicht,
sodass jeder Lauf eine eindeutige Revision erhält. Details: [CI.md](./CI.md).

## Publizieren

Der eigentliche Push (`dotnet nuget push` auf NuGet.org oder Azure Artifacts) ist bewusst **nicht**
Teil der CI-Pipeline (#16) – er folgt mit dem Publish-Issue (#49).
