using Flirty.Domain;
using Mediator;

namespace Flirty.Runtime;

/// <summary>
/// In-process notification (trigger scope <see cref="TriggerScope.AfterAnswer"/>): published
/// after the <see cref="SubmitAnswerCommandHandler"/> has persisted a submitted answer –
/// regardless of whether the session then advances or completes.
/// </summary>
/// <remarks>
/// Fires once per submitted answer. Subsequent corrections via the
/// <see cref="EditAnswerCommandHandler"/> deliberately do <b>not</b> trigger this notification.
/// </remarks>
/// <param name="SessionId">The primary key of the <see cref="DialogSession"/> for which the answer was given.</param>
/// <param name="DialogKey">The business, stable key of the running dialog.</param>
/// <param name="QuestionId">The id of the answered question.</param>
/// <param name="Value">The submitted answer value as raw JSON text (format depends on the question type).</param>
/// <param name="LoopInstanceId">
/// The instance id of the loop the answer belongs to, or <see langword="null"/> if the answer
/// was given outside a loop.
/// </param>
/// <param name="IterationIndex">
/// The zero-based iteration index within the loop, or <see langword="null"/> outside a
/// loop.
/// </param>
// MSG0005: The Mediator source generator (martinothamar) requires a handler per message in the
// core compilation. In-process triggers are deliberately handled only by host apps via their own
// INotificationHandler<T> (see docs/TRIGGERS.md) – therefore suppressed here on purpose.
#pragma warning disable MSG0005
public sealed record AnswerSubmittedNotification(
    Guid SessionId,
    string DialogKey,
    Guid QuestionId,
    string Value,
    Guid? LoopInstanceId,
    int? IterationIndex) : INotification;
#pragma warning restore MSG0005
