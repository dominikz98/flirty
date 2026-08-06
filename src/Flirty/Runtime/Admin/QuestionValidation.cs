using System.ComponentModel.DataAnnotations;
using Flirty.Domain;

namespace Flirty.Runtime.Admin;

/// <summary>
/// Shared cross-field checks of <see cref="CreateQuestionCommand"/> and
/// <see cref="UpdateQuestionCommand"/>. Both commands invoke them via <see cref="IValidatableObject"/>;
/// the <c>ValidationPipelineBehavior</c> thereby runs them before the handler and reports violations
/// as a <see cref="ValidationException"/> (in the WebAPI: HTTP 400).
/// </summary>
/// <remarks>
/// Deliberately here and not in the handler: the rules describe the <b>request</b>, not the state of
/// the database. And deliberately only this one rule – whether the named custom type is actually
/// declared is <b>not</b> checked, because an undeclared key is not an error: the answer is then
/// validated as plain JSON. Checking it here would also be a lie, since the registry belongs to the
/// host process and a second consumer of the same database may well have declared it.
/// </remarks>
internal static class QuestionValidation
{
    /// <summary>
    /// Checks that a custom question type key is only used where it means something.
    /// </summary>
    /// <param name="type">The answer type of the question.</param>
    /// <param name="customTypeKey">The custom question type key, if any.</param>
    /// <returns>The violations found (empty if everything is consistent).</returns>
    public static IEnumerable<ValidationResult> Validate(QuestionType type, string? customTypeKey)
    {
        if (!string.IsNullOrWhiteSpace(customTypeKey) && type != QuestionType.Json)
        {
            yield return new ValidationResult(
                $"A custom question type key is only allowed on a question of type "
                + $"'{QuestionType.Json}' – the type '{type}' carries no custom key.",
                [nameof(Question.CustomTypeKey)]);
        }
    }
}
