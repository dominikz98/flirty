namespace Flirty.Runtime;

/// <summary>
/// Ergebnis von <see cref="EditAnswerCommand"/> bzw. <see cref="IFlirtyEngine.EditAnswerAsync"/>: gibt an,
/// ob der Dialog nach der Neuberechnung abgeschlossen ist, liefert andernfalls die nun als Nächstes zu
/// beantwortende Frage und meldet, wie viele nachgelagerte Antworten dabei verworfen (invalidiert) wurden.
/// </summary>
/// <param name="SessionId">Der Primärschlüssel der betroffenen <see cref="Flirty.Domain.DialogSession"/>.</param>
/// <param name="IsCompleted">
/// <see langword="true"/>, wenn der Dialog nach der Neuberechnung abgeschlossen ist (die editierte Frage
/// ist terminal); andernfalls <see langword="false"/>.
/// </param>
/// <param name="NextQuestion">
/// Die nach der Neuberechnung als Nächstes zu präsentierende Frage oder <see langword="null"/>, wenn der
/// Dialog abgeschlossen ist (<paramref name="IsCompleted"/> ist dann <see langword="true"/>).
/// </param>
/// <param name="InvalidatedAnswers">
/// Anzahl der wegen der Editierung verworfenen nachgelagerten Antworten (alle Antworten nach der
/// editierten Frage); <c>0</c>, wenn keine nachgelagerte Antwort existierte.
/// </param>
public sealed record EditAnswerResult(
    Guid SessionId, bool IsCompleted, QuestionView? NextQuestion, int InvalidatedAnswers);
