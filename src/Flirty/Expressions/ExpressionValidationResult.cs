namespace Flirty.Expressions;

/// <summary>
/// Result of the validation (compile check) of a condition expression by
/// <see cref="IExpressionEvaluator.Validate(string, ExpressionContext)"/>. Unlike
/// <see cref="IExpressionEvaluator.Evaluate(string, ExpressionContext)"/>, this does <b>not</b> abort
/// with an exception, but returns a structured result instead – so the
/// designer can report an invalid expression already on save (incl. error position).
/// </summary>
public sealed class ExpressionValidationResult
{
    private ExpressionValidationResult(bool isValid, string? error, int? errorPosition)
    {
        IsValid = isValid;
        Error = error;
        ErrorPosition = errorPosition;
    }

    /// <summary>The shared result for a valid (compilable) expression.</summary>
    public static ExpressionValidationResult Valid { get; } = new(true, null, null);

    /// <summary><see langword="true"/> if the expression could be compiled successfully.</summary>
    public bool IsValid { get; }

    /// <summary>
    /// Human-readable error description when <see cref="IsValid"/> is <see langword="false"/> –
    /// otherwise <see langword="null"/>.
    /// </summary>
    public string? Error { get; }

    /// <summary>
    /// Zero-based position of the error in the expression (as far as reported by the engine), e.g. for
    /// underlining in the designer. <see langword="null"/> if no position is available or the
    /// expression is valid.
    /// </summary>
    public int? ErrorPosition { get; }

    /// <summary>
    /// Creates an error result (<see cref="IsValid"/> = <see langword="false"/>).
    /// </summary>
    /// <param name="error">The human-readable error description.</param>
    /// <param name="errorPosition">Optional zero-based error position in the expression.</param>
    /// <returns>An invalid <see cref="ExpressionValidationResult"/>.</returns>
    public static ExpressionValidationResult Invalid(string error, int? errorPosition = null)
        => new(false, error, errorPosition);
}
