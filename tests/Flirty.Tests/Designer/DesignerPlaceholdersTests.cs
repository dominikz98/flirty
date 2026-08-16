using Flirty.Designer.Models;
using Flirty.Designer.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Flirty.Tests.Designer;

/// <summary>
/// Verifies <see cref="DesignerPlaceholders.Declare"/> – the half of #140 that turns parsed descriptors
/// into real declarations on the core registry, mirroring <see cref="DesignerQuestionTypesTests"/>.
/// </summary>
/// <remarks>
/// <para>
/// The whole point of this class is that it keeps <b>no</b> copy of the core's validity rules: it calls
/// <c>AddPlaceholder</c> and catches what that throws. These tests therefore drive the real
/// <see cref="FlirtyOptions"/>, so a future change to the core's rules shows up here rather than being
/// masked by a designer-side re-implementation.
/// </para>
/// <para>
/// And a bad entry must be <b>skipped, not fatal</b> – the call site is <c>DesignerApp.ConfigureServices</c>,
/// where an exception is a designer that will not start.
/// </para>
/// </remarks>
public sealed class DesignerPlaceholdersTests
{
    private static PlaceholderDescriptor Descriptor(string key, string name = "User name", string? sample = null)
        => new() { Key = key, DisplayName = name, Sample = sample };

    [Fact]
    public void Valid_descriptors_are_declared()
    {
        var options = new FlirtyOptions();

        var problems = DesignerPlaceholders.Declare(
            options,
            [Descriptor("user-name"), Descriptor("today", "Today's date", "2026-08-07")]);

        Assert.Empty(problems);
        Assert.Equal(["today", "user-name"], options.Placeholders.Keys.Order());
        Assert.Equal("Today's date", options.Placeholders["today"].DisplayName);
        Assert.Equal("2026-08-07", options.Placeholders["today"].Sample);
    }

    /// <summary>
    /// The designer declares descriptors, never fillers – a filler is code and lives in the host process.
    /// This is the fact the delta note in the test runner rests on, so it is pinned rather than assumed.
    /// </summary>
    [Fact]
    public void A_declared_placeholder_carries_no_filler()
    {
        var options = new FlirtyOptions();

        _ = DesignerPlaceholders.Declare(options, [Descriptor("user-name")]);

        Assert.Null(options.Placeholders["user-name"].FillerType);
    }

    /// <summary>
    /// The load-bearing case: one unusable entry must not cost the others. Each row here is refused by a
    /// different guard inside <c>AddPlaceholder</c> – charset, blank name, duplicate. Unlike a custom
    /// question type there is no JSON check on the sample, so a plain-text sample never fails.
    /// </summary>
    [Fact]
    public void An_unusable_entry_is_skipped_and_reported()
    {
        var options = new FlirtyOptions();

        var problems = DesignerPlaceholders.Declare(
            options,
            [
                Descriptor("User-Name"),                    // uppercase - outside [a-z0-9-]
                Descriptor("blank", name: "  "),            // no display name
                Descriptor("user-name"),
                Descriptor("user-name", "Again"),           // duplicate key
            ]);

        Assert.Equal(3, problems.Count);
        Assert.Equal("user-name", Assert.Single(options.Placeholders).Key);

        Assert.StartsWith("Entry 1 (\"User-Name\")", problems[0], StringComparison.Ordinal);
        Assert.StartsWith("Entry 4 (\"user-name\")", problems[2], StringComparison.Ordinal);
    }

    /// <summary>A plain-text sample is accepted as-is – a placeholder sample is not a JSON document.</summary>
    [Fact]
    public void A_plain_text_sample_is_accepted()
    {
        var options = new FlirtyOptions();

        var problems = DesignerPlaceholders.Declare(options, [Descriptor("user-name", sample: "Alice")]);

        Assert.Empty(problems);
        Assert.Equal("Alice", options.Placeholders["user-name"].Sample);
    }

    /// <summary>An empty <c>"sample"</c> in the file means "none".</summary>
    [Fact]
    public void An_empty_sample_is_read_as_none()
    {
        var options = new FlirtyOptions();

        var problems = DesignerPlaceholders.Declare(options, [Descriptor("user-name", sample: "   ")]);

        Assert.Empty(problems);
        Assert.Null(options.Placeholders["user-name"].Sample);
    }

    [Fact]
    public void No_descriptors_declare_nothing()
    {
        var options = new FlirtyOptions();

        Assert.Empty(DesignerPlaceholders.Declare(options, []));
        Assert.Empty(options.Placeholders);
    }
}
