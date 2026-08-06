using Flirty.Designer.Services;

namespace Flirty.Tests.Designer;

/// <summary>
/// Verifies <see cref="QuestionTypeDescriptorFile"/> – the parse half of the designer's question-type
/// descriptors (#137).
/// </summary>
/// <remarks>
/// The property under test throughout is that reading <b>never throws</b>. This file is hand-written and
/// read during <c>ConfigureServices</c>, so an exception here is a designer that will not start – over a
/// display name. Every failure therefore has to come back as a message instead.
/// </remarks>
public sealed class QuestionTypeDescriptorFileTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "flirty-descriptors-" + Guid.NewGuid().ToString("N"));

    public QuestionTypeDescriptorFileTests() => Directory.CreateDirectory(_directory);

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
        var path = Path.Combine(_directory, "question-types.json");
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>
    /// The normal case. Without descriptors the designer behaves exactly as after #136, so a missing file
    /// must not even be reported as a problem – it is not one.
    /// </summary>
    [Fact]
    public void A_missing_file_is_not_a_problem()
    {
        var (descriptors, problems) =
            QuestionTypeDescriptorFile.Read(Path.Combine(_directory, "absent.json"));

        Assert.Empty(descriptors);
        Assert.Empty(problems);
    }

    [Fact]
    public void An_empty_file_is_not_a_problem()
    {
        var (descriptors, problems) = QuestionTypeDescriptorFile.Read(Write("   "));

        Assert.Empty(descriptors);
        Assert.Empty(problems);
    }

    [Fact]
    public void Descriptors_are_read_in_file_order()
    {
        var (descriptors, problems) = QuestionTypeDescriptorFile.Read(Write(
            """
            {
              "questionTypes": [
                { "key": "color", "displayName": "Colour picker", "sample": "\"#ff0000\"" },
                { "key": "address", "displayName": "Postal address" }
              ]
            }
            """));

        Assert.Empty(problems);
        Assert.Equal(["color", "address"], descriptors.Select(descriptor => descriptor.Key));
        Assert.Equal("Colour picker", descriptors[0].DisplayName);
        Assert.Equal("\"#ff0000\"", descriptors[0].Sample);
        Assert.Null(descriptors[1].Sample);
    }

    /// <summary>
    /// Written by a person, not by the UI – unlike <c>connection-profiles.json</c>. So the parser accepts
    /// what a person leaves behind, and reads the property names case-insensitively.
    /// </summary>
    [Fact]
    public void Comments_trailing_commas_and_casing_are_tolerated()
    {
        var (descriptors, problems) = QuestionTypeDescriptorFile.Read(Write(
            """
            {
              // the host declares this one with a validator
              "QuestionTypes": [
                { "Key": "color", "DISPLAYNAME": "Colour picker" },
              ]
            }
            """));

        Assert.Empty(problems);
        Assert.Equal("Colour picker", Assert.Single(descriptors).DisplayName);
    }

    /// <summary>
    /// Broken JSON loses the whole file – there is no honest way to salvage half of it – but it is
    /// reported rather than thrown, and the designer starts.
    /// </summary>
    [Fact]
    public void Broken_json_is_reported_rather_than_thrown()
    {
        var (descriptors, problems) = QuestionTypeDescriptorFile.Read(Write("{ oops"));

        Assert.Empty(descriptors);
        Assert.Single(problems);
    }

    /// <summary>
    /// A file whose root object simply has no <c>questionTypes</c> is a likely typo, and silence would
    /// look exactly like "no file at all" – hence a message even though nothing failed.
    /// </summary>
    [Fact]
    public void A_missing_array_is_reported()
    {
        var (descriptors, problems) = QuestionTypeDescriptorFile.Read(Write("""{ "types": [] }"""));

        Assert.Empty(descriptors);
        Assert.Single(problems);
    }

    /// <summary>A null entry in the array is dropped instead of becoming a null reference later.</summary>
    [Fact]
    public void A_null_entry_is_dropped()
    {
        var (descriptors, problems) = QuestionTypeDescriptorFile.Read(Write(
            """{ "questionTypes": [ null, { "key": "color", "displayName": "Colour picker" } ] }"""));

        Assert.Empty(problems);
        Assert.Equal("color", Assert.Single(descriptors).Key);
    }
}
