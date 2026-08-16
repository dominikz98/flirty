using Flirty.Designer;
using Flirty.Designer.Models;
using Flirty.Placeholders;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Flirty.Tests.Designer;

/// <summary>
/// Verifies the composition step of #140: <see cref="DesignerApp.ConfigureServices"/> reads the
/// placeholder descriptor file and declares it against the <b>core</b> registry, so the designer knows
/// host-declared placeholders the same way a host does (ADR 0013) – mirroring
/// <see cref="DesignerAppQuestionTypesTests"/>.
/// </summary>
/// <remarks>
/// Driven through the real <see cref="DesignerApp"/> rather than a hand-mirrored container: the claim is
/// precisely that the app's own wiring does this, and a mirror would keep passing after the app stopped.
/// </remarks>
public sealed class DesignerAppPlaceholdersTests : IDisposable
{
    private readonly string _contentRoot =
        Path.Combine(Path.GetTempPath(), "flirty-designer-placeholders-" + Guid.NewGuid().ToString("N"));

    public DesignerAppPlaceholdersTests() => Directory.CreateDirectory(_contentRoot);

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
        => File.WriteAllText(Path.Combine(_contentRoot, DesignerApp.PlaceholdersFileName), content);

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

    [Fact]
    public void Without_a_descriptor_file_the_registry_is_empty_and_the_source_reports_no_file()
    {
        using var app = Configure().Build();

        Assert.Empty(app.Services.GetRequiredService<FlirtyPlaceholderRegistry>().Placeholders);

        var source = app.Services.GetRequiredService<DesignerPlaceholderSource>();
        Assert.False(source.FileExists);
        Assert.Empty(source.Problems);
    }

    [Fact]
    public void A_descriptor_file_is_declared_on_the_core_registry()
    {
        WriteDescriptors(
            """
            {
              "placeholders": [
                { "key": "user-name", "displayName": "User name", "sample": "Alice" }
              ]
            }
            """);

        using var app = Configure().Build();

        var declared = Assert.Single(app.Services.GetRequiredService<FlirtyPlaceholderRegistry>().Placeholders);

        Assert.Equal("user-name", declared.Key);
        Assert.Equal("User name", declared.DisplayName);
        Assert.Equal("Alice", declared.Sample);

        // The designer declares descriptors, never fillers - the reason a test run previews the sample.
        Assert.Null(declared.FillerType);

        var source = app.Services.GetRequiredService<DesignerPlaceholderSource>();
        Assert.True(source.FileExists);
        Assert.Empty(source.Problems);
    }

    [Fact]
    public void A_broken_entry_is_reported_and_the_designer_still_starts()
    {
        WriteDescriptors(
            """
            {
              "placeholders": [
                { "key": "NOT VALID", "displayName": "Broken" },
                { "key": "user-name", "displayName": "User name" }
              ]
            }
            """);

        using var app = Configure().Build();

        Assert.Equal(
            "user-name",
            Assert.Single(app.Services.GetRequiredService<FlirtyPlaceholderRegistry>().Placeholders).Key);
        Assert.Single(app.Services.GetRequiredService<DesignerPlaceholderSource>().Problems);
    }

    [Fact]
    public void A_broken_file_is_reported_and_the_designer_still_starts()
    {
        WriteDescriptors("{ this is not json");

        using var app = Configure().Build();

        Assert.Empty(app.Services.GetRequiredService<FlirtyPlaceholderRegistry>().Placeholders);
        Assert.Single(app.Services.GetRequiredService<DesignerPlaceholderSource>().Problems);
    }
}
