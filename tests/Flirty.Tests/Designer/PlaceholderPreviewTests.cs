using Flirty.Designer.Services;
using Flirty.Placeholders;

namespace Flirty.Tests.Designer;

/// <summary>
/// Verifies <see cref="PlaceholderPreview"/> – the designer's sample-based marker substitution for the
/// test runner (#140). It previews the declared sample because the designer runs no host filler; a marker
/// without a declared sample stays raw, exactly as the engine would show an unresolved one.
/// </summary>
public sealed class PlaceholderPreviewTests
{
    private static FlirtyPlaceholderRegistry Registry(params (string Key, string? Sample)[] entries)
        => new(entries.ToDictionary(
            entry => entry.Key,
            entry => new FlirtyPlaceholder(entry.Key, entry.Key, FillerType: null, entry.Sample),
            StringComparer.Ordinal));

    [Fact]
    public void Fills_a_marker_with_its_declared_sample()
    {
        var text = PlaceholderPreview.Fill("Hello {{user-name}}", Registry(("user-name", "Alice")));

        Assert.Equal("Hello Alice", text);
    }

    [Fact]
    public void Fills_several_markers()
    {
        var registry = Registry(("user-name", "Alice"), ("today", "2026-08-07"));

        var text = PlaceholderPreview.Fill("{{user-name}} on {{today}}", registry);

        Assert.Equal("Alice on 2026-08-07", text);
    }

    [Fact]
    public void An_unknown_key_is_left_raw()
    {
        var text = PlaceholderPreview.Fill("Order {{order-id}}", Registry(("user-name", "Alice")));

        Assert.Equal("Order {{order-id}}", text);
    }

    [Fact]
    public void A_placeholder_without_a_sample_is_left_raw()
    {
        var text = PlaceholderPreview.Fill("Hello {{user-name}}", Registry(("user-name", null)));

        Assert.Equal("Hello {{user-name}}", text);
    }

    [Fact]
    public void A_token_outside_the_charset_is_not_a_marker()
    {
        var registry = Registry(("user-name", "Alice"));

        Assert.Equal("Hi {{User_Name}}", PlaceholderPreview.Fill("Hi {{User_Name}}", registry));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Null_or_empty_text_yields_the_empty_string(string? text)
        => Assert.Equal(string.Empty, PlaceholderPreview.Fill(text, FlirtyPlaceholderRegistry.Empty));

    [Fact]
    public void ContainsMarker_detects_a_marker()
    {
        Assert.True(PlaceholderPreview.ContainsMarker("Hello {{user-name}}"));
        Assert.False(PlaceholderPreview.ContainsMarker("Hello there"));
        Assert.False(PlaceholderPreview.ContainsMarker(null));
    }
}
