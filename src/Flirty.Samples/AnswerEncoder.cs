using System.Globalization;
using System.Text.Json;
using Flirty.Domain;

namespace Flirty.Samples;

/// <summary>
/// Wandelt eine rohe Text-Eingabe abhängig vom <see cref="QuestionType"/> in den von der Engine
/// erwarteten JSON-Antwortwert um (die Facade erwartet Antworten als rohen JSON-Text).
/// </summary>
internal static class AnswerEncoder
{
    /// <summary>
    /// Kodiert die Roh-Eingabe <paramref name="rawInput"/> als JSON gemäß dem Fragetyp
    /// <paramref name="type"/>.
    /// </summary>
    /// <param name="type">Der Antworttyp der Frage.</param>
    /// <param name="rawInput">Die rohe (unkodierte) Eingabe, z. B. der Options-Wert oder ein Freitext.</param>
    /// <returns>Der Antwortwert als JSON-Text.</returns>
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
            // SingleChoice, FreeText und Date werden als JSON-String kodiert.
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
