using Flirty.Domain;
using Mediator;

namespace Flirty.Runtime;

/// <summary>
/// In-process notification (trigger scope <see cref="TriggerScope.OnDialogStarted"/>): published
/// after the <see cref="StartDialogCommandHandler"/> has persisted a <b>newly created</b> session.
/// A resume (continuing an already running session) deliberately does <b>not</b> trigger the notification.
/// </summary>
/// <remarks>
/// Host apps hook in their own reactions by registering an <see cref="INotificationHandler{TNotification}"/>
/// for this type; the engine invokes it automatically on start.
/// </remarks>
/// <param name="SessionId">The primary key of the newly created <see cref="DialogSession"/>.</param>
/// <param name="DialogId">The id of the started (pinned) dialog version.</param>
/// <param name="DialogKey">The business, stable key of the started dialog.</param>
/// <param name="ExternalUserKey">The business user key of the host app for which the dialog was started.</param>
/// <param name="CurrentQuestionId">
/// The id of the first open question of the session, or <see langword="null"/> if no question is open.
/// </param>
/// <param name="StartedAt">The point in time at which the session was started.</param>
// MSG0005: The Mediator source generator (martinothamar) requires a handler per message in the
// core compilation. In-process triggers are deliberately handled only by host apps via their own
// INotificationHandler<T> (see docs/TRIGGERS.md) – therefore suppressed here on purpose.
#pragma warning disable MSG0005
public sealed record DialogStartedNotification(
    Guid SessionId,
    Guid DialogId,
    string DialogKey,
    string ExternalUserKey,
    Guid? CurrentQuestionId,
    DateTimeOffset StartedAt) : INotification;
#pragma warning restore MSG0005
