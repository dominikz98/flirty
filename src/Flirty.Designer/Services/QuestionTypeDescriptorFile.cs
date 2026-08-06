using System.Text.Json;
using Flirty.Designer.Models;

namespace Flirty.Designer.Services;

/// <summary>
/// Reads the designer's question-type descriptor file (<c>question-types.json</c> in the ContentRoot,
/// beside <c>connection-profiles.json</c>).
/// </summary>
/// <remarks>
/// <para>
/// Reading <b>never throws</b>. A missing file is the normal case – without descriptors the designer
/// behaves exactly as it did after #136 – and a malformed one is reported as a problem rather than as a
/// startup crash, because this file is hand-written and a display name is not worth a dead tool.
/// </para>
/// <para>
/// The parser is deliberately more forgiving than <see cref="JsonConnectionProfileStore"/>'s: comments
/// and trailing commas are allowed, because that store's file is written by the UI while this one is
/// written by a person.
/// </para>
/// <para>
/// This class only parses. Whether an entry is <i>usable</i> is decided by the core in
/// <see cref="DesignerQuestionTypes.Declare"/> – the designer deliberately keeps no second copy of those
/// rules.
/// </para>
/// </remarks>
internal static class QuestionTypeDescriptorFile
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Reads the descriptors from the given file.</summary>
    /// <param name="filePath">Full path to the JSON file; it need not exist.</param>
    /// <returns>
    /// The descriptors in file order, and human-readable problems for whatever could not be read. Both
    /// lists are empty when the file is absent or empty.
    /// </returns>
    public static (IReadOnlyList<QuestionTypeDescriptor> Descriptors, IReadOnlyList<string> Problems) Read(
        string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!File.Exists(filePath))
        {
            return ([], []);
        }

        string json;
        try
        {
            json = File.ReadAllText(filePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ([], [$"The file could not be read: {exception.Message}"]);
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            return ([], []);
        }

        DescriptorDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<DescriptorDocument>(json, SerializerOptions);
        }
        catch (JsonException exception)
        {
            return ([], [$"The file is not valid JSON and was ignored entirely: {exception.Message}"]);
        }

        if (document?.QuestionTypes is not { } entries)
        {
            return ([], ["The file has no \"questionTypes\" array, so no custom question type was declared."]);
        }

        return (entries.OfType<QuestionTypeDescriptor>().ToList(), []);
    }

    /// <summary>Serialization container for the JSON file.</summary>
    private sealed class DescriptorDocument
    {
        public List<QuestionTypeDescriptor?>? QuestionTypes { get; set; }
    }
}
