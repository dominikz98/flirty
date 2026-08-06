using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Flirty.Designer.Services;
using Flirty.Domain;
using Flirty.Runtime.Admin;
using Flirty.Validation;

namespace Flirty.Designer.Models;

/// <summary>
/// Form model of the question editor (#39). Deliberately mutable (settable properties), so the Blazor
/// <c>EditForm</c> can bind directly to it; the annotations mirror those of the
/// <c>CreateQuestionCommand</c>/<c>UpdateQuestionCommand</c>, so violations already surface in the browser
/// and not only in the engine's <c>ValidationPipelineBehavior</c>.
/// </summary>
/// <remarks>
/// <para>
/// Besides the question's metadata, the model maps the JSON-stored
/// <see cref="Flirty.Domain.Question.ValidationRules"/> onto individual input fields. The authority for
/// that is the public core type <see cref="ValidationRules"/> – the schema is <b>not</b> duplicated here
/// but used directly as the serialization type.
/// </para>
/// <para>
/// If the stored JSON contains fields that <see cref="ValidationRules"/> does not know (or is not a valid
/// JSON object at all), <see cref="From"/> switches to <see cref="UseRawJson"/>. Otherwise saving would
/// silently discard the foreign fields.
/// </para>
/// </remarks>
internal sealed class QuestionFormModel
{
    /// <summary>
    /// Time limit for the pattern check. Identical to <c>AnswerValidator.RegexTimeout</c>, so that nothing
    /// passes as valid in the designer that the engine judges differently at runtime.
    /// </summary>
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

    /// <summary>The JSON fields supported by <see cref="ValidationRules"/> (case-insensitive as in the <see cref="AnswerValidator"/>).</summary>
    private static readonly HashSet<string> KnownRuleProperties =
        new(StringComparer.OrdinalIgnoreCase) { "minLength", "maxLength", "min", "max", "pattern" };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>The stable business key of the question (must be unique within the dialog).</summary>
    [Required(ErrorMessage = "Please enter a key.")]
    public string Key { get; set; } = string.Empty;

    /// <summary>The displayed question text.</summary>
    [Required(ErrorMessage = "Please enter a question text.")]
    public string Text { get; set; } = string.Empty;

    /// <summary>The answer type of the question.</summary>
    public QuestionType Type { get; set; } = QuestionType.FreeText;

    /// <summary>
    /// The key of a host-declared custom question type; only meaningful with
    /// <see cref="QuestionType.Json"/>. The designer deliberately does not check it against a registry –
    /// it does not have one, and an unknown key is not an error but degrades to the plain JSON check.
    /// </summary>
    public string? CustomTypeKey { get; set; }

    /// <summary>Indicates whether an answer to the question is required.</summary>
    public bool IsRequired { get; set; }

    /// <summary>Minimum length of the text (only <see cref="QuestionType.FreeText"/>).</summary>
    public int? MinLength { get; set; }

    /// <summary>Maximum length of the text (only <see cref="QuestionType.FreeText"/>).</summary>
    public int? MaxLength { get; set; }

    /// <summary>Smallest allowed value (only <see cref="QuestionType.Number"/>).</summary>
    public decimal? Min { get; set; }

    /// <summary>Largest allowed value (only <see cref="QuestionType.Number"/>).</summary>
    public decimal? Max { get; set; }

    /// <summary>Regular expression the text must match (only <see cref="QuestionType.FreeText"/>).</summary>
    public string? Pattern { get; set; }

    /// <summary>
    /// Indicates whether the rules are edited as raw JSON. Set by <see cref="From"/> when the stored JSON
    /// cannot be mapped losslessly onto the individual fields.
    /// </summary>
    public bool UseRawJson { get; set; }

    /// <summary>The raw-edited rule JSON; only relevant when <see cref="UseRawJson"/> is set.</summary>
    public string? RawJson { get; set; }

    /// <summary>Creates a form model from an existing question.</summary>
    /// <param name="question">The question view from the admin CRUD.</param>
    /// <returns>The populated form model.</returns>
    public static QuestionFormModel From(QuestionDetail question)
    {
        ArgumentNullException.ThrowIfNull(question);

        var model = new QuestionFormModel
        {
            Key = question.Key,
            Text = question.Text,
            Type = question.Type,
            CustomTypeKey = question.CustomTypeKey,
            IsRequired = question.IsRequired,
        };

        model.ReadValidationRules(question.ValidationRules);
        return model;
    }

