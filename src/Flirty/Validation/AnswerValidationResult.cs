namespace Flirty.Validation;

/// <summary>
/// Ergebnis der fachlichen Antwort-Validierung durch
/// <see cref="IAnswerValidator.Validate(Flirty.Domain.Question, string)"/>. Analog zu
/// <c>ExpressionValidationResult</c> wird hier <b>nicht</b> mit einer Exception abgebrochen, sondern
/// ein strukturiertes Ergebnis zurückgegeben – das <c>AnswerValidationPipelineBehavior</c> übersetzt
/// ein ungültiges Ergebnis anschließend in eine <see cref="AnswerValidationException"/>.
/// </summary>
public sealed class AnswerValidationResult
{
    private AnswerValidationResult(bool isValid, IReadOnlyList<string> errors)
    {
        IsValid = isValid;
        Errors = errors;
    }

    /// <summary>Das gemeinsame Ergebnis für eine gültige Antwort (ohne Fehler).</summary>
    public static AnswerValidationResult Valid { get; } = new(true, []);

    /// <summary><see langword="true"/>, wenn die Antwort alle Typ- und Regelprüfungen bestanden hat.</summary>
    public bool IsValid { get; }

    /// <summary>
    /// Die menschlesbaren Fehlerbeschreibungen, wenn <see cref="IsValid"/> <see langword="false"/> ist –
    /// andernfalls leer.
    /// </summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>
    /// Erstellt ein Fehlerergebnis (<see cref="IsValid"/> = <see langword="false"/>) mit mindestens
    /// einer Fehlerbeschreibung.
    /// </summary>
    /// <param name="errors">Die menschlesbaren Fehlerbeschreibungen (mindestens eine).</param>
    /// <returns>Ein ungültiges <see cref="AnswerValidationResult"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="errors"/> ist <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="errors"/> ist leer.</exception>
    public static AnswerValidationResult Invalid(params string[] errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (errors.Length == 0)
        {
            throw new ArgumentException("Mindestens eine Fehlerbeschreibung ist erforderlich.", nameof(errors));
        }

        return new AnswerValidationResult(false, [.. errors]);
    }
}
