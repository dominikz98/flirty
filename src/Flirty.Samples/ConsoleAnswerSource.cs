using Flirty.Runtime;

namespace Flirty.Samples;

/// <summary>
/// Liest die Antwort auf eine Frage interaktiv von der Konsole (<see cref="Console.ReadLine"/>) und
/// kodiert die Eingabe abhängig vom Fragetyp als JSON.
/// </summary>
public sealed class ConsoleAnswerSource : IAnswerSource
{
    /// <inheritdoc />
    /// <remarks>
    /// Bei Auswahlfragen ist der Options-Schlüssel (bzw. -Wert) einzugeben; bei einer leeren Eingabe
    /// wird der Anwender erneut gefragt, sofern die Frage Optionen hat.
    /// </remarks>
    public string GetAnswer(QuestionView question)
    {
        ArgumentNullException.ThrowIfNull(question);

        var raw = Console.ReadLine() ?? string.Empty;
        return AnswerEncoder.Encode(question.Type, raw);
    }
}
