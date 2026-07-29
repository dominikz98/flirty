using System.Globalization;
using System.Text.Json;
using Flirty.Domain;

namespace Flirty.Samples;

/// <summary>
/// Converts a raw text input into the JSON answer value expected by the engine, depending on the
/// <see cref="QuestionType"/> (the facade expects answers as raw JSON text).
/// </summary>
internal static class AnswerEncoder
{
    /// <summary>
    /// Encodes the raw input <paramref name="rawInput"/> as JSON according to the question type
    /// <paramref name="type"/>.
    /// </summary>
    /// <param name="type">The answer type of the question.</param>
    /// <param name="rawInput">The raw (unencoded) input, e.g. the option value or free text.</param>
    /// <returns>The answer value as JSON text.</returns>
    public static string Encode(QuestionType type, string rawInput)
    {
        var trimmed = (rawInput ?? string.Empty).Trim();

        return type switch
        {
            QuestionType.MultiChoice => JsonSerializer.Serialize(
                trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)),
            QuestionType.Number => decimal.TryParse(trimmed, NumberStyles.Any, CultureInfo.InvariantCulture, out var number)
                ? number.ToString(CultureInfo.InvariantCulture)
                : JsonSerializer.Serialize(trimmed),
            QuestionType.Boolean => IsAffirmative(trimmed) ? "true" : "false",
            // SingleChoice, FreeText and Date are encoded as a JSON string.
            _ => JsonSerializer.Serialize(trimmed),
        };
    }

    private static bool IsAffirmative(string value) =>
        value.Equals("true", StringComparison.OrdinalIgnoreCase)
        || value.Equals("ja", StringComparison.OrdinalIgnoreCase)
        || value.Equals("j", StringComparison.OrdinalIgnoreCase)
        || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
        || value == "1";
}
