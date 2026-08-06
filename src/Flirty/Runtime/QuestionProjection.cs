using Flirty.Domain;

namespace Flirty.Runtime;

/// <summary>
/// Shared projection of a <see cref="Question"/> from a loaded <see cref="Dialog"/> graph
/// into the lean, navigation-free <see cref="QuestionView"/>. Shared by the runtime handlers
/// (<see cref="StartDialogCommandHandler"/>, <see cref="SubmitAnswerCommandHandler"/>) so that
/// the resolution including the option order is defined in only one place.
/// </summary>
internal static class QuestionProjection
{
    /// <summary>
    /// Resolves the question with <paramref name="questionId"/> from the loaded <paramref name="dialog"/> graph
    /// and projects it along with its options (in <see cref="AnswerOption.Order"/> order) into a
    /// <see cref="QuestionView"/>.
    /// </summary>
    /// <param name="dialog">The loaded dialog graph (incl. questions and options).</param>
    /// <param name="questionId">The id of the question to resolve.</param>
    /// <returns>The projected <see cref="QuestionView"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// The question with <paramref name="questionId"/> does not belong to the <paramref name="dialog"/> graph
    /// (misconfiguration).
    /// </exception>
    public static QuestionView ResolveQuestion(Dialog dialog, Guid? questionId)
    {
        var question = dialog.Questions.FirstOrDefault(candidate => candidate.Id == questionId)
            ?? throw new InvalidOperationException(
                $"The question '{questionId}' does not belong to dialog '{dialog.Key}'.");

        var options = question.Options
            .OrderBy(option => option.Order)
            .Select(option => new AnswerOptionView(option.Id, option.Key, option.Label, option.Value))
            .ToList();

        return new QuestionView(
            question.Id, question.Key, question.Text, question.Type, question.CustomTypeKey, options);
    }
}
