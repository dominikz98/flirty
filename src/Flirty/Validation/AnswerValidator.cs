using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Flirty.Domain;

namespace Flirty.Validation;

/// <summary>
/// Default-Implementierung von <see cref="IAnswerValidator"/>: prüft den rohen JSON-Antwortwert je
/// <see cref="QuestionType"/> und wendet die typ-skopierten <see cref="ValidationRules"/> an. Die
/// Werte werden – wie im <c>DynamicExpressoExpressionEvaluator</c> (#23) – tolerant gelesen: gültiges
/// JSON wird typisiert interpretiert, ansonsten gilt der rohe Text als Zeichenkette.
/// </summary>
/// <remarks>
/// Die Klasse ist zustandslos und daher als Singleton nutzbar (DI-Verdrahtung in <c>AddFlirty()</c>).
/// </remarks>
public sealed class AnswerValidator : IAnswerValidator
{
    private const int RegexTimeoutMilliseconds = 250;

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(RegexTimeoutMilliseconds);

    private static readonly JsonSerializerOptions RuleOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <inheritdoc />
    public AnswerValidationResult Validate(Question question, string value)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(value);

        var rules = ParseRules(question);

        return question.Type switch
        {
            QuestionType.FreeText => ValidateFreeText(value, rules),
            QuestionType.Number => ValidateNumber(value, rules),
            QuestionType.Boolean => ValidateBoolean(value),
            QuestionType.Date => ValidateDate(value),
            QuestionType.SingleChoice => ValidateSingleChoice(question, value),
            QuestionType.MultiChoice => ValidateMultiChoice(question, value),
            _ => throw new InvalidOperationException(
                $"Unbekannter Fragetyp '{question.Type}' der Frage '{question.Id}'."),
        };
    }

    // ---- Typprüfungen -------------------------------------------------------------------------

    private static AnswerValidationResult ValidateFreeText(string value, ValidationRules? rules)
        => ApplyStringRules(TryReadJsonString(value, out var text) ? text : value, rules);

    private static AnswerValidationResult ValidateNumber(string value, ValidationRules? rules)
    {
        if (!TryReadNumber(value, out var number))
        {
            return AnswerValidationResult.Invalid($"Der Wert '{Describe(value)}' ist keine gültige Zahl.");
        }

        var errors = new List<string>();
        if (rules?.Min is decimal min && number < min)
        {
            errors.Add($"Der Wert {number} unterschreitet das Minimum {min}.");
        }

        if (rules?.Max is decimal max && number > max)
        {
            errors.Add($"Der Wert {number} überschreitet das Maximum {max}.");
        }

        return errors.Count == 0 ? AnswerValidationResult.Valid : AnswerValidationResult.Invalid([.. errors]);
    }

    private static AnswerValidationResult ValidateBoolean(string value)
        => IsBoolean(value)
            ? AnswerValidationResult.Valid
            : AnswerValidationResult.Invalid(
                $"Der Wert '{Describe(value)}' ist kein gültiger Wahrheitswert (true/false erwartet).");

    private static AnswerValidationResult ValidateDate(string value)
    {
        var text = TryReadJsonString(value, out var s) ? s : value;
        var isDate = DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
                  || DateOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

        return isDate
            ? AnswerValidationResult.Valid
            : AnswerValidationResult.Invalid(
                $"Der Wert '{Describe(value)}' ist kein gültiges Datum (ISO-8601 erwartet).");
    }

    private static AnswerValidationResult ValidateSingleChoice(Question question, string value)
    {
        var selected = TryReadJsonString(value, out var text) ? text : value;
        return AllowedValues(question).Contains(selected)
            ? AnswerValidationResult.Valid
            : AnswerValidationResult.Invalid(
                $"Die Auswahl '{Describe(selected)}' ist keine gültige Option der Frage '{question.Key}'.");
    }

    private static AnswerValidationResult ValidateMultiChoice(Question question, string value)
    {
        if (!TryReadStringArray(value, out var selections))
        {
            return AnswerValidationResult.Invalid(
                $"Der Wert '{Describe(value)}' ist keine gültige Mehrfachauswahl "
                + "(erwartet wird ein JSON-Array von Zeichenketten).");
        }

        var allowed = AllowedValues(question);
        var unknown = selections.Where(selection => !allowed.Contains(selection)).ToList();

        return unknown.Count == 0
            ? AnswerValidationResult.Valid
            : AnswerValidationResult.Invalid(
                $"Die Auswahl(en) {string.Join(", ", unknown.Select(u => $"'{Describe(u)}'"))} "
                + $"sind keine gültigen Optionen der Frage '{question.Key}'.");
    }

    // ---- Regeln -------------------------------------------------------------------------------

    private static AnswerValidationResult ApplyStringRules(string text, ValidationRules? rules)
    {
        if (rules is null)
        {
            return AnswerValidationResult.Valid;
        }

        var errors = new List<string>();
        if (rules.MinLength is int minLength && text.Length < minLength)
        {
            errors.Add($"Der Wert ist mit {text.Length} Zeichen kürzer als die Mindestlänge {minLength}.");
        }

        if (rules.MaxLength is int maxLength && text.Length > maxLength)
        {
            errors.Add($"Der Wert überschreitet mit {text.Length} Zeichen die Maximallänge {maxLength}.");
        }

        if (!string.IsNullOrEmpty(rules.Pattern) && !MatchesPattern(text, rules.Pattern))
        {
            errors.Add($"Der Wert entspricht nicht dem Muster '{rules.Pattern}'.");
        }

        return errors.Count == 0 ? AnswerValidationResult.Valid : AnswerValidationResult.Invalid([.. errors]);
    }

    private static bool MatchesPattern(string text, string pattern)
    {
        try
        {
            return Regex.IsMatch(text, pattern, RegexOptions.None, RegexTimeout);
        }
        catch (RegexParseException ex)
        {
            throw new InvalidOperationException(
                $"Das Validierungs-Muster '{pattern}' ist kein gültiger regulärer Ausdruck.", ex);
        }
        catch (RegexMatchTimeoutException)
        {
            // Pathologische Eingabe (Backtracking-Explosion) gilt als Nicht-Treffer -> ungültig.
            return false;
        }
    }

    // ---- Parsing-Helfer -----------------------------------------------------------------------

    /// <summary>
    /// Liest die konfigurierten Regeln der Frage. Ist <see cref="Question.ValidationRules"/> leer, gibt
    /// es keine Regeln (<see langword="null"/>); ist der Text kein gültiges JSON, ist die Frage
    /// fehlkonfiguriert.
    /// </summary>
    private static ValidationRules? ParseRules(Question question)
    {
        if (string.IsNullOrWhiteSpace(question.ValidationRules))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ValidationRules>(question.ValidationRules, RuleOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Die ValidationRules der Frage '{question.Id}' sind kein gültiges JSON.", ex);
        }
    }

    private static HashSet<string> AllowedValues(Question question)
        => question.Options.Select(option => option.Value).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Liest den Wert als JSON-Zeichenkette. Ist der Wurzelknoten eine JSON-Zeichenkette, wird deren
    /// Inhalt geliefert; andernfalls (Nicht-Zeichenketten-JSON oder ungültiges JSON) schlägt der
    /// Versuch fehl und der Aufrufer verwendet den rohen Text.
    /// </summary>
    private static bool TryReadJsonString(string value, out string result)
    {
        result = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(value);
            if (document.RootElement.ValueKind == JsonValueKind.String)
            {
                result = document.RootElement.GetString() ?? string.Empty;
                return true;
            }

            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryReadNumber(string value, out decimal number)
    {
        number = 0m;
        try
        {
            using var document = JsonDocument.Parse(value);
            return document.RootElement.ValueKind switch
            {
                JsonValueKind.Number => document.RootElement.TryGetDecimal(out number),
                JsonValueKind.String => TryParseDecimal(document.RootElement.GetString(), out number),
                _ => false,
            };
        }
        catch (JsonException)
        {
            return TryParseDecimal(value, out number);
        }
    }

    private static bool TryParseDecimal(string? text, out decimal number)
        => decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out number);

    private static bool IsBoolean(string value)
    {
        try
        {
            using var document = JsonDocument.Parse(value);
            return document.RootElement.ValueKind switch
            {
                JsonValueKind.True or JsonValueKind.False => true,
                JsonValueKind.String => bool.TryParse(document.RootElement.GetString(), out _),
                _ => false,
            };
        }
        catch (JsonException)
        {
            return bool.TryParse(value, out _);
        }
    }

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

    /// <summary>Kürzt lange Werte für Fehlermeldungen, damit diese lesbar bleiben.</summary>
    private static string Describe(string value)
        => value.Length <= 64 ? value : string.Concat(value.AsSpan(0, 61), "...");
}
