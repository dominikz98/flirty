using System.Text.RegularExpressions;
using Flirty.Placeholders;

namespace Flirty.Designer.Services;

/// <summary>
/// Fills <c>{{key}}</c> markers with the <b>sample</b> value declared for the key, for the designer's test
/// runner. This is a display-only substitution and the designer's one honest answer to a problem it cannot
/// otherwise solve: a filler is host-process code, so the designer cannot produce the real live value a
/// running engine would. It previews the declared sample instead – exactly the delta-honesty ADR 0012 set
/// for question types.
/// </summary>
/// <remarks>
/// Lives in a service rather than a component's <c>@code</c> block for the reason #103 recorded: rules in
/// a Razor code block are not unit-testable, and this one is asserted directly. The marker pattern is the
/// same one the core <c>PlaceholderRenderer</c> uses, so what the designer previews and what the engine
/// fills recognise the same tokens.
/// </remarks>
internal static partial class PlaceholderPreview
{
    [GeneratedRegex(@"\{\{([a-z0-9-]+)\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex MarkerPattern();

    /// <summary>Whether the text contains at least one <c>{{key}}</c> marker.</summary>
    /// <param name="text">The text to scan; <see langword="null"/> or empty contains none.</param>
    /// <returns><see langword="true"/> if a marker is present.</returns>
    public static bool ContainsMarker(string? text)
        => !string.IsNullOrEmpty(text) && MarkerPattern().IsMatch(text);

    /// <summary>
    /// Replaces every <c>{{key}}</c> marker with the sample declared for the key. A marker whose key is not
    /// declared, or is declared without a sample, is left raw – the same visible degradation the engine
    /// would show for an unresolved placeholder.
    /// </summary>
    /// <param name="text">The text to fill; <see langword="null"/> yields the empty string.</param>
    /// <param name="registry">The declared placeholders, whose samples are substituted.</param>
    /// <returns>The text with markers replaced by their samples where available.</returns>
    public static string Fill(string? text, FlirtyPlaceholderRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        return MarkerPattern().Replace(text, match =>
            registry.TryGet(match.Groups[1].Value, out var placeholder) && placeholder!.Sample is { } sample
                ? sample
                : match.Value);
    }
}
