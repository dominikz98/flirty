using Flirty.Domain;
using Mediator;

namespace Flirty.Runtime;

/// <summary>
/// In-process notification (trigger scope <see cref="TriggerScope.OnDialogCompleted"/>): published
/// after a session has been completed – either by the <see cref="SubmitAnswerCommandHandler"/>
/// (last answer with no applying transition) or by the <see cref="EditAnswerCommandHandler"/>, when
/// the recomputation after an edit leads to a completion.
/// </summary>
/// <remarks>
/// Carries the answers given at the completion time as navigation-free <see cref="SessionAnswerView"/>
/// so that host handlers can evaluate the result without knowing the configuration graph.
/// </remarks>
/// <param name="SessionId">The primary key of the completed <see cref="DialogSession"/>.</param>
/// <param name="DialogKey">The business, stable key of the completed dialog.</param>
/// <param name="Answers">
/// The answers given over the course of the session in ascending <see cref="SessionAnswer.Sequence"/>
/// (chronological order).
/// </param>
// MSG0005: The Mediator source generator (martinothamar) requires a handler per message in the
// core compilation. In-process triggers are deliberately handled only by host apps via their own
// INotificationHandler<T> (see docs/TRIGGERS.md) – therefore suppressed here on purpose.
#pragma warning disable MSG0005
public sealed record DialogCompletedNotification(
    Guid SessionId,
    string DialogKey,
    IReadOnlyList<SessionAnswerView> Answers) : INotification;
#pragma warning restore MSG0005
