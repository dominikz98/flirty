using System.Text.RegularExpressions;

namespace Flirty.Tests.Docs;

/// <summary>
/// Verifies issue #52: the root <c>README.md</c> is not only the GitHub start page – via
/// <c>PackageReadmeFile</c> (see <c>Directory.Build.targets</c>) it is packed into <b>both</b> NuGet
/// packages and rendered on nuget.org. There is no repo root directory there: relative targets
/// resolve against the package page and run into the void, and images with a relative path or from
/// hosts that are not allow-listed are not rendered at all. These tests pin both rules down, so that
/// they do not silently break with the next incremental addition to the README.
/// </summary>
/// <remarks>
/// The README is copied into the test output directory via a <c>Content</c> entry in
/// <c>Flirty.Tests.csproj</c> (same pattern as the chat UI in <c>Flirty.E2E</c>), so the test needs
/// no assumption about the working directory.
/// </remarks>
public sealed class PackageReadmeTests
{
    /// <summary>Hosts allow-listed by nuget.org for images/badges, as far as used here.</summary>
    private static readonly string[] AllowedImageHosts = ["img.shields.io", "github.com"];

    /// <summary>Markdown link or image: captures the image marker and the target separately.</summary>
    private static readonly Regex LinkPattern = new(@"(?<image>!)?\[[^\]]*\]\((?<target>[^)\s]+)", RegexOptions.Compiled);

    /// <summary>A target with a scheme (<c>https:</c>, <c>mailto:</c> …), so not repo-relative.</summary>
    private static readonly Regex AbsoluteTargetPattern = new(@"^[a-z][a-z0-9+.\-]*:", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Every reference to repo content must be absolute
    /// (<c>https://github.com/dominikz98/flirty/blob/main/…</c>). Besides that, only in-page anchors
    /// and other schemes stay allowed (e.g. the <c>http://localhost</c> hint).
    /// </summary>
    [Fact]
    public void Readme_links_to_repo_content_only_absolutely()
    {
        var relative = Targets(ReadReadme())
            .Where(target => !AbsoluteTargetPattern.IsMatch(target) && !target.StartsWith('#'))
            .ToList();

        Assert.True(
            relative.Count == 0,
            "Relative targets break on nuget.org (the README is the package page of both packages). "
            + "Affected: " + string.Join(", ", relative));
    }

    /// <summary>
    /// Image/badge sources must come from one of the hosts allow-listed by nuget.org – otherwise the
    /// image stays blank on the package page (with a warning only the package owner ever sees).
    /// </summary>
    [Fact]
    public void Readme_image_sources_are_on_the_nuget_org_allowlist()
    {
        var blocked = Targets(ReadReadme(), imagesOnly: true)
            .Where(target => !AllowedImageHosts.Any(host =>
                target.StartsWith($"https://{host}/", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Assert.True(
            blocked.Count == 0,
            "Images outside the nuget.org allowlist are not rendered. Affected: "
            + string.Join(", ", blocked));
    }

    private static IEnumerable<string> Targets(string markdown, bool imagesOnly = false)
        => LinkPattern.Matches(markdown)
            .Where(match => !imagesOnly || match.Groups["image"].Success)
            .Select(match => match.Groups["target"].Value);

    private static string ReadReadme()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "README.md");
        Assert.True(File.Exists(path), $"README.md not found in the test output directory: {path}");
        return File.ReadAllText(path);
    }
}
