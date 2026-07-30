using Flirty.Domain;
using Mediator;

namespace Flirty.Runtime;

/// <summary>
/// In-process notification (trigger scope <see cref="TriggerScope.AfterQuestion"/>): published
/// after the <see cref="SubmitAnswerCommandHandler"/> has persisted the answer and evaluated the outgoing
/// transitions (branching) of the question – i.e. once it is settled whether the session advances or
/// completes.
/// </summary>
/// <remarks>
/// Complements the <see cref="AnswerSubmittedNotification"/> with the result of the transition evaluation:
/// which question is open next, or whether the dialog was completed. Downstream triggers that react only to
/// a specific question filter via <see cref="QuestionId"/>.
/// </remarks>
/// <param name="SessionId">The primary key of the <see cref="DialogSession"/>.</param>
/// <param name="DialogKey">The business, stable key of the running dialog.</param>
/// <param name="QuestionId">The id of the just-answered question.</param>
/// <param name="NextQuestionId">
/// The id of the next open question, or <see langword="null"/> if no transition applies anymore and the
/// dialog was completed.
/// </param>
/// <param name="IsCompleted">
/// <see langword="true"/> if the answer completed the dialog; otherwise <see langword="false"/>.
/// </param>
// MSG0005: The Mediator source generator (martinothamar) requires a handler per message in the
// core compilation. In-process triggers are deliberately handled only by host apps via their own
// INotificationHandler<T> (see docs/TRIGGERS.md) – therefore suppressed here on purpose.
#pragma warning disable MSG0005
public sealed record QuestionAnsweredNotification(
    Guid SessionId,
    string DialogKey,
    Guid QuestionId,
    Guid? NextQuestionId,
    bool IsCompleted) : INotification;
#pragma warning restore MSG0005
