using Flirty.Domain;
using Mediator;

namespace Flirty.Runtime;

/// <summary>
/// In-Process-Notification (Trigger-Scope <see cref="TriggerScope.AfterQuestion"/>): wird publiziert,
/// nachdem der <see cref="SubmitAnswerCommandHandler"/> die Antwort persistiert und die ausgehenden
/// Übergänge (Branching) der Frage ausgewertet hat – also feststeht, ob die Session weiterschaltet oder
/// abschließt.
/// </summary>
/// <remarks>
/// Ergänzt die <see cref="AnswerSubmittedNotification"/> um das Ergebnis der Übergangs-Auswertung: welche
/// Frage als Nächstes offen ist bzw. ob der Dialog abgeschlossen wurde. Downstream-Trigger, die nur auf
/// eine bestimmte Frage reagieren, filtern über <see cref="QuestionId"/>.
/// </remarks>
/// <param name="SessionId">Der Primärschlüssel der <see cref="DialogSession"/>.</param>
/// <param name="DialogKey">Der fachliche, stabile Schlüssel des laufenden Dialogs.</param>
/// <param name="QuestionId">Die Id der soeben beantworteten Frage.</param>
/// <param name="NextQuestionId">
/// Die Id der nächsten offenen Frage, oder <see langword="null"/>, wenn kein Übergang mehr greift und der
/// Dialog abgeschlossen wurde.
/// </param>
/// <param name="IsCompleted">
/// <see langword="true"/>, wenn die Antwort den Dialog abgeschlossen hat; sonst <see langword="false"/>.
/// </param>
// MSG0005: Der Mediator-Source-Generator (martinothamar) verlangt je Nachricht einen Handler in der
// Core-Compilation. In-Process-Trigger werden bewusst erst von Host-Apps über eigene
// INotificationHandler<T> behandelt (siehe docs/TRIGGERS.md) – daher hier gezielt unterdrückt.
#pragma warning disable MSG0005
public sealed record QuestionAnsweredNotification(
    Guid SessionId,
    string DialogKey,
    Guid QuestionId,
    Guid? NextQuestionId,
    bool IsCompleted) : INotification;
#pragma warning restore MSG0005
