namespace Flirty.Validation;

/// <summary>
/// Deserialisiertes Modell der optionalen, je Frage konfigurierten Validierungsregeln
/// (<see cref="Flirty.Domain.Question.ValidationRules"/>, als JSON gespeichert). Alle Felder sind
/// optional; ein nicht gesetztes Feld bedeutet „keine Einschränkung". Die Regeln sind
/// <b>typ-skopiert</b>: Längen- und Muster-Regeln greifen bei <see cref="Flirty.Domain.QuestionType.FreeText"/>,
/// die numerischen Grenzen bei <see cref="Flirty.Domain.QuestionType.Number"/>; auf andere Fragetypen
/// nicht anwendbare Regeln werden ignoriert.
/// </summary>
/// <remarks>
/// Das JSON verwendet camelCase-Feldnamen (z. B. <c>{ "maxLength": 50 }</c>). Die Deserialisierung
/// erfolgt case-insensitiv durch den <see cref="AnswerValidator"/>.
/// </remarks>
public sealed record ValidationRules
{
    /// <summary>Mindestlänge des Textes (Zeichen) für <see cref="Flirty.Domain.QuestionType.FreeText"/>.</summary>
    public int? MinLength { get; init; }

    /// <summary>Maximallänge des Textes (Zeichen) für <see cref="Flirty.Domain.QuestionType.FreeText"/>.</summary>
    public int? MaxLength { get; init; }

    /// <summary>Kleinster zulässiger Wert für <see cref="Flirty.Domain.QuestionType.Number"/> (inklusiv).</summary>
    public decimal? Min { get; init; }

    /// <summary>Größter zulässiger Wert für <see cref="Flirty.Domain.QuestionType.Number"/> (inklusiv).</summary>
    public decimal? Max { get; init; }

    /// <summary>
    /// Regulärer Ausdruck, dem der Text bei <see cref="Flirty.Domain.QuestionType.FreeText"/> entsprechen
    /// muss (Teiltreffer via <c>Regex.IsMatch</c>; für Vollprüfung im Muster verankern, z. B. <c>^…$</c>).
    /// </summary>
    public string? Pattern { get; init; }
}
