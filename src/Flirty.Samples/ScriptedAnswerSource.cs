using Flirty.Runtime;

namespace Flirty.Samples;

/// <summary>
/// Deterministische <see cref="IAnswerSource"/> für nicht-interaktive Durchläufe (z. B. Tests): liefert
/// vordefinierte Roh-Antworten je <c>Question.Key</c> und kodiert sie – wie die interaktive Quelle –
/// abhängig vom Fragetyp als JSON.
/// </summary>
public sealed class ScriptedAnswerSource : IAnswerSource
{
    private readonly IReadOnlyDictionary<string, string> _answersByQuestionKey;

    /// <summary>
    /// Initialisiert die Quelle mit den Roh-Antworten je Frage-Schlüssel.
    /// </summary>
    /// <param name="answersByQuestionKey">
    /// Zuordnung von <c>Question.Key</c> auf die rohe (unkodierte) Antwort, z. B.
    /// <c>["role"] = "dev"</c>.
    /// </param>
    public ScriptedAnswerSource(IReadOnlyDictionary<string, string> answersByQuestionKey)
    {
        ArgumentNullException.ThrowIfNull(answersByQuestionKey);
        _answersByQuestionKey = answersByQuestionKey;
    }

    /// <inheritdoc />
    /// <exception cref="KeyNotFoundException">
    /// Für den Schlüssel der Frage wurde keine Antwort hinterlegt.
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
