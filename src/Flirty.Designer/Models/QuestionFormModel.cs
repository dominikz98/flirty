using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Flirty.Domain;
using Flirty.Runtime.Admin;
using Flirty.Validation;

namespace Flirty.Designer.Models;

/// <summary>
/// Formular-Modell des Frage-Editors (#39). Bewusst veränderbar (settable Properties), damit die
/// Blazor-<c>EditForm</c> direkt daran binden kann; die Annotationen spiegeln die des
/// <c>CreateQuestionCommand</c>/<c>UpdateQuestionCommand</c>, damit Verstöße schon im Browser auffallen
/// und nicht erst im <c>ValidationPipelineBehavior</c> der Engine.
/// </summary>
/// <remarks>
/// <para>
/// Neben den Metadaten der Frage bildet das Modell die als JSON gespeicherten
/// <see cref="Flirty.Domain.Question.ValidationRules"/> auf einzelne Eingabefelder ab. Maßgeblich ist
/// dabei der öffentliche Core-Typ <see cref="ValidationRules"/> – das Schema wird hier <b>nicht</b>
/// dupliziert, sondern direkt als Serialisierungstyp benutzt.
/// </para>
/// <para>
/// Enthält das gespeicherte JSON Felder, die <see cref="ValidationRules"/> nicht kennt (oder ist es gar
/// kein gültiges JSON-Objekt), schaltet <see cref="From"/> auf <see cref="UseRawJson"/> um. Sonst würde
/// das Speichern die fremden Felder stillschweigend verwerfen.
/// </para>
/// </remarks>
internal sealed class QuestionFormModel
{
    /// <summary>
    /// Zeitlimit für die Muster-Prüfung. Identisch zu <c>AnswerValidator.RegexTimeout</c>, damit im
    /// Designer nichts als gültig durchgeht, was die Engine zur Laufzeit anders bewertet.
    /// </summary>
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

    /// <summary>Die von <see cref="ValidationRules"/> unterstützten JSON-Felder (case-insensitiv wie im <see cref="AnswerValidator"/>).</summary>
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

    /// <summary>Der fachliche, stabile Schlüssel der Frage (muss im Dialog eindeutig sein).</summary>
    [Required(ErrorMessage = "Bitte einen Schlüssel angeben.")]
    public string Key { get; set; } = string.Empty;

    /// <summary>Der angezeigte Fragetext.</summary>
    [Required(ErrorMessage = "Bitte einen Fragetext angeben.")]
    public string Text { get; set; } = string.Empty;

    /// <summary>Der Antworttyp der Frage.</summary>
    public QuestionType Type { get; set; } = QuestionType.FreeText;

    /// <summary>Gibt an, ob eine Antwort auf die Frage erforderlich ist.</summary>
    public bool IsRequired { get; set; }

    /// <summary>Mindestlänge des Textes (nur <see cref="QuestionType.FreeText"/>).</summary>
    public int? MinLength { get; set; }

    /// <summary>Maximallänge des Textes (nur <see cref="QuestionType.FreeText"/>).</summary>
    public int? MaxLength { get; set; }

    /// <summary>Kleinster zulässiger Wert (nur <see cref="QuestionType.Number"/>).</summary>
    public decimal? Min { get; set; }

    /// <summary>Größter zulässiger Wert (nur <see cref="QuestionType.Number"/>).</summary>
    public decimal? Max { get; set; }

    /// <summary>Regulärer Ausdruck, dem der Text entsprechen muss (nur <see cref="QuestionType.FreeText"/>).</summary>
    public string? Pattern { get; set; }

    /// <summary>
    /// Gibt an, ob die Regeln als Roh-JSON bearbeitet werden. Wird von <see cref="From"/> gesetzt, wenn
    /// das gespeicherte JSON nicht verlustfrei auf die Einzelfelder abbildbar ist.
    /// </summary>
    public bool UseRawJson { get; set; }

    /// <summary>Das roh bearbeitete Regel-JSON; nur relevant, wenn <see cref="UseRawJson"/> gesetzt ist.</summary>
    public string? RawJson { get; set; }

    /// <summary>Erzeugt ein Formular-Modell aus einer bestehenden Frage.</summary>
    /// <param name="question">Die Frage-Sicht aus dem Admin-CRUD.</param>
    /// <returns>Das befüllte Formular-Modell.</returns>
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
    /// Baut aus den Eingabefeldern das JSON für <see cref="Flirty.Domain.Question.ValidationRules"/>.
    /// </summary>
    /// <param name="json">
    /// Das erzeugte JSON oder <see langword="null"/>, wenn keine Regel gesetzt ist (statt eines leeren
    /// <c>{}</c> in der Spalte).
    /// </param>
    /// <param name="error">Die deutsche Fehlermeldung, falls die Eingaben unbrauchbar sind.</param>
    /// <returns><see langword="true"/>, wenn die Regeln gültig sind.</returns>
    public bool TryBuildValidationRules(out string? json, out string? error)
    {
        json = null;
        error = null;

        if (UseRawJson)
        {
            return TryValidateRawJson(out json, out error);
        }

        // Nur typ-relevante Regeln übernehmen: die Engine wertet Längen/Muster ausschließlich bei
        // FreeText und Min/Max ausschließlich bei Number aus (siehe AnswerValidator). Nach einem
        // Typwechsel blieben sonst wirkungslose Regeln im JSON stehen.
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
    /// Übernimmt das gespeicherte Regel-JSON in die Einzelfelder – oder fällt auf die Roh-Bearbeitung
    /// zurück, wenn es nicht verlustfrei abbildbar ist.
    /// </summary>
    /// <param name="rules">Das gespeicherte JSON oder <see langword="null"/>.</param>
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

        // Ab hier steht fest: gültiges Objekt, ausschließlich bekannte Felder.
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
    /// Prüft das roh eingegebene JSON auf Lesbarkeit und gibt es unverändert zurück – fremde Felder
    /// bleiben so erhalten.
    /// </summary>
    /// <param name="json">Das übernommene JSON oder <see langword="null"/> bei leerer Eingabe.</param>
    /// <param name="error">Die Fehlermeldung bei ungültigem JSON.</param>
    /// <returns><see langword="true"/>, wenn das JSON lesbar ist.</returns>
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
    /// Kompiliert das Muster wie der <see cref="AnswerValidator"/> – ein ungültiger Ausdruck würde sonst
    /// erst zur Laufzeit auffallen (dort als <see cref="InvalidOperationException"/>).
    /// </summary>
    /// <param name="pattern">Der zu prüfende reguläre Ausdruck.</param>
    /// <param name="error">Die Fehlermeldung bei ungültigem Muster.</param>
    /// <returns><see langword="true"/>, wenn das Muster übersetzbar ist.</returns>
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
