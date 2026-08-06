using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Flirty.Domain;

namespace Flirty.Validation;

/// <summary>
/// Default implementation of <see cref="IAnswerValidator"/>: validates the raw JSON answer value per
/// <see cref="QuestionType"/> and applies the type-scoped <see cref="ValidationRules"/>. The values are
/// read tolerantly – as in the <c>DynamicExpressoExpressionEvaluator</c> (#23): valid JSON is
/// interpreted in a typed way, otherwise the raw text counts as a string.
/// </summary>
/// <remarks>
/// The class is stateless and therefore usable as a singleton (DI wiring in <c>AddFlirty()</c>).
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
            QuestionType.Json => ValidateJson(value),
            _ => throw new InvalidOperationException(
                $"Unknown question type '{question.Type}' of question '{question.Id}'."),
        };
    }

    // ---- Type checks --------------------------------------------------------------------------

    private static AnswerValidationResult ValidateFreeText(string value, ValidationRules? rules)
        => ApplyStringRules(TryReadJsonString(value, out var text) ? text : value, rules);

    private static AnswerValidationResult ValidateNumber(string value, ValidationRules? rules)
    {
        if (!TryReadNumber(value, out var number))
        {
            return AnswerValidationResult.Invalid($"The value '{Describe(value)}' is not a valid number.");
        }

        var errors = new List<string>();
        if (rules?.Min is decimal min && number < min)
        {
            errors.Add($"The value {number} is below the minimum {min}.");
        }

        if (rules?.Max is decimal max && number > max)
        {
            errors.Add($"The value {number} exceeds the maximum {max}.");
        }

        return errors.Count == 0 ? AnswerValidationResult.Valid : AnswerValidationResult.Invalid([.. errors]);
    }

    private static AnswerValidationResult ValidateBoolean(string value)
        => IsBoolean(value)
            ? AnswerValidationResult.Valid
            : AnswerValidationResult.Invalid(
                $"The value '{Describe(value)}' is not a valid boolean (true/false expected).");

    private static AnswerValidationResult ValidateDate(string value)
    {
        var text = TryReadJsonString(value, out var s) ? s : value;
        var isDate = DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
                  || DateOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

        return isDate
            ? AnswerValidationResult.Valid
            : AnswerValidationResult.Invalid(
                $"The value '{Describe(value)}' is not a valid date (ISO-8601 expected).");
    }

    private static AnswerValidationResult ValidateSingleChoice(Question question, string value)
    {
        var selected = TryReadJsonString(value, out var text) ? text : value;
        return AllowedValues(question).Contains(selected)
            ? AnswerValidationResult.Valid
            : AnswerValidationResult.Invalid(
                $"The selection '{Describe(selected)}' is not a valid option of question '{question.Key}'.");
    }

    private static AnswerValidationResult ValidateMultiChoice(Question question, string value)
    {
        if (!TryReadStringArray(value, out var selections))
        {
            return AnswerValidationResult.Invalid(
                $"The value '{Describe(value)}' is not a valid multi-selection "
                + "(a JSON array of strings is expected).");
        }

        var allowed = AllowedValues(question);
        var unknown = selections.Where(selection => !allowed.Contains(selection)).ToList();

        return unknown.Count == 0
            ? AnswerValidationResult.Valid
            : AnswerValidationResult.Invalid(
                $"The selection(s) {string.Join(", ", unknown.Select(u => $"'{Describe(u)}'"))} "
                + $"are not valid options of question '{question.Key}'.");
    }

    /// <summary>
    /// Checks well-formedness, and nothing else – that is the whole contract of
    /// <see cref="QuestionType.Json"/>. Every <see cref="ValidationRules"/> field is FreeText- or
    /// Number-scoped, so none of them applies here, exactly as <c>Min</c> does not apply to a
    /// FreeText question. Semantics on top of this come from a host-declared custom question type.
    /// </summary>
    private static AnswerValidationResult ValidateJson(string value)
    {
        try
        {
            using var document = JsonDocument.Parse(value);
            return AnswerValidationResult.Valid;
        }
        catch (JsonException)
        {
            // The library's own message carries byte offsets and resource strings; it describes a
            // parser state, not the author's mistake. Own message instead - the same reason the
            // expression sandbox stopped forwarding the DynamicExpresso wording (#97).
            return AnswerValidationResult.Invalid(
                $"The value '{Describe(value)}' is not valid JSON "
                + "(a JSON document is expected; a text value must be quoted).");
        }
    }

    // ---- Rules --------------------------------------------------------------------------------

    private static AnswerValidationResult ApplyStringRules(string text, ValidationRules? rules)
    {
        if (rules is null)
        {
            return AnswerValidationResult.Valid;
        }

        var errors = new List<string>();
        if (rules.MinLength is int minLength && text.Length < minLength)
        {
            errors.Add($"The value is {text.Length} characters long, shorter than the minimum length {minLength}.");
        }

        if (rules.MaxLength is int maxLength && text.Length > maxLength)
        {
            errors.Add($"The value is {text.Length} characters long, exceeding the maximum length {maxLength}.");
        }

        if (!string.IsNullOrEmpty(rules.Pattern) && !MatchesPattern(text, rules.Pattern))
        {
            errors.Add($"The value does not match the pattern '{rules.Pattern}'.");
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
                $"The validation pattern '{pattern}' is not a valid regular expression.", ex);
        }
        catch (RegexMatchTimeoutException)
        {
            // Pathological input (backtracking explosion) counts as a non-match -> invalid.
            return false;
        }
    }

    // ---- Parsing helpers ----------------------------------------------------------------------

    /// <summary>
    /// Reads the configured rules of the question. If <see cref="Question.ValidationRules"/> is empty,
    /// there are no rules (<see langword="null"/>); if the text is not valid JSON, the question is
    /// misconfigured.
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
                $"The ValidationRules of question '{question.Id}' are not valid JSON.", ex);
        }
    }

    private static HashSet<string> AllowedValues(Question question)
        => question.Options.Select(option => option.Value).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Reads the value as a JSON string. If the root node is a JSON string, its content is returned;
    /// otherwise (non-string JSON or invalid JSON) the attempt fails and the caller uses the raw text.
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

    /// <summary>Truncates long values for error messages so they stay readable.</summary>
    private static string Describe(string value)
        => value.Length <= 64 ? value : string.Concat(value.AsSpan(0, 61), "...");
}
