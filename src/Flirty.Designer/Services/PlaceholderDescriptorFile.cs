using System.Text.Json;
using Flirty.Designer.Models;

namespace Flirty.Designer.Services;

/// <summary>
/// Reads the designer's placeholder descriptor file (<c>placeholders.json</c> in the ContentRoot, beside
/// <c>question-types.json</c> and <c>connection-profiles.json</c>).
/// </summary>
/// <remarks>
/// <para>
/// Reading <b>never throws</b>. A missing file is the normal case – without descriptors the designer shows
/// a marker's raw key and behaves exactly as it did before #140 – and a malformed one is reported as a
/// problem rather than as a startup crash, because this file is hand-written and a display name is not
/// worth a dead tool.
/// </para>
/// <para>
/// The parser mirrors <see cref="QuestionTypeDescriptorFile"/> exactly (comments and trailing commas
/// allowed, because the file is written by a person). This class only parses; whether an entry is
/// <i>usable</i> is decided by the core in <see cref="DesignerPlaceholders.Declare"/> – the designer keeps
/// no second copy of those rules.
/// </para>
/// </remarks>
internal static class PlaceholderDescriptorFile
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
    public static (IReadOnlyList<PlaceholderDescriptor> Descriptors, IReadOnlyList<string> Problems) Read(
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

        if (document?.Placeholders is not { } entries)
        {
            return ([], ["The file has no \"placeholders\" array, so no placeholder was declared."]);
        }

        return (entries.OfType<PlaceholderDescriptor>().ToList(), []);
    }

    /// <summary>Serialization container for the JSON file.</summary>
    private sealed class DescriptorDocument
    {
        public List<PlaceholderDescriptor?>? Placeholders { get; set; }
    }
}
