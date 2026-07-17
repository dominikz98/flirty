namespace Flirty.Runtime;

/// <summary>
/// Interner Marker für Runtime-Commands, die eine Antwort auf eine Frage einer Session einreichen
/// (<see cref="SubmitAnswerCommand"/>, <see cref="EditAnswerCommand"/>). Das
/// <c>AnswerValidationPipelineBehavior</c> nutzt ihn, um vor dem Handler die betroffene Frage
/// aufzulösen und den Antwortwert fachlich zu validieren.
/// </summary>
internal interface IAnswerCommand
{
    /// <summary>Der Primärschlüssel der Session, in der geantwortet wird.</summary>
    Guid SessionId { get; }

    /// <summary>Die Id der beantworteten Frage.</summary>
    Guid QuestionId { get; }

    /// <summary>Der abgegebene Antwortwert als roher JSON-Text.</summary>
    string Value { get; }
}
