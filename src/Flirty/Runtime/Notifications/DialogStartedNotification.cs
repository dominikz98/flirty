using Flirty.Domain;
using Mediator;

namespace Flirty.Runtime;

/// <summary>
/// In-Process-Notification (Trigger-Scope <see cref="TriggerScope.OnDialogStarted"/>): wird publiziert,
/// nachdem der <see cref="StartDialogCommandHandler"/> eine <b>neu angelegte</b> Session persistiert hat.
/// Ein Resume (Fortsetzen einer bereits laufenden Session) löst die Notification bewusst <b>nicht</b> aus.
/// </summary>
/// <remarks>
/// Host-Apps hängen eigene Reaktionen ein, indem sie einen <see cref="INotificationHandler{TNotification}"/>
/// für diesen Typ registrieren; die Engine ruft ihn beim Start automatisch auf.
/// </remarks>
/// <param name="SessionId">Der Primärschlüssel der neu angelegten <see cref="DialogSession"/>.</param>
/// <param name="DialogId">Die Id der gestarteten (gepinnten) Dialogversion.</param>
/// <param name="DialogKey">Der fachliche, stabile Schlüssel des gestarteten Dialogs.</param>
/// <param name="ExternalUserKey">Der fachliche Anwenderschlüssel der Host-App, für den gestartet wurde.</param>
/// <param name="CurrentQuestionId">
/// Die Id der ersten offenen Frage der Session, oder <see langword="null"/>, wenn keine Frage offen ist.
/// </param>
/// <param name="StartedAt">Der Zeitpunkt, zu dem die Session gestartet wurde.</param>
// MSG0005: Der Mediator-Source-Generator (martinothamar) verlangt je Nachricht einen Handler in der
// Core-Compilation. In-Process-Trigger werden bewusst erst von Host-Apps über eigene
// INotificationHandler<T> behandelt (siehe docs/TRIGGERS.md) – daher hier gezielt unterdrückt.
#pragma warning disable MSG0005
public sealed record DialogStartedNotification(
    Guid SessionId,
    Guid DialogId,
    string DialogKey,
    string ExternalUserKey,
    Guid? CurrentQuestionId,
    DateTimeOffset StartedAt) : INotification;
#pragma warning restore MSG0005
