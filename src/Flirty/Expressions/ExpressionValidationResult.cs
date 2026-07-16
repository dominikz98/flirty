namespace Flirty.Expressions;

/// <summary>
/// Ergebnis der Validierung (Compile-Check) eines Bedingungsausdrucks durch
/// <see cref="IExpressionEvaluator.Validate(string, ExpressionContext)"/>. Anders als
/// <see cref="IExpressionEvaluator.Evaluate(string, ExpressionContext)"/> wird hier <b>nicht</b> mit
/// einer Exception abgebrochen, sondern ein strukturiertes Ergebnis zurückgegeben – so kann der
/// Designer einen ungültigen Ausdruck bereits beim Speichern melden (inkl. Fehlerposition).
/// </summary>
public sealed class ExpressionValidationResult
{
    private ExpressionValidationResult(bool isValid, string? error, int? errorPosition)
    {
        IsValid = isValid;
        Error = error;
        ErrorPosition = errorPosition;
    }

    /// <summary>Das gemeinsame Ergebnis für einen gültigen (kompilierbaren) Ausdruck.</summary>
    public static ExpressionValidationResult Valid { get; } = new(true, null, null);

    /// <summary><see langword="true"/>, wenn der Ausdruck erfolgreich kompiliert werden konnte.</summary>
    public bool IsValid { get; }

    /// <summary>
    /// Menschlesbare Fehlerbeschreibung, wenn <see cref="IsValid"/> <see langword="false"/> ist –
    /// andernfalls <see langword="null"/>.
    /// </summary>
    public string? Error { get; }

    /// <summary>
    /// Nullbasierte Position des Fehlers im Ausdruck (soweit von der Engine gemeldet), z. B. zum
    /// Unterstreichen im Designer. <see langword="null"/>, wenn keine Position verfügbar oder der
    /// Ausdruck gültig ist.
    /// </summary>
    public int? ErrorPosition { get; }

    /// <summary>
    /// Erstellt ein Fehlerergebnis (<see cref="IsValid"/> = <see langword="false"/>).
    /// </summary>
    /// <param name="error">Die menschlesbare Fehlerbeschreibung.</param>
    /// <param name="errorPosition">Optionale nullbasierte Fehlerposition im Ausdruck.</param>
    /// <returns>Ein ungültiges <see cref="ExpressionValidationResult"/>.</returns>
    public static ExpressionValidationResult Invalid(string error, int? errorPosition = null)
        => new(false, error, errorPosition);
}
