using Flirty.Designer.Models;
using Flirty.Designer.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Flirty.Tests.Designer;

/// <summary>
/// Verifies <see cref="DesignerQuestionTypes.Declare"/> – the half of #137 that turns parsed descriptors
/// into real declarations on the core registry.
/// </summary>
/// <remarks>
/// <para>
/// The whole point of this class is that it keeps <b>no</b> copy of the core's validity rules: it calls
/// <c>AddQuestionType</c> and catches what that throws. These tests therefore drive the real
/// <see cref="FlirtyOptions"/>, so a future change to the core's rules shows up here rather than being
/// masked by a designer-side re-implementation that agrees with the old ones.
/// </para>
/// <para>
/// And a bad entry must be <b>skipped, not fatal</b>. The call site is
/// <c>DesignerApp.ConfigureServices</c>; an exception there is a designer that will not start.
/// </para>
/// </remarks>
public sealed class DesignerQuestionTypesTests
{
    private static QuestionTypeDescriptor Descriptor(string key, string name = "Colour picker", string? sample = null)
        => new() { Key = key, DisplayName = name, Sample = sample };

    [Fact]
    public void Valid_descriptors_are_declared()
    {
        var options = new FlirtyOptions();

        var problems = DesignerQuestionTypes.Declare(
            options,
            [Descriptor("color"), Descriptor("address", "Postal address", """{"city":"Berlin"}""")]);

        Assert.Empty(problems);
        Assert.Equal(["address", "color"], options.QuestionTypes.Keys.Order());
        Assert.Equal("Postal address", options.QuestionTypes["address"].DisplayName);
        Assert.Equal("""{"city":"Berlin"}""", options.QuestionTypes["address"].SampleValue);
    }

    /// <summary>
    /// The designer declares descriptors, never validators – a validator is code and lives in the host
    /// process. This is the fact the delta note in the test runner rests on, so it is pinned rather than
    /// assumed.
    /// </summary>
    [Fact]
    public void A_declared_type_carries_no_validator()
    {
        var options = new FlirtyOptions();

        _ = DesignerQuestionTypes.Declare(options, [Descriptor("color")]);

        Assert.Null(options.QuestionTypes["color"].ValidatorType);
    }

    /// <summary>
    /// The load-bearing case: one unusable entry must not cost the others. Each row here is refused by a
    /// different guard inside <c>AddQuestionType</c> – charset, blank name, malformed sample, duplicate.
    /// </summary>
    [Fact]
    public void An_unusable_entry_is_skipped_and_reported()
    {
        var options = new FlirtyOptions();

        var problems = DesignerQuestionTypes.Declare(
            options,
            [
                Descriptor("Colour"),                       // uppercase - outside [a-z0-9-]
                Descriptor("blank", name: "  "),            // no display name
                Descriptor("broken", sample: "{oops"),      // sample is not JSON
                Descriptor("color"),
                Descriptor("color", "Second colour"),       // duplicate key
            ]);

        Assert.Equal(4, problems.Count);
        Assert.Equal("color", Assert.Single(options.QuestionTypes).Key);

        // The position makes a message findable in a file that has no line numbers on screen.
        Assert.StartsWith("Entry 1 (\"Colour\")", problems[0], StringComparison.Ordinal);
        Assert.StartsWith("Entry 5 (\"color\")", problems[3], StringComparison.Ordinal);
    }

    /// <summary>
    /// An empty <c>"sample"</c> in the file means "none". Passing it through would hit the core's JSON
    /// check and turn a blank field into a skipped entry, which is not what writing nothing means.
    /// </summary>
    [Fact]
    public void An_empty_sample_is_read_as_none()
    {
        var options = new FlirtyOptions();

        var problems = DesignerQuestionTypes.Declare(options, [Descriptor("color", sample: "   ")]);

        Assert.Empty(problems);
        Assert.Null(options.QuestionTypes["color"].SampleValue);
    }

    [Fact]
    public void No_descriptors_declare_nothing()
    {
        var options = new FlirtyOptions();

        Assert.Empty(DesignerQuestionTypes.Declare(options, []));
        Assert.Empty(options.QuestionTypes);
    }
}
