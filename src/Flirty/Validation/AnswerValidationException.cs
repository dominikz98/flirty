using System.ComponentModel.DataAnnotations;

namespace Flirty.Validation;

/// <summary>
/// Thrown when a submitted answer fails the domain validation
/// (<see cref="IAnswerValidator"/>) – for example a type-mismatched value, an unknown selection
/// or a rule violation (length/range/pattern). Derives from <see cref="ValidationException"/>,
/// so that host apps can handle it together with the pipeline validation errors (DataAnnotations) via
/// <c>catch (ValidationException)</c>, and additionally carries the
/// <see cref="QuestionId"/> and the individual <see cref="Errors"/>.
/// </summary>
public sealed class AnswerValidationException : ValidationException
{
    /// <summary>Creates the exception without further details.</summary>
    public AnswerValidationException()
    {
    }

    /// <summary>Creates the exception with the given message.</summary>
    /// <param name="message">The error message describing the cause.</param>
    public AnswerValidationException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and a triggering exception.</summary>
    /// <param name="message">The error message describing the cause.</param>
    /// <param name="innerException">The exception that triggered this exception.</param>
    public AnswerValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// The id of the question whose answer was invalid, or <see langword="null"/> if it is not
    /// known.
    /// </summary>
    public Guid? QuestionId { get; init; }

    /// <summary>The individual violations (human-readable) that led to the rejection.</summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>
    /// Creates an <see cref="AnswerValidationException"/> for the given
    /// <paramref name="questionId"/> with the individual <paramref name="errors"/> and a message
    /// composed from them.
    /// </summary>
    /// <param name="questionId">The id of the question whose answer was rejected.</param>
    /// <param name="errors">The individual violations.</param>
    /// <returns>The prepared exception with <see cref="QuestionId"/> and <see cref="Errors"/> set.</returns>
    public static AnswerValidationException For(Guid questionId, IReadOnlyList<string> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        return new AnswerValidationException(
            $"The answer to the question '{questionId}' is invalid: {string.Join("; ", errors)}")
        {
            QuestionId = questionId,
            Errors = [.. errors],
        };
    }
}
