using Flirty.Designer;
using Flirty.Designer.Models;
using Flirty.Validation;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Flirty.Tests.Designer;

/// <summary>
/// Verifies the composition step of #137: <see cref="DesignerApp.ConfigureServices"/> reads the
/// question-type descriptor file and declares it against the <b>core</b> registry, so the designer knows
/// host-declared types the same way a host does (ADR 0012).
/// </summary>
/// <remarks>
/// Driven through the real <see cref="DesignerApp"/> rather than through a hand-mirrored container: the
/// claim is precisely that the app's own wiring does this, and a mirror would keep passing after the app
/// stopped.
/// </remarks>
public sealed class DesignerAppQuestionTypesTests : IDisposable
{
    private readonly string _contentRoot =
        Path.Combine(Path.GetTempPath(), "flirty-designer-app-" + Guid.NewGuid().ToString("N"));

    public DesignerAppQuestionTypesTests() => Directory.CreateDirectory(_contentRoot);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_contentRoot, recursive: true);
        }
        catch (IOException)
        {
            // Temp directory; a leftover is not worth failing a test over.
        }
    }

    private void WriteDescriptors(string content)
        => File.WriteAllText(Path.Combine(_contentRoot, DesignerApp.QuestionTypesFileName), content);

    /// <summary>Runs the designer's real composition against the temp content root.</summary>
    private WebApplicationBuilder Configure()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = "Flirty.Designer",
            ContentRootPath = _contentRoot,
        });

        DesignerApp.ConfigureServices(builder);
        return builder;
    }

    /// <summary>
    /// <b>The EPIC 14 invariant, at the composition level.</b> Without a descriptor file the registry is
    /// empty and – the part that is easy to break – <see cref="IAnswerValidator"/> is still the plain
    /// singleton. The decorator is what turns it scoped, and a designer that declares nothing must not
    /// pay a lifetime change it never asked for. Asserted on the implementation type too, for the reason
    /// the core's own test gives: a refactor to a factory would keep the lifetime and break the promise.
    /// </summary>
    [Fact]
    public void Without_a_descriptor_file_the_designer_declares_nothing()
    {
        var builder = Configure();

        var descriptor = Assert.Single(
            builder.Services, service => service.ServiceType == typeof(IAnswerValidator));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        Assert.Equal(typeof(AnswerValidator), descriptor.ImplementationType);

        using var app = builder.Build();

        Assert.Empty(app.Services.GetRequiredService<FlirtyQuestionTypeRegistry>().Types);

        var source = app.Services.GetRequiredService<DesignerQuestionTypeSource>();
        Assert.False(source.FileExists);
        Assert.Empty(source.Problems);
    }

    /// <summary>
    /// With a file the types land in the core registry – name and sample included, which is what the
    /// dropdowns, the palette and the runner's prefill read.
    /// </summary>
    [Fact]
    public void A_descriptor_file_is_declared_on_the_core_registry()
    {
        WriteDescriptors(
            """
            {
              "questionTypes": [
                { "key": "color", "displayName": "Colour picker", "sample": "\"#ff0000\"" }
              ]
            }
            """);

        var builder = Configure();

        // The flip side of the test above: a declaration DOES buy the scoped decorator, here as
        // elsewhere. Worth pinning in the designer, because it is a lifetime change in a Blazor app.
        Assert.Equal(
            ServiceLifetime.Scoped,
            Assert.Single(builder.Services, service => service.ServiceType == typeof(IAnswerValidator))
                .Lifetime);

        using var app = builder.Build();

        var declared = Assert.Single(app.Services.GetRequiredService<FlirtyQuestionTypeRegistry>().Types);

        Assert.Equal("color", declared.Key);
        Assert.Equal("Colour picker", declared.DisplayName);
        Assert.Equal("\"#ff0000\"", declared.SampleValue);

        // The designer declares descriptors, never validators - the reason the semantic delta stays open
        // and the test runner has to say so.
        Assert.Null(declared.ValidatorType);

        var source = app.Services.GetRequiredService<DesignerQuestionTypeSource>();
        Assert.True(source.FileExists);
        Assert.Empty(source.Problems);
    }

    /// <summary>
    /// A hand-written file with a bad entry must not stop the designer from starting: the entry is
    /// skipped, reported through <see cref="DesignerQuestionTypeSource"/> (which the question-types page
    /// renders) and the usable entries are declared anyway.
    /// </summary>
    [Fact]
    public void A_broken_entry_is_reported_and_the_designer_still_starts()
    {
        WriteDescriptors(
            """
            {
              "questionTypes": [
                { "key": "NOT VALID", "displayName": "Broken" },
                { "key": "color", "displayName": "Colour picker" }
              ]
            }
            """);

        using var app = Configure().Build();

        Assert.Equal(
            "color",
            Assert.Single(app.Services.GetRequiredService<FlirtyQuestionTypeRegistry>().Types).Key);
        Assert.Single(app.Services.GetRequiredService<DesignerQuestionTypeSource>().Problems);
    }

    /// <summary>
    /// A file that is not JSON at all is the same story one level up, and worth its own case because it
    /// takes a different path: the parse fails before any entry exists.
    /// </summary>
    [Fact]
    public void A_broken_file_is_reported_and_the_designer_still_starts()
    {
        WriteDescriptors("{ this is not json");

        using var app = Configure().Build();

        Assert.Empty(app.Services.GetRequiredService<FlirtyQuestionTypeRegistry>().Types);
        Assert.Single(app.Services.GetRequiredService<DesignerQuestionTypeSource>().Problems);
    }
}
