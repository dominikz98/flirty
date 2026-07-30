namespace Flirty.Runtime;

/// <summary>
/// Internal marker for runtime commands that submit an answer to a question of a session
/// (<see cref="SubmitAnswerCommand"/>, <see cref="EditAnswerCommand"/>). The
/// <c>AnswerValidationPipelineBehavior</c> uses it to resolve the affected question
/// before the handler and to validate the answer value against the business rules.
/// </summary>
internal interface IAnswerCommand
{
    /// <summary>The primary key of the session in which the answer is given.</summary>
    Guid SessionId { get; }

    /// <summary>The id of the answered question.</summary>
    Guid QuestionId { get; }

    /// <summary>The submitted answer value as raw JSON text.</summary>
    string Value { get; }
}
