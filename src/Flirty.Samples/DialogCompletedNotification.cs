using Flirty.Runtime;
using Mediator;

namespace Flirty.Samples;

/// <summary>
/// Sample-eigene In-Process-Notification, mit der das Console-Sample seine registrierten Handler
/// benachrichtigt, sobald die Facade den Abschluss eines Dialogs meldet.
/// </summary>
/// <remarks>
/// Bewusst im Sample definiert: Die Engine publiziert (Stand M1) noch keine Notifications –
/// das engine-getriebene Publishing aus den Command-Handlern folgt in EPIC 4 (M2). Bis dahin zeigt
/// das Sample den In-Process-Rückkanal, indem der <see cref="ConsoleDialogRunner"/> nach dem
/// Facade-Durchlauf die registrierten <see cref="INotificationHandler{TNotification}"/> für diese
/// Notification aufruft. Der Typ implementiert bereits <see cref="INotification"/>, sodass er sich in
/// EPIC 4 ohne Änderung über den Mediator publizieren lässt.
/// </remarks>
/// <param name="SessionId">Der Primärschlüssel der abgeschlossenen Session.</param>
/// <param name="DialogKey">Der fachliche Schlüssel des abgeschlossenen Dialogs.</param>
/// <param name="Answers">Die im Verlauf der Session gegebenen Antworten.</param>
public sealed record DialogCompletedNotification(
    Guid SessionId,
    string DialogKey,
    IReadOnlyList<SessionAnswerView> Answers) : INotification;
