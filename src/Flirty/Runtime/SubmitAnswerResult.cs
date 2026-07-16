namespace Flirty.Runtime;

/// <summary>
/// Ergebnis von <see cref="SubmitAnswerCommand"/> bzw. <see cref="IFlirtyEngine.SubmitAnswerAsync"/>:
/// gibt an, ob der Dialog nach dieser Antwort abgeschlossen ist, und liefert andernfalls die als
/// Nächstes zu beantwortende Frage.
/// </summary>
/// <param name="SessionId">Der Primärschlüssel der betroffenen <see cref="Flirty.Domain.DialogSession"/>.</param>
/// <param name="IsCompleted">
/// <see langword="true"/>, wenn der Dialog mit dieser Antwort abgeschlossen wurde (kein weiterer
/// Übergang); andernfalls <see langword="false"/>.
/// </param>
/// <param name="NextQuestion">
/// Die als Nächstes zu präsentierende Frage oder <see langword="null"/>, wenn der Dialog abgeschlossen
/// ist (<paramref name="IsCompleted"/> ist dann <see langword="true"/>).
/// </param>
public sealed record SubmitAnswerResult(Guid SessionId, bool IsCompleted, QuestionView? NextQuestion);
