using Flirty.Domain;
using Mediator;

namespace Flirty.Runtime;

/// <summary>
/// In-Process-Notification (Trigger-Scope <see cref="TriggerScope.OnDialogCompleted"/>): wird publiziert,
/// nachdem eine Session abgeschlossen wurde – entweder durch den <see cref="SubmitAnswerCommandHandler"/>
/// (letzte Antwort ohne greifenden Übergang) oder durch den <see cref="EditAnswerCommandHandler"/>, wenn
/// die Neuberechnung nach einer Editierung auf einen Abschluss führt.
/// </summary>
/// <remarks>
/// Trägt die zum Abschlusszeitpunkt gegebenen Antworten als navigationsfreie <see cref="SessionAnswerView"/>
/// mit, damit Host-Handler das Ergebnis auswerten können, ohne den Konfigurationsgraphen zu kennen.
/// </remarks>
/// <param name="SessionId">Der Primärschlüssel der abgeschlossenen <see cref="DialogSession"/>.</param>
/// <param name="DialogKey">Der fachliche, stabile Schlüssel des abgeschlossenen Dialogs.</param>
/// <param name="Answers">
/// Die im Verlauf der Session gegebenen Antworten in aufsteigender <see cref="SessionAnswer.Sequence"/>
/// (chronologische Reihenfolge).
/// </param>
// MSG0005: Der Mediator-Source-Generator (martinothamar) verlangt je Nachricht einen Handler in der
// Core-Compilation. In-Process-Trigger werden bewusst erst von Host-Apps über eigene
// INotificationHandler<T> behandelt (siehe docs/TRIGGERS.md) – daher hier gezielt unterdrückt.
#pragma warning disable MSG0005
public sealed record DialogCompletedNotification(
    Guid SessionId,
    string DialogKey,
    IReadOnlyList<SessionAnswerView> Answers) : INotification;
#pragma warning restore MSG0005
