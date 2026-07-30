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
}
