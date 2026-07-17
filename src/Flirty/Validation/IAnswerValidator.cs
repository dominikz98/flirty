using Flirty.Domain;

namespace Flirty.Validation;

/// <summary>
/// Validiert eine eingereichte Antwort fachlich anhand des Fragetyps
/// (<see cref="Question.Type"/>) und der optionalen Regeln (<see cref="Question.ValidationRules"/>).
/// Die Default-Implementierung ist der <see cref="AnswerValidator"/>; das
/// <c>AnswerValidationPipelineBehavior</c> ruft den Validator vor den Runtime-Handlern
/// (Submit/Edit) auf und weist ungültige Antworten mit einer <see cref="AnswerValidationException"/> ab.
/// </summary>
public interface IAnswerValidator
{
    /// <summary>
    /// Prüft, ob der rohe Antwortwert <paramref name="value"/> zum Typ und zu den Regeln der Frage
    /// <paramref name="question"/> passt.
    /// </summary>
    /// <param name="question">Die Frage samt <see cref="Question.Type"/>, Optionen und
    /// <see cref="Question.ValidationRules"/>.</param>
    /// <param name="value">Der abgegebene Antwortwert als roher JSON-Text (Format abhängig vom Fragetyp,
    /// z. B. der <see cref="Flirty.Domain.AnswerOption.Value"/> einer Auswahl als JSON-String).</param>
    /// <returns>
    /// <see cref="AnswerValidationResult.Valid"/> bei einer gültigen Antwort, andernfalls ein Ergebnis mit
    /// <see cref="AnswerValidationResult.IsValid"/> = <see langword="false"/> und den Verstößen.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="question"/> oder <paramref name="value"/> ist <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Die Frage ist fehlkonfiguriert: unbekannter <see cref="Question.Type"/>, ungültiges
    /// <see cref="Question.ValidationRules"/>-JSON oder ein ungültiges Regex-Muster.
    /// </exception>
    AnswerValidationResult Validate(Question question, string value);
}
