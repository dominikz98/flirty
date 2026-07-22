using System.Globalization;
using System.Text.Json;
using Flirty.Domain;
using Flirty.Runtime.Admin;

namespace Flirty.Designer.Services;

/// <summary>
/// Übersetzt zwischen den Eingabefeldern des Designers und dem <b>rohen JSON-Text</b>, in dem die Engine
/// Antwortwerte entgegennimmt und speichert (<c>SubmitAnswerCommand.Value</c>,
/// <c>SessionAnswer.Value</c>).
/// </summary>
/// <remarks>
/// <para>
/// Verbindlich ist der Core-<c>AnswerValidator</c> (<c>src/Flirty/Validation/AnswerValidator.cs</c>) –
/// er entscheidet, welche JSON-Form je <see cref="QuestionType"/> durchgeht:
/// </para>
/// <list type="table">
/// <item><term><see cref="QuestionType.FreeText"/>, <see cref="QuestionType.Date"/>,
/// <see cref="QuestionType.SingleChoice"/></term><description>JSON-Zeichenkette</description></item>
/// <item><term><see cref="QuestionType.Number"/></term><description>rohes Zahlliteral (invariant)</description></item>
/// <item><term><see cref="QuestionType.Boolean"/></term><description><c>true</c> / <c>false</c></description></item>
/// <item><term><see cref="QuestionType.MultiChoice"/></term><description>JSON-Array von Zeichenketten</description></item>
/// </list>
/// <para>
/// Diese Klasse ist die <b>einzige</b> Stelle des Designers, die diesen Vertrag kennt: Der
/// <see cref="DesignerExpressionContext"/> leitet seine Beispielwerte ebenfalls von hier ab, damit
/// Ausdrucks-Validierung und Testlauf nicht auseinanderlaufen können.
/// </para>
/// </remarks>
internal static class AnswerValueCodec
{
    /// <summary>
    /// Kodiert eine Eingabe als rohen JSON-Antwortwert der angegebenen Frage.
    /// </summary>
    /// <param name="type">Der Antworttyp der Frage.</param>
    /// <param name="text">
    /// Der Text- bzw. Einzelwert (Freitext, Datum im ISO-Format, Zahl, gewählter Options-Wert,
    /// <c>true</c>/<c>false</c>); bei <see cref="QuestionType.MultiChoice"/> ignoriert.
    /// </param>
    /// <param name="selected">
    /// Die gewählten Options-Werte einer <see cref="QuestionType.MultiChoice"/>-Frage; sonst ignoriert.
    /// </param>
    /// <returns>Der rohe JSON-Text für die Engine.</returns>
    public static string Encode(QuestionType type, string? text, IReadOnlyList<string>? selected = null)
        => type switch
        {
            QuestionType.MultiChoice => JsonSerializer.Serialize(selected ?? []),
            QuestionType.Boolean => IsTrue(text) ? "true" : "false",
            QuestionType.Number => EncodeNumber(text),
            _ => JsonSerializer.Serialize(text ?? string.Empty),
        };

    /// <summary>
    /// Beschreibt einen gespeicherten Antwortwert für die Anzeige: Optionen erscheinen mit ihrer
    /// Beschriftung, Wahrheitswerte als „Ja“/„Nein“, Mehrfachauswahlen kommagetrennt.
    /// </summary>
    /// <param name="question">
    /// Die zugehörige Frage (für Typ und Options-Beschriftungen) oder <see langword="null"/>, wenn sie
    /// nicht (mehr) zum Dialog gehört – dann wird der Rohwert bestmöglich gelesen.
    /// </param>
    /// <param name="value">Der gespeicherte rohe JSON-Antwortwert.</param>
    /// <returns>Der anzuzeigende Text.</returns>
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
            QuestionType.Boolean => IsTrue(text) ? "Ja" : "Nein",
            QuestionType.SingleChoice => LabelOf(question, text),
            _ => text,
        };
    }

    /// <summary>
    /// Liest einen gespeicherten Antwortwert zurück in die Eingabefelder – das Gegenstück zu
    /// <see cref="Encode"/> für den Editier-Modus des Test-Runners.
    /// </summary>
    /// <param name="type">Der Antworttyp der Frage.</param>
    /// <param name="value">Der gespeicherte rohe JSON-Antwortwert.</param>
    /// <returns>
    /// Der Einzelwert (leer bei <see cref="QuestionType.MultiChoice"/>) und die gewählten Options-Werte.
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

    /// <summary>Die Beschriftung der Option mit diesem Wert; sonst der Rohwert.</summary>
    /// <param name="question">Die Frage mit ihren Antwortoptionen.</param>
    /// <param name="value">Der gespeicherte Options-Wert.</param>
    /// <returns>Die Beschriftung oder der Rohwert.</returns>
    private static string LabelOf(QuestionDetail question, string value)
        => question.Options.FirstOrDefault(option => option.Value == value)?.Label ?? value;

    /// <summary>
    /// Kodiert eine Zahleingabe als JSON-Zahl. Akzeptiert das deutsche Dezimalkomma und fällt bei
    /// unlesbarer Eingabe auf eine JSON-Zeichenkette zurück – so lehnt die <b>Engine</b> den Wert ab
    /// (mit ihrer Meldung), statt dass der Designer still etwas anderes einreicht.
    /// </summary>
    /// <param name="text">Die Eingabe.</param>
    /// <returns>Der rohe JSON-Text.</returns>
    private static string EncodeNumber(string? text)
    {
        var trimmed = (text ?? string.Empty).Trim().Replace(',', '.');

        return decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out var number)
            ? number.ToString(CultureInfo.InvariantCulture)
            : JsonSerializer.Serialize(text ?? string.Empty);
    }

    private static bool IsTrue(string? text)
        => bool.TryParse((text ?? string.Empty).Trim(), out var parsed) && parsed;

    /// <summary>Liest den Wert als JSON-Zeichenkette (wie der Core-<c>AnswerValidator</c>).</summary>
    /// <param name="value">Der rohe Wert.</param>
    /// <param name="result">Der gelesene Text bei Erfolg.</param>
    /// <returns><see langword="true"/>, wenn der Wurzelknoten eine JSON-Zeichenkette ist.</returns>
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

    /// <summary>Liest den Wert als JSON-Array von Zeichenketten (wie der Core-<c>AnswerValidator</c>).</summary>
    /// <param name="value">Der rohe Wert.</param>
    /// <param name="items">Die gelesenen Einträge bei Erfolg.</param>
    /// <returns><see langword="true"/>, wenn der Wurzelknoten ein Zeichenketten-Array ist.</returns>
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
