using Flirty.Domain;

namespace Flirty.Validation;

/// <summary>
/// Validates a submitted answer against the domain rules based on the question type
/// (<see cref="Question.Type"/>) and the optional rules (<see cref="Question.ValidationRules"/>).
/// The default implementation is the <see cref="AnswerValidator"/>; the
/// <c>AnswerValidationPipelineBehavior</c> calls the validator before the runtime handlers
/// (submit/edit) and rejects invalid answers with an <see cref="AnswerValidationException"/>.
/// </summary>
public interface IAnswerValidator
{
    /// <summary>
    /// Checks whether the raw answer value <paramref name="value"/> fits the type and rules of the
    /// question <paramref name="question"/>.
    /// </summary>
    /// <param name="question">The question including <see cref="Question.Type"/>, options and
    /// <see cref="Question.ValidationRules"/>.</param>
    /// <param name="value">The submitted answer value as raw JSON text (format depends on the question type,
    /// e.g. the <see cref="Flirty.Domain.AnswerOption.Value"/> of a selection as a JSON string).</param>
    /// <returns>
    /// <see cref="AnswerValidationResult.Valid"/> for a valid answer, otherwise a result with
    /// <see cref="AnswerValidationResult.IsValid"/> = <see langword="false"/> and the violations.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="question"/> or <paramref name="value"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The question is misconfigured: unknown <see cref="Question.Type"/>, invalid
    /// <see cref="Question.ValidationRules"/> JSON or an invalid regex pattern.
    /// </exception>
    AnswerValidationResult Validate(Question question, string value);
}
