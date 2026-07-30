using System.Globalization;
using System.Text.Json;
using Flirty.Domain;
using Flirty.Runtime.Admin;

namespace Flirty.Designer.Services;

/// <summary>
/// Translates between the designer's input fields and the <b>raw JSON text</b> in which the engine
/// accepts and stores answer values (<c>SubmitAnswerCommand.Value</c>,
/// <c>SessionAnswer.Value</c>).
/// </summary>
/// <remarks>
/// <para>
/// The core <c>AnswerValidator</c> (<c>src/Flirty/Validation/AnswerValidator.cs</c>) is authoritative –
/// it decides which JSON form passes per <see cref="QuestionType"/>:
/// </para>
/// <list type="table">
/// <item><term><see cref="QuestionType.FreeText"/>, <see cref="QuestionType.Date"/>,
/// <see cref="QuestionType.SingleChoice"/></term><description>JSON string</description></item>
/// <item><term><see cref="QuestionType.Number"/></term><description>raw number literal (invariant)</description></item>
/// <item><term><see cref="QuestionType.Boolean"/></term><description><c>true</c> / <c>false</c></description></item>
/// <item><term><see cref="QuestionType.MultiChoice"/></term><description>JSON array of strings</description></item>
/// </list>
/// <para>
/// This class is the <b>only</b> place in the designer that knows this contract: the
/// <see cref="DesignerExpressionContext"/> derives its sample values from here too, so that expression
/// validation and test run cannot drift apart.
/// </para>
/// </remarks>
internal static class AnswerValueCodec
{
    /// <summary>
    /// Encodes an input as the raw JSON answer value of the given question.
    /// </summary>
    /// <param name="type">The answer type of the question.</param>
    /// <param name="text">
    /// The text or single value (free text, date in ISO format, number, chosen option value,
    /// <c>true</c>/<c>false</c>); ignored for <see cref="QuestionType.MultiChoice"/>.
    /// </param>
    /// <param name="selected">
    /// The chosen option values of a <see cref="QuestionType.MultiChoice"/> question; otherwise ignored.
    /// </param>
    /// <returns>The raw JSON text for the engine.</returns>
    public static string Encode(QuestionType type, string? text, IReadOnlyList<string>? selected = null)
        => type switch
        {
            QuestionType.MultiChoice => JsonSerializer.Serialize(selected ?? []),
            QuestionType.Boolean => IsTrue(text) ? "true" : "false",
            QuestionType.Number => EncodeNumber(text),
            _ => JsonSerializer.Serialize(text ?? string.Empty),
        };

    /// <summary>
    /// Describes a stored answer value for display: options appear with their label, boolean values
    /// as "Yes"/"No", multiple choices comma-separated.
    /// </summary>
    /// <param name="question">
    /// The associated question (for type and option labels) or <see langword="null"/> if it no longer
    /// belongs to the dialog – then the raw value is read as best as possible.
    /// </param>
    /// <param name="value">The stored raw JSON answer value.</param>
    /// <returns>The text to display.</returns>
    public static string Describe(QuestionDetail? question, string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (question?.Type == QuestionType.MultiChoice)
        {
            return TryReadStringArray(value, out var items)
                ? string.Join(", ", items.Select(item => LabelOf(question, item)))
                : value;
        }

        var text = TryReadJsonString(value, out var single) ? single : value.Trim();

        return question?.Type switch
        {
            QuestionType.Boolean => IsTrue(text) ? "Yes" : "No",
            QuestionType.SingleChoice => LabelOf(question, text),
            _ => text,
        };
    }

    /// <summary>
    /// Reads a stored answer value back into the input fields – the counterpart to
    /// <see cref="Encode"/> for the edit mode of the test runner.
    /// </summary>
    /// <param name="type">The answer type of the question.</param>
    /// <param name="value">The stored raw JSON answer value.</param>
    /// <returns>
    /// The single value (empty for <see cref="QuestionType.MultiChoice"/>) and the chosen option values.
    /// </returns>
    public static (string Text, IReadOnlyList<string> Selected) Decode(QuestionType type, string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (type == QuestionType.MultiChoice)
        {
            return (string.Empty, TryReadStringArray(value, out var items) ? items : []);
        }

        var text = TryReadJsonString(value, out var single) ? single : value.Trim();
        return (type == QuestionType.Boolean ? (IsTrue(text) ? "true" : "false") : text, []);
    }

    /// <summary>The label of the option with this value; otherwise the raw value.</summary>
    /// <param name="question">The question with its answer options.</param>
    /// <param name="value">The stored option value.</param>
    /// <returns>The label or the raw value.</returns>
    private static string LabelOf(QuestionDetail question, string value)
        => question.Options.FirstOrDefault(option => option.Value == value)?.Label ?? value;

    /// <summary>
    /// Encodes a number input as a JSON number. Accepts the German decimal comma and falls back to a
    /// JSON string on unreadable input – so the <b>engine</b> rejects the value (with its message)
    /// instead of the designer silently submitting something else.
    /// </summary>
    /// <param name="text">The input.</param>
    /// <returns>The raw JSON text.</returns>
    private static string EncodeNumber(string? text)
    {
        var trimmed = (text ?? string.Empty).Trim().Replace(',', '.');

        return decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out var number)
            ? number.ToString(CultureInfo.InvariantCulture)
            : JsonSerializer.Serialize(text ?? string.Empty);
    }

    private static bool IsTrue(string? text)
        => bool.TryParse((text ?? string.Empty).Trim(), out var parsed) && parsed;

    /// <summary>Reads the value as a JSON string (like the core <c>AnswerValidator</c>).</summary>
    /// <param name="value">The raw value.</param>
    /// <param name="result">The read text on success.</param>
    /// <returns><see langword="true"/> if the root node is a JSON string.</returns>
    private static bool TryReadJsonString(string value, out string result)
    {
        result = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(value);
            if (document.RootElement.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            result = document.RootElement.GetString() ?? string.Empty;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Reads the value as a JSON array of strings (like the core <c>AnswerValidator</c>).</summary>
    /// <param name="value">The raw value.</param>
    /// <param name="items">The read entries on success.</param>
    /// <returns><see langword="true"/> if the root node is a string array.</returns>
    private static bool TryReadStringArray(string value, out IReadOnlyList<string> items)
    {
        items = [];
        try
        {
            using var document = JsonDocument.Parse(value);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var list = new List<string>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.String)
                {
                    return false;
                }

                list.Add(element.GetString() ?? string.Empty);
            }

            items = list;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
