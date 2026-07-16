using Flirty.Domain;

namespace Flirty.Runtime;

/// <summary>
/// Ergebnis von <see cref="ResumeDialogQuery"/> bzw. <see cref="IFlirtyEngine.ResumeDialogAsync"/>:
/// der aktuelle Zustand einer <see cref="DialogSession"/> – Status, die (ggf.) aktuell offene Frage und
/// die bisher gegebenen Antworten – zum Wiederherstellen einer Befragung (z. B. nach einem Reload der
/// Host-App).
/// </summary>
/// <param name="SessionId">Der Primärschlüssel der abgefragten <see cref="DialogSession"/>.</param>
/// <param name="Status">
/// Der aktuelle Status der Session (<see cref="SessionStatus.InProgress"/>,
/// <see cref="SessionStatus.Completed"/> oder <see cref="SessionStatus.Abandoned"/>).
/// </param>
/// <param name="CurrentQuestion">
/// Die aktuell offene Frage, die dem Anwender zu präsentieren ist, oder <see langword="null"/>, wenn die
/// Session keine offene Frage mehr hat (abgeschlossen bzw. abgebrochen).
/// </param>
/// <param name="Answers">
/// Die bisher gegebenen Antworten der Session in aufsteigender <see cref="SessionAnswer.Sequence"/>
/// (chronologische Reihenfolge); leer, wenn noch keine Antwort erfasst wurde.
/// </param>
public sealed record ResumeDialogResult(
    Guid SessionId,
    SessionStatus Status,
    QuestionView? CurrentQuestion,
    IReadOnlyList<SessionAnswerView> Answers);

/// <summary>
/// Schlanke, unveränderliche Sicht auf einen <see cref="SessionAnswer"/> für die Laufzeit-API – ohne
/// EF-Core-Navigationen, damit Host-Apps bereits gegebene Antworten anzeigen können, ohne den
/// Konfigurationsgraphen zu kennen.
/// </summary>
/// <param name="QuestionId">Der Primärschlüssel der beantworteten Frage.</param>
/// <param name="QuestionKey">
/// Der fachliche, stabile Schlüssel der beantworteten Frage (aus der gepinnten Dialogversion aufgelöst).
/// </param>
/// <param name="Value">Der gespeicherte Antwortwert als roher JSON-Text (Format abhängig vom Fragetyp).</param>
/// <param name="Sequence">Die fortlaufende Position der Antwort innerhalb der Session (beginnend bei 0).</param>
/// <param name="AnsweredAt">Der Zeitpunkt, zu dem die Antwort erfasst wurde.</param>
public sealed record SessionAnswerView(
    Guid QuestionId,
    string QuestionKey,
    string Value,
    int Sequence,
    DateTimeOffset AnsweredAt);
