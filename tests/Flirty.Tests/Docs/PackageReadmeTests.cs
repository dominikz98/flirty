using System.Text.RegularExpressions;

namespace Flirty.Tests.Docs;

/// <summary>
/// Verifiziert Issue #52: Die Root-<c>README.md</c> ist nicht nur die GitHub-Startseite, sondern wird
/// über <c>PackageReadmeFile</c> (siehe <c>Directory.Build.targets</c>) in <b>beide</b> NuGet-Pakete
/// gepackt und auf nuget.org gerendert. Dort gibt es kein Repo-Wurzelverzeichnis: relative Ziele lösen
/// gegen die Paketseite auf und laufen ins Leere, und Bilder mit relativem Pfad oder von nicht
/// freigegebenen Hosts werden gar nicht erst gerendert. Diese Tests halten beide Regeln fest, damit sie
/// beim nächsten inkrementellen Anbau an der README nicht still kippen.
/// </summary>
/// <remarks>
/// Die README wird per <c>Content</c>-Eintrag im <c>Flirty.Tests.csproj</c> ins Testausgabeverzeichnis
/// kopiert (Muster wie die Chat-UI in <c>Flirty.E2E</c>), damit der Test ohne Annahmen über den
/// Arbeitsordner auskommt.
/// </remarks>
public sealed class PackageReadmeTests
{
    /// <summary>Von nuget.org für Bilder/Badges freigegebene Hosts, soweit hier genutzt.</summary>
    private static readonly string[] AllowedImageHosts = ["img.shields.io", "github.com"];

    /// <summary>Markdown-Link bzw. -Bild: erfasst Bild-Marker und Ziel getrennt.</summary>
    private static readonly Regex LinkPattern = new(@"(?<image>!)?\[[^\]]*\]\((?<target>[^)\s]+)", RegexOptions.Compiled);

    /// <summary>Ziel mit Schema (<c>https:</c>, <c>mailto:</c> …), also nicht repo-relativ.</summary>
    private static readonly Regex AbsoluteTargetPattern = new(@"^[a-z][a-z0-9+.\-]*:", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Alle Verweise auf Repo-Inhalte müssen absolut sein
    /// (<c>https://github.com/dominikz98/flirty/blob/main/…</c>). Erlaubt bleiben daneben nur
    /// seiteninterne Anker und andere Schemata (z. B. der <c>http://localhost</c>-Hinweis).
    /// </summary>
    [Fact]
    public void Readme_verlinkt_Repo_Inhalte_nur_absolut()
    {
        var relative = Targets(ReadReadme())
            .Where(target => !AbsoluteTargetPattern.IsMatch(target) && !target.StartsWith('#'))
            .ToList();

        Assert.True(
            relative.Count == 0,
            "Relative Ziele brechen auf nuget.org (die README ist die Paketseite beider Pakete). "
            + "Betroffen: " + string.Join(", ", relative));
    }

    /// <summary>
    /// Bild-/Badge-Quellen müssen von einem der von nuget.org freigegebenen Hosts kommen – sonst
    /// bleibt das Bild auf der Paketseite leer (mit einer Warnung, die nur der Paket-Eigentümer sieht).
    /// </summary>
    [Fact]
    public void Readme_Bildquellen_liegen_auf_der_nuget_org_Allowlist()
    {
        var blocked = Targets(ReadReadme(), imagesOnly: true)
            .Where(target => !AllowedImageHosts.Any(host =>
                target.StartsWith($"https://{host}/", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Assert.True(
            blocked.Count == 0,
            "Bilder außerhalb der nuget.org-Allowlist werden nicht gerendert. Betroffen: "
            + string.Join(", ", blocked));
    }

    private static IEnumerable<string> Targets(string markdown, bool imagesOnly = false)
        => LinkPattern.Matches(markdown)
            .Where(match => !imagesOnly || match.Groups["image"].Success)
            .Select(match => match.Groups["target"].Value);

    private static string ReadReadme()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "README.md");
        Assert.True(File.Exists(path), $"README.md nicht im Testausgabeverzeichnis gefunden: {path}");
        return File.ReadAllText(path);
    }
}
