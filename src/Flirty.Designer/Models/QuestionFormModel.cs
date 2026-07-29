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
/// Form model of the question editor (#39). Deliberately mutable (settable properties), so that the
/// Blazor <c>EditForm</c> can bind directly to it; the annotations mirror those of the
/// <c>CreateQuestionCommand</c>/<c>UpdateQuestionCommand</c>, so that violations already show up in the browser
/// and not only in the <c>ValidationPipelineBehavior</c> of the engine.
/// </summary>
/// <remarks>
/// <para>
/// Besides the metadata of the question, the model maps the
/// <see cref="Flirty.Domain.Question.ValidationRules"/> stored as JSON onto individual input fields. The
/// authoritative type here is the public core type <see cref="ValidationRules"/> – the schema is <b>not</b>
/// duplicated here, but used directly as the serialization type.
/// </para>
/// <para>
/// If the stored JSON contains fields that <see cref="ValidationRules"/> does not know (or if it is not
/// even a valid JSON object), <see cref="From"/> switches to <see cref="UseRawJson"/>. Otherwise
/// saving would silently discard the foreign fields.
/// </para>
/// </remarks>
internal sealed class QuestionFormModel
{
    /// <summary>
    /// Time limit for the pattern check. Identical to <c>AnswerValidator.RegexTimeout</c>, so that in the
    /// designer nothing passes as valid that the engine evaluates differently at runtime.
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

    /// <summary>The domain, stable key of the question (must be unique within the dialog).</summary>
    [Required(ErrorMessage = "Bitte einen Schlüssel angeben.")]
    public string Key { get; set; } = string.Empty;

    /// <summary>The displayed question text.</summary>
    [Required(ErrorMessage = "Bitte einen Fragetext angeben.")]
    public string Text { get; set; } = string.Empty;

    /// <summary>The answer type of the question.</summary>
    public QuestionType Type { get; set; } = QuestionType.FreeText;

    /// <summary>Indicates whether an answer to the question is required.</summary>
    public bool IsRequired { get; set; }

    /// <summary>Minimum length of the text (only <see cref="QuestionType.FreeText"/>).</summary>
    public int? MinLength { get; set; }

    /// <summary>Maximum length of the text (only <see cref="QuestionType.FreeText"/>).</summary>
    public int? MaxLength { get; set; }

    /// <summary>Smallest permitted value (only <see cref="QuestionType.Number"/>).</summary>
    public decimal? Min { get; set; }

    /// <summary>Largest permitted value (only <see cref="QuestionType.Number"/>).</summary>
    public decimal? Max { get; set; }

    /// <summary>Regular expression the text must match (only <see cref="QuestionType.FreeText"/>).</summary>
    public string? Pattern { get; set; }

    /// <summary>
    /// Indicates whether the rules are edited as raw JSON. Is set by <see cref="From"/> when
    /// the stored JSON cannot be mapped losslessly onto the individual fields.
    /// </summary>
    public bool UseRawJson { get; set; }

    /// <summary>The raw-edited rule JSON; only relevant if <see cref="UseRawJson"/> is set.</summary>
    public string? RawJson { get; set; }

    /// <summary>Creates a form model from an existing question.</summary>
    /// <param name="question">The question view from the admin CRUD.</param>
    /// <returns>The filled form model.</returns>
    public static QuestionFormModel From(QuestionDetail question)
    {
        ArgumentNullException.ThrowIfNull(question);

        var model = new QuestionFormModel
        {
            Key = question.Key,
            Text = question.Text,
            Type = question.Type,
            IsRequired = question.IsRequired,
        };

        model.ReadValidationRules(question.ValidationRules);
        return model;
    }

