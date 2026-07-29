using Flirty.Runtime;

namespace Flirty.Samples;

/// <summary>
/// Deterministic <see cref="IAnswerSource"/> for non-interactive runs (e.g. tests): supplies predefined
/// raw answers per <c>Question.Key</c> and encodes them – like the interactive source – as JSON depending
/// on the question type.
/// </summary>
public sealed class ScriptedAnswerSource : IAnswerSource
{
    private readonly IReadOnlyDictionary<string, string> _answersByQuestionKey;

    /// <summary>
    /// Initializes the source with the raw answers per question key.
    /// </summary>
    /// <param name="answersByQuestionKey">
    /// Mapping from <c>Question.Key</c> to the raw (unencoded) answer, e.g.
    /// <c>["role"] = "dev"</c>.
    /// </param>
    public ScriptedAnswerSource(IReadOnlyDictionary<string, string> answersByQuestionKey)
    {
        ArgumentNullException.ThrowIfNull(answersByQuestionKey);
        _answersByQuestionKey = answersByQuestionKey;
    }

    /// <inheritdoc />
    /// <exception cref="KeyNotFoundException">
    /// No answer was registered for the question's key.
    /// </exception>
    public string GetAnswer(QuestionView question)
    {
        ArgumentNullException.ThrowIfNull(question);

        if (!_answersByQuestionKey.TryGetValue(question.Key, out var raw))
        {
            throw new KeyNotFoundException(
                $"Keine skriptgesteuerte Antwort für die Frage '{question.Key}' hinterlegt.");
        }

        return AnswerEncoder.Encode(question.Type, raw);
    }
}
