using Flirty.Domain;

namespace Flirty.Validation;

/// <summary>
/// Validates the answer to a question of a <b>host-declared custom question type</b>. A host declares
/// such a type with <c>AddQuestionType</c> and implements this interface to give it real semantics –
/// "is this a well-formed colour?", "does this SKU exist?" – on top of the well-formedness check that
/// <see cref="QuestionType.Json"/> already performs.
/// </summary>
/// <remarks>
/// <para>
/// An implementation is resolved <b>from the request scope</b>, so it may take scoped dependencies –
/// an <c>HttpClient</c>, options, or the same <c>FlirtyDbContext</c> the handler uses. That is the
/// difference to <see cref="IAnswerValidator"/>, which is a stateless singleton.
/// </para>
/// <para>
/// It is called only after the built-in checks passed, so the value is guaranteed to be well-formed
/// JSON by the time it arrives. Return a failing <see cref="AnswerValidationResult"/> for a value
/// error; throw only for a genuine misconfiguration of the question, exactly as
/// <see cref="IAnswerValidator"/> documents.
/// </para>
/// </remarks>
public interface IQuestionTypeValidator
{
    /// <summary>
    /// Checks whether the raw answer value <paramref name="value"/> is valid for the custom type of
    /// the question <paramref name="question"/>.
    /// </summary>
    /// <param name="question">The question, including its options and
    /// <see cref="Question.ValidationRules"/> – a custom type may read both, and it is the single
    /// owner of whatever extra rules it puts into that free-form JSON.</param>
    /// <param name="value">The submitted answer value as raw JSON text. Already checked to be
    /// well-formed JSON.</param>
    /// <returns>
    /// <see cref="AnswerValidationResult.Valid"/> for a valid answer, otherwise a result carrying the
    /// violations.
    /// </returns>
    AnswerValidationResult Validate(Question question, string value);
}
