using Flirty.Runtime;

namespace Flirty.Samples;

/// <summary>
/// Reads the answer to a question interactively from the console (<see cref="Console.ReadLine"/>) and
/// encodes the input as JSON depending on the question type.
/// </summary>
public sealed class ConsoleAnswerSource : IAnswerSource
{
    /// <inheritdoc />
    /// <remarks>
    /// For choice questions the option key (or value) is to be entered; on an empty input the user is
    /// asked again, provided the question has options.
    /// </remarks>
    public string GetAnswer(QuestionView question)
    {
        ArgumentNullException.ThrowIfNull(question);

        var raw = Console.ReadLine() ?? string.Empty;
        return AnswerEncoder.Encode(question.Type, raw);
    }
}
