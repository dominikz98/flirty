namespace Flirty.Validation;

/// <summary>
/// Result of the domain answer validation by
/// <see cref="IAnswerValidator.Validate(Flirty.Domain.Question, string)"/>. Analogous to
/// <c>ExpressionValidationResult</c>, this does <b>not</b> abort with an exception, but returns
/// a structured result instead – the <c>AnswerValidationPipelineBehavior</c> then translates
/// an invalid result into an <see cref="AnswerValidationException"/>.
/// </summary>
public sealed class AnswerValidationResult
{
    private AnswerValidationResult(bool isValid, IReadOnlyList<string> errors)
    {
        IsValid = isValid;
        Errors = errors;
    }

    /// <summary>The shared result for a valid answer (without errors).</summary>
    public static AnswerValidationResult Valid { get; } = new(true, []);

    /// <summary><see langword="true"/> if the answer passed all type and rule checks.</summary>
    public bool IsValid { get; }

    /// <summary>
    /// The human-readable error descriptions when <see cref="IsValid"/> is <see langword="false"/> –
    /// otherwise empty.
    /// </summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>
    /// Creates an error result (<see cref="IsValid"/> = <see langword="false"/>) with at least
    /// one error description.
    /// </summary>
    /// <param name="errors">The human-readable error descriptions (at least one).</param>
    /// <returns>An invalid <see cref="AnswerValidationResult"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="errors"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="errors"/> is empty.</exception>
    public static AnswerValidationResult Invalid(params string[] errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (errors.Length == 0)
        {
            throw new ArgumentException("At least one error description is required.", nameof(errors));
        }

        return new AnswerValidationResult(false, [.. errors]);
    }
}
