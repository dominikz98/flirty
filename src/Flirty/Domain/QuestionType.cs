namespace Flirty.Domain;

/// <summary>
/// Defines what kind of answer a <see cref="Question"/> expects and how it is
/// parsed and validated.
/// </summary>
public enum QuestionType
{
    /// <summary>Exactly one answer option from a predefined list (<see cref="AnswerOption"/>).</summary>
    SingleChoice = 0,

    /// <summary>Any number of answer options from a predefined list (<see cref="AnswerOption"/>).</summary>
    MultiChoice = 1,

    /// <summary>Free text without predefined options.</summary>
    FreeText = 2,

    /// <summary>A numeric input.</summary>
    Number = 3,

    /// <summary>A date(/time) input.</summary>
    Date = 4,

    /// <summary>A yes/no input (true/false).</summary>
    Boolean = 5,

    /// <summary>
    /// An arbitrary JSON document. The engine only checks that the value is well-formed JSON, which
    /// makes this the one open-shaped built-in type: a host declares its own question types on top of
    /// it with <c>AddQuestionType</c> and names one on a question via
    /// <see cref="Question.CustomTypeKey"/>. A <c>Json</c> question without such a key is a valid
    /// question in its own right.
    /// </summary>
    Json = 6,
}
