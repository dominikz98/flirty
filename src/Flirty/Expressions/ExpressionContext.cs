using System.Collections.ObjectModel;
using Flirty.Domain;

namespace Flirty.Expressions;

/// <summary>
/// Der unveränderliche Auswertungskontext, gegen den ein <see cref="IConditionEvaluator"/> einen
/// Bedingungsausdruck auswertet. Bündelt die zum Auswertungszeitpunkt sichtbaren Daten einer
/// laufenden <see cref="DialogSession"/>: die bisherigen Antworten (nach Frage-Schlüssel), die je
/// Iteration gesammelten Loop-Collections (nach <c>CollectionKey</c>), den aktuellen
/// Iterationsindex, den Zeitpunkt und die Session selbst.
/// </summary>
/// <remarks>
/// Die Werte werden bewusst als <b>roher JSON-Text</b> (wie in <see cref="SessionAnswer.Value"/>
/// gespeichert) geführt; die typisierte Deserialisierung je Fragetyp obliegt der konkreten
/// Engine (Issue #23). Die Bausteine bilden die in <c>docs/ARCHITECTURE.md</c> §7 beschriebenen
/// fünf Kontext-Elemente ab: <c>answers</c>, Loop-Collections, <c>iterationIndex</c>, <c>now</c>,
/// <c>session</c>.
/// </remarks>
public sealed class ExpressionContext
{
    /// <summary>
    /// Erstellt einen neuen Auswertungskontext. Nicht angegebene Sammlungen werden als leere,
    /// nicht-<see langword="null"/>e Sammlungen initialisiert.
    /// </summary>
    /// <param name="session">Die laufende Session, deren Bedingungen ausgewertet werden.</param>
    /// <param name="now">Der Auswertungszeitpunkt (z. B. für zeitbasierte Ausdrücke).</param>
    /// <param name="answers">
    /// Die bisherigen Antworten, indiziert nach dem fachlichen Frage-Schlüssel (<see cref="Question.Key"/>);
    /// Werte sind roher JSON-Text (<see cref="SessionAnswer.Value"/>). <see langword="null"/> ⇒ leer.
    /// </param>
    /// <param name="collections">
    /// Die je Iteration gesammelten Loop-Antworten, indiziert nach dem <see cref="LoopDefinition.CollectionKey"/>;
    /// je Iteration ein roh-JSON-Eintrag. <see langword="null"/> ⇒ leer.
    /// </param>
    /// <param name="iterationIndex">
    /// Der nullbasierte Iterationsindex innerhalb einer Schleife oder <see langword="null"/> außerhalb
    /// einer Schleife (vgl. <see cref="SessionAnswer.IterationIndex"/>).
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> ist <see langword="null"/>.</exception>
    public ExpressionContext(
        DialogSession session,
        DateTimeOffset now,
        IReadOnlyDictionary<string, string?>? answers = null,
        IReadOnlyDictionary<string, IReadOnlyList<string?>>? collections = null,
        int? iterationIndex = null)
    {
        ArgumentNullException.ThrowIfNull(session);

        Session = session;
        Now = now;
        Answers = answers ?? ReadOnlyDictionary<string, string?>.Empty;
        Collections = collections ?? ReadOnlyDictionary<string, IReadOnlyList<string?>>.Empty;
        IterationIndex = iterationIndex;
    }

    /// <summary>Die laufende Session, in deren Kontext der Ausdruck ausgewertet wird.</summary>
    public DialogSession Session { get; }

    /// <summary>Der Auswertungszeitpunkt.</summary>
    public DateTimeOffset Now { get; }

    /// <summary>
    /// Die bisherigen Antworten der Session, indiziert nach dem fachlichen Frage-Schlüssel
    /// (<see cref="Question.Key"/>). Der Wert ist der rohe JSON-Text der Antwort
    /// (<see cref="SessionAnswer.Value"/>).
    /// </summary>
    public IReadOnlyDictionary<string, string?> Answers { get; }

    /// <summary>
    /// Die je Iteration gesammelten Antworten der Schleifen, indiziert nach dem
    /// <see cref="LoopDefinition.CollectionKey"/> (z. B. <c>positions</c> für <c>positions.Count &gt; 0</c>).
    /// Jeder Listeneintrag steht für eine Iteration und trägt rohen JSON-Text.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string?>> Collections { get; }

    /// <summary>
    /// Der nullbasierte Iterationsindex innerhalb einer Schleife oder <see langword="null"/>
    /// außerhalb einer Schleife.
    /// </summary>
    public int? IterationIndex { get; }
}