    /// <summary>
    /// The custom type key as it should be saved: trimmed, and dropped entirely when the type is not
    /// <see cref="QuestionType.Json"/>.
    /// </summary>
    /// <returns>The key to store, or <see langword="null"/>.</returns>
    /// <remarks>
    /// The single source of that rule for every save path, so switching the type away from
    /// <c>Json</c> silently drops the key rather than producing a 400 from the core guard – the same
    /// shape as "only take the rules that apply to the type" in <see cref="TryBuildValidationRules"/>.
    /// </remarks>
    public string? NormalizedCustomTypeKey()
        => Type != QuestionType.Json || string.IsNullOrWhiteSpace(CustomTypeKey)
            ? null
            : CustomTypeKey.Trim();

    /// <summary>
    /// Suggests a free key for a question created <b>by gesture</b>: a stem based on the question type
    /// (<c>text</c>, <c>number</c>, <c>date</c>, <c>choice</c>, <c>multi</c>, <c>yesno</c>), with an
    /// appended number when taken (<c>text2</c>, <c>text3</c> …).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately different from <see cref="LoopFormModel.SuggestCollectionKey"/>, which returns
    /// <b>empty</b> on a collision: there the suggestion fills a required field the author has to choose
    /// deliberately anyway. Here it carries a gesture that writes immediately – an empty key would make it
    /// fail at the <c>CreateQuestionCommand</c>. The key is a placeholder the inspector makes renameable.
    /// </para>
    /// <para>
    /// It is checked against question <b>and</b> collection keys: a question key that duplicates a
    /// <c>CollectionKey</c> is shadowed by it in the expression context – the gesture would otherwise
    /// produce a warning on the spot. <see cref="DesignerExpressionContext.IsBindable"/> at the same time
    /// keeps the reserved names (<c>now</c>, <c>iterationIndex</c>, <c>session</c>) free.
    /// </para>
    /// </remarks>
    /// <param name="type">The question type that determines the stem.</param>
    /// <param name="detail">The dialog including its graph, against which collisions are checked.</param>
    /// <param name="customTypeKey">
    /// The host-declared type's key (#137), if the gesture created one – a better stem than the generic
    /// <c>json</c>. Its <c>-</c> becomes <c>_</c>: the declaration charset allows a hyphen, an expression
    /// identifier does not, and without the swap
    /// <see cref="DesignerExpressionContext.IsBindable"/> would reject every candidate and the loop would
    /// fall through to an unusable stem.
    /// </param>
    /// <returns>A free, referenceable key – never empty.</returns>
    public static string SuggestKey(QuestionType type, DialogDetail detail, string? customTypeKey = null)
    {
        ArgumentNullException.ThrowIfNull(detail);

        var stem = type switch
        {
            QuestionType.SingleChoice => "choice",
            QuestionType.MultiChoice => "multi",
            QuestionType.Number => "number",
            QuestionType.Date => "date",
            QuestionType.Boolean => "yesno",
            QuestionType.Json when !string.IsNullOrWhiteSpace(customTypeKey)
                => customTypeKey.Trim().Replace('-', '_'),
            QuestionType.Json => "json",
            _ => "text",
        };

        // The upper bound is constructed to be reachable, not guessed: a free name lies at the latest at
        // (number of taken names + 1), because each pass checks a different candidate.
        var limit = detail.Questions.Count + detail.Loops.Count + 2;

        for (var suffix = 1; suffix <= limit; suffix++)
        {
            var candidate = suffix == 1 ? stem : $"{stem}{suffix}";

            if (DesignerExpressionContext.IsBindable(candidate) && IsFree(candidate, detail))
            {
                return candidate;
            }
        }

        return stem;
    }

    private static bool IsFree(string candidate, DialogDetail detail)
        => !detail.Questions.Any(question => string.Equals(question.Key, candidate, StringComparison.Ordinal))
            && !detail.Loops.Any(loop => string.Equals(loop.CollectionKey, candidate, StringComparison.Ordinal));