    /// <summary>
    /// Suggests a free key for a question created <b>by gesture</b>: a stem after the
    /// question type (<c>text</c>, <c>zahl</c>, <c>datum</c>, <c>auswahl</c>, <c>mehrfach</c>, <c>janein</c>),
    /// with an appended number when taken (<c>text2</c>, <c>text3</c> …).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately different from <see cref="LoopFormModel.SuggestCollectionKey"/>, which returns
    /// <b>empty</b> on a collision: there the suggestion fills a required field that the author
    /// must consciously choose anyway. Here it carries a gesture that writes immediately – an empty key would make it
    /// fail at the <c>CreateQuestionCommand</c>. The key is a placeholder that the inspector
    /// makes renamable.
    /// </para>
    /// <para>
    /// It is checked against question <b>and</b> collection keys: a question key that duplicates a
    /// <c>CollectionKey</c> is shadowed by it in the expression context – the gesture would
    /// otherwise produce a warning on the spot. <see cref="DesignerExpressionContext.IsBindable"/> at the
    /// same time keeps the reserved names free (<c>now</c>, <c>iterationIndex</c>, <c>session</c>).
    /// </para>
    /// </remarks>
    /// <param name="type">The question type that determines the stem.</param>
    /// <param name="detail">The dialog together with the graph against which collisions are checked.</param>
    /// <returns>A free, referenceable key – never empty.</returns>
    public static string SuggestKey(QuestionType type, DialogDetail detail)
    {
        ArgumentNullException.ThrowIfNull(detail);

        var stem = type switch
        {
            QuestionType.SingleChoice => "auswahl",
            QuestionType.MultiChoice => "mehrfach",
            QuestionType.Number => "zahl",
            QuestionType.Date => "datum",
            QuestionType.Boolean => "janein",
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
    /// Builds from the input fields the JSON for <see cref="Flirty.Domain.Question.ValidationRules"/>.
    /// </summary>
    /// <param name="json">
    /// The produced JSON or <see langword="null"/> if no rule is set (instead of an empty
    /// <c>{}</c> in the column).
    /// </param>
    /// <param name="error">The German error message if the inputs are unusable.</param>
    /// <returns><see langword="true"/> if the rules are valid.</returns>
    public bool TryBuildValidationRules(out string? json, out string? error)
    {
        json = null;
        error = null;

        if (UseRawJson)
        {
            return TryValidateRawJson(out json, out error);
        }

        // Take over only type-relevant rules: the engine evaluates lengths/patterns exclusively for
        // FreeText and Min/Max exclusively for Number (see AnswerValidator). After a
        // type switch, ineffective rules would otherwise remain in the JSON.
        var isText = Type == QuestionType.FreeText;
        var isNumber = Type == QuestionType.Number;

        var minLength = isText ? MinLength : null;
        var maxLength = isText ? MaxLength : null;
        var pattern = isText && !string.IsNullOrWhiteSpace(Pattern) ? Pattern : null;
        var min = isNumber ? Min : null;
        var max = isNumber ? Max : null;

        if (minLength is int von && maxLength is int bis && von > bis)
        {
            error = $"Die Mindestlänge {von} ist größer als die Maximallänge {bis}.";
            return false;
        }

        if (min is decimal untergrenze && max is decimal obergrenze && untergrenze > obergrenze)
        {
            error = $"Das Minimum {untergrenze} ist größer als das Maximum {obergrenze}.";
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
    /// Takes over the stored rule JSON into the individual fields – or falls back to raw editing
    /// if it cannot be mapped losslessly.
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

        // From here on it is certain: valid object, exclusively known fields.
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
    /// Checks the raw-entered JSON for readability and returns it unchanged – foreign fields
    /// are thereby preserved.
    /// </summary>
    /// <param name="json">The taken-over JSON or <see langword="null"/> on empty input.</param>
    /// <param name="error">The error message on invalid JSON.</param>
    /// <returns><see langword="true"/> if the JSON is readable.</returns>
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
            error = $"Die Validierungsregeln sind kein gültiges JSON: {exception.Message}";
            return false;
        }

        json = RawJson;
        return true;
    }

    /// <summary>
    /// Compiles the pattern like the <see cref="AnswerValidator"/> – an invalid expression would otherwise
    /// only show up at runtime (there as an <see cref="InvalidOperationException"/>).
    /// </summary>
    /// <param name="pattern">The regular expression to check.</param>
    /// <param name="error">The error message on an invalid pattern.</param>
    /// <returns><see langword="true"/> if the pattern is translatable.</returns>
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
            error = $"Das Muster '{pattern}' ist kein gültiger regulärer Ausdruck: {exception.Message}";
            return false;
        }
    }
}
