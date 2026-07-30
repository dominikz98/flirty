using Flirty.Runtime;

namespace Flirty.Samples;

/// <summary>
/// Supplies the answer to a posed question. The abstraction decouples the
/// <see cref="ConsoleDialogRunner"/> from the concrete input source, so that the app reads
/// interactively from the console while a test feeds a fixed script instead.
/// </summary>
public interface IAnswerSource
{
    /// <summary>
    /// Determines the answer to the given question.
    /// </summary>
    /// <param name="question">The question currently to be answered.</param>
    /// <returns>
    /// The answer value as raw JSON text in the format expected by the question type (e.g. <c>"dev"</c>
    /// for a choice/free-text answer).
    /// </returns>
    string GetAnswer(QuestionView question);
}
