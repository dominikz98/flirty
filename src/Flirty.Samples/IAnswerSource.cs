using Flirty.Runtime;

namespace Flirty.Samples;

/// <summary>
/// Liefert die Antwort auf eine gestellte Frage. Die Abstraktion entkoppelt den
/// <see cref="ConsoleDialogRunner"/> von der konkreten Eingabequelle, sodass die App interaktiv
/// von der Konsole liest, ein Test hingegen ein festes Skript einspeist.
/// </summary>
public interface IAnswerSource
{
    /// <summary>
    /// Ermittelt die Antwort auf die übergebene Frage.
    /// </summary>
    /// <param name="question">Die aktuell zu beantwortende Frage.</param>
    /// <returns>
    /// Der Antwortwert als roher JSON-Text im vom Fragetyp erwarteten Format (z. B. <c>"dev"</c> für
    /// eine Auswahl-/Freitext-Antwort).
    /// </returns>
    string GetAnswer(QuestionView question);
}
