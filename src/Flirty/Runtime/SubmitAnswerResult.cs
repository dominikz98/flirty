namespace Flirty.Runtime;

/// <summary>
/// Result of <see cref="SubmitAnswerCommand"/> or <see cref="IFlirtyEngine.SubmitAnswerAsync"/>:
/// indicates whether the dialog is completed after this answer, and otherwise delivers the
/// question to be answered next.
/// </summary>
/// <param name="SessionId">The primary key of the affected <see cref="Flirty.Domain.DialogSession"/>.</param>
/// <param name="IsCompleted">
/// <see langword="true"/> if the dialog was completed with this answer (no further
/// transition); otherwise <see langword="false"/>.
/// </param>
/// <param name="NextQuestion">
/// The question to be presented next, or <see langword="null"/> if the dialog is completed
/// (<paramref name="IsCompleted"/> is then <see langword="true"/>).
/// </param>
public sealed record SubmitAnswerResult(Guid SessionId, bool IsCompleted, QuestionView? NextQuestion);
