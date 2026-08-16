using Flirty.Designer.Services;

namespace Flirty.Tests.Designer;

/// <summary>
/// Verifies <see cref="PlaceholderDescriptorFile"/> – the parse half of the designer's placeholder
/// descriptors (#140), mirroring <see cref="QuestionTypeDescriptorFileTests"/>.
/// </summary>
/// <remarks>
/// The property under test throughout is that reading <b>never throws</b>. This file is hand-written and
/// read during <c>ConfigureServices</c>, so an exception here is a designer that will not start – over a
/// display name. Every failure therefore has to come back as a message instead.
/// </remarks>
public sealed class PlaceholderDescriptorFileTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "flirty-placeholders-" + Guid.NewGuid().ToString("N"));

    public PlaceholderDescriptorFileTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }

    private string Write(string content)
    {
        var path = Path.Combine(_directory, "placeholders.json");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void A_missing_file_is_not_a_problem()
    {
        var (descriptors, problems) =
            PlaceholderDescriptorFile.Read(Path.Combine(_directory, "absent.json"));

        Assert.Empty(descriptors);
        Assert.Empty(problems);
    }

    [Fact]
    public void An_empty_file_is_not_a_problem()
    {
        var (descriptors, problems) = PlaceholderDescriptorFile.Read(Write("   "));

        Assert.Empty(descriptors);
        Assert.Empty(problems);
    }

    [Fact]
    public void Descriptors_are_read_in_file_order()
    {
        var (descriptors, problems) = PlaceholderDescriptorFile.Read(Write(
            """
            {
              "placeholders": [
                { "key": "user-name", "displayName": "User name", "sample": "Alice" },
                { "key": "today", "displayName": "Today's date" }
              ]
            }
            """));

        Assert.Empty(problems);
        Assert.Equal(["user-name", "today"], descriptors.Select(descriptor => descriptor.Key));
        Assert.Equal("User name", descriptors[0].DisplayName);
        Assert.Equal("Alice", descriptors[0].Sample);
        Assert.Null(descriptors[1].Sample);
    }

    /// <summary>Written by a person, so the parser tolerates comments, trailing commas and casing.</summary>
    [Fact]
    public void Comments_trailing_commas_and_casing_are_tolerated()
    {
        var (descriptors, problems) = PlaceholderDescriptorFile.Read(Write(
            """
            {
              // greet the user by name
              "Placeholders": [
                { "Key": "user-name", "DISPLAYNAME": "User name" },
              ]
            }
            """));

        Assert.Empty(problems);
        Assert.Equal("User name", Assert.Single(descriptors).DisplayName);
    }

    [Fact]
    public void Broken_json_is_reported_rather_than_thrown()
    {
        var (descriptors, problems) = PlaceholderDescriptorFile.Read(Write("{ oops"));

        Assert.Empty(descriptors);
        Assert.Single(problems);
    }

    /// <summary>
    /// A file whose root object has no <c>placeholders</c> array is a likely typo, and silence would look
    /// exactly like "no file at all" – hence a message even though nothing failed.
    /// </summary>
    [Fact]
    public void A_missing_array_is_reported()
    {
        var (descriptors, problems) = PlaceholderDescriptorFile.Read(Write("""{ "markers": [] }"""));

        Assert.Empty(descriptors);
        Assert.Single(problems);
    }

    [Fact]
    public void A_null_entry_is_dropped()
    {
        var (descriptors, problems) = PlaceholderDescriptorFile.Read(Write(
            """{ "placeholders": [ null, { "key": "user-name", "displayName": "User name" } ] }"""));

        Assert.Empty(problems);
        Assert.Equal("user-name", Assert.Single(descriptors).Key);
    }
}
