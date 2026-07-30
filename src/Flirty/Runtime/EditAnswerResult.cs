namespace Flirty.Runtime;

/// <summary>
/// Result of <see cref="EditAnswerCommand"/> or <see cref="IFlirtyEngine.EditAnswerAsync"/>: indicates
/// whether the dialog is completed after the recomputation, otherwise delivers the question now to be
/// answered next and reports how many downstream answers were discarded (invalidated) in the process.
/// </summary>
/// <param name="SessionId">The primary key of the affected <see cref="Flirty.Domain.DialogSession"/>.</param>
/// <param name="IsCompleted">
/// <see langword="true"/> if the dialog is completed after the recomputation (the edited question
/// is terminal); otherwise <see langword="false"/>.
/// </param>
/// <param name="NextQuestion">
/// The question to be presented next after the recomputation, or <see langword="null"/> if the
/// dialog is completed (<paramref name="IsCompleted"/> is then <see langword="true"/>).
/// </param>
/// <param name="InvalidatedAnswers">
/// Number of downstream answers discarded because of the edit (all answers after the
/// edited question); <c>0</c> if no downstream answer existed.
/// </param>
public sealed record EditAnswerResult(
    Guid SessionId, bool IsCompleted, QuestionView? NextQuestion, int InvalidatedAnswers);