    /// <summary>
    /// Builds the JSON for <see cref="Flirty.Domain.Question.ValidationRules"/> from the input fields.
    /// </summary>
    /// <param name="json">
    /// The produced JSON or <see langword="null"/> when no rule is set (instead of an empty <c>{}</c> in
    /// the column).
    /// </param>
    /// <param name="error">The error message if the inputs are unusable.</param>
    /// <returns><see langword="true"/> when the rules are valid.</returns>
    public bool TryBuildValidationRules(out string? json, out string? error)
    {
        json = null;
        error = null;

        if (UseRawJson)
        {
            return TryValidateRawJson(out json, out error);
        }

        // Only take type-relevant rules: the engine evaluates lengths/pattern exclusively for FreeText and
        // Min/Max exclusively for Number (see AnswerValidator). After a type change, ineffective rules
        // would otherwise stay in the JSON.
        var isText = Type == QuestionType.FreeText;
        var isNumber = Type == QuestionType.Number;

        var minLength = isText ? MinLength : null;
        var maxLength = isText ? MaxLength : null;
        var pattern = isText && !string.IsNullOrWhiteSpace(Pattern) ? Pattern : null;
        var min = isNumber ? Min : null;
        var max = isNumber ? Max : null;

        if (minLength is int lower && maxLength is int upper && lower > upper)
        {
            error = $"The minimum length {lower} is greater than the maximum length {upper}.";
            return false;
        }

        if (min is decimal lowerBound && max is decimal upperBound && lowerBound > upperBound)
        {
            error = $"The minimum {lowerBound} is greater than the maximum {upperBound}.";
            return false;
        }

        if (pattern is not null && !TryCompilePattern(pattern, out error))
        {
            return false;
        }

        if (minLength is null && maxLength is null && pattern is null && min is null && max is null)
        {
            return true;
        }

        var rules = new ValidationRules
        {
            MinLength = minLength,
            MaxLength = maxLength,
            Min = min,
            Max = max,
            Pattern = pattern,
        };

        json = JsonSerializer.Serialize(rules, WriteOptions);
        return true;
    }

    /// <summary>
    /// Takes the stored rule JSON into the individual fields – or falls back to raw editing when it cannot
    /// be mapped losslessly.
    /// </summary>
    /// <param name="rules">The stored JSON or <see langword="null"/>.</param>
    private void ReadValidationRules(string? rules)
    {
        if (string.IsNullOrWhiteSpace(rules))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(rules);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || document.RootElement.EnumerateObject().Any(property => !KnownRuleProperties.Contains(property.Name)))
            {
                UseRawJson = true;
                RawJson = rules;
                return;
            }
        }
        catch (JsonException)
        {
            UseRawJson = true;
            RawJson = rules;
            return;
        }

        // From here it is certain: a valid object, exclusively known fields.
        var parsed = JsonSerializer.Deserialize<ValidationRules>(rules, ReadOptions);
        if (parsed is null)
        {
            return;
        }

        MinLength = parsed.MinLength;
        MaxLength = parsed.MaxLength;
        Min = parsed.Min;
        Max = parsed.Max;
        Pattern = parsed.Pattern;
    }

    /// <summary>
    /// Checks the raw-entered JSON for readability and returns it unchanged – foreign fields are thereby
    /// preserved.
    /// </summary>
    /// <param name="json">The taken JSON or <see langword="null"/> on empty input.</param>
    /// <param name="error">The error message on invalid JSON.</param>
    /// <returns><see langword="true"/> when the JSON is readable.</returns>
    private bool TryValidateRawJson(out string? json, out string? error)
    {
        json = null;
        error = null;

        if (string.IsNullOrWhiteSpace(RawJson))
        {
            return true;
        }

        try
        {
            _ = JsonSerializer.Deserialize<ValidationRules>(RawJson, ReadOptions);
        }
        catch (JsonException exception)
        {
            error = $"The validation rules are not valid JSON: {exception.Message}";
            return false;
        }

        json = RawJson;
        return true;
    }

    /// <summary>
    /// Compiles the pattern like the <see cref="AnswerValidator"/> – an invalid expression would otherwise
    /// only surface at runtime (there as an <see cref="InvalidOperationException"/>).
    /// </summary>
    /// <param name="pattern">The regular expression to check.</param>
    /// <param name="error">The error message on an invalid pattern.</param>
    /// <returns><see langword="true"/> when the pattern is compilable.</returns>
    private static bool TryCompilePattern(string pattern, out string? error)
    {
        error = null;
        try
        {
            _ = new Regex(pattern, RegexOptions.None, RegexTimeout);
            return true;
        }
        catch (ArgumentException exception)
        {
            error = $"The pattern '{pattern}' is not a valid regular expression: {exception.Message}";
            return false;
        }
    }
}
