using Flirty.Domain;
using Mediator;

namespace Flirty.Runtime;

/// <summary>
/// In-Process-Notification (Trigger-Scope <see cref="TriggerScope.AfterAnswer"/>): wird publiziert,
/// nachdem der <see cref="SubmitAnswerCommandHandler"/> eine eingereichte Antwort persistiert hat –
/// unabhängig davon, ob die Session danach weiterschaltet oder abschließt.
/// </summary>
/// <remarks>
/// Feuert je eingereichter Antwort einmal. Nachträgliche Korrekturen über den
/// <see cref="EditAnswerCommandHandler"/> lösen diese Notification bewusst <b>nicht</b> aus.
/// </remarks>
/// <param name="SessionId">Der Primärschlüssel der <see cref="DialogSession"/>, für die geantwortet wurde.</param>
/// <param name="DialogKey">Der fachliche, stabile Schlüssel des laufenden Dialogs.</param>
/// <param name="QuestionId">Die Id der beantworteten Frage.</param>
/// <param name="Value">Der eingereichte Antwortwert als roher JSON-Text (Format abhängig vom Fragetyp).</param>
/// <param name="LoopInstanceId">
/// Die Instanz-Id der Schleife, zu der die Antwort gehört, oder <see langword="null"/>, wenn die Antwort
/// außerhalb einer Schleife gegeben wurde.
/// </param>
/// <param name="IterationIndex">
/// Der nullbasierte Iterationsindex innerhalb der Schleife oder <see langword="null"/> außerhalb einer
/// Schleife.
/// </param>
// MSG0005: Der Mediator-Source-Generator (martinothamar) verlangt je Nachricht einen Handler in der
// Core-Compilation. In-Process-Trigger werden bewusst erst von Host-Apps über eigene
// INotificationHandler<T> behandelt (siehe docs/TRIGGERS.md) – daher hier gezielt unterdrückt.
#pragma warning disable MSG0005
public sealed record AnswerSubmittedNotification(
    Guid SessionId,
    string DialogKey,
    Guid QuestionId,
    string Value,
    Guid? LoopInstanceId,
    int? IterationIndex) : INotification;
#pragma warning restore MSG0005
