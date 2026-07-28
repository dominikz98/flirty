using Flirty.Designer.Models;
using Flirty.Runtime.Admin;

namespace Flirty.Designer.Services;

/// <summary>
/// Meldet Übergangs-Konfigurationen, die zur Laufzeit anders wirken als gedacht – die Regeln spiegeln
/// den <c>TransitionResolver</c> der Engine: Es gewinnt der erste zutreffende <b>nicht</b>-Default in
/// <c>Priority</c>-Reihenfolge (ein leerer Ausdruck trifft immer zu), sonst der erste Default; trifft
/// nichts, wirft die Runtime.
/// </summary>
/// <remarks>
/// <para>
/// Die Regeln standen bis #101 privat in <c>Components/Pages/DialogEditor.razor</c>. Sie sind
/// hierher gewandert, weil die Graph-Ansicht dieselben Befunde braucht – dort aber
/// <b>am betroffenen Knoten bzw. an der betroffenen Kante</b> statt als Fließtextliste. Wortlaut und
/// Reihenfolge sind unverändert übernommen; die Texte sind Vertrag gegenüber Tests und E2E-Suite.
/// Dieselbe Bauform wie <see cref="LoopAnalyzer"/>: statische Klasse, <see cref="DialogDetail"/> rein,
/// verortete Warnungen raus, keine DI.
/// </para>
/// <para>
/// Was hier <b>nicht</b> steht: die Schleifen-Befunde (die liefert <see cref="LoopAnalyzer"/>) und die
/// Erreichbarkeit im Graphen (die kennt erst der <c>DialogGraphBuilder</c>, weil sie von der
/// Einstiegsfrage abhängt).
/// </para>
/// </remarks>
internal static class TransitionWarningAnalyzer
{
    /// <summary>Die ausgehenden Übergänge einer Frage in Auswertungsreihenfolge.</summary>
    /// <param name="detail">Der Dialog samt Graph.</param>
    /// <param name="questionId">Die Ausgangsfrage.</param>
    /// <returns>Die Übergänge nach <c>Priority</c> sortiert.</returns>
    public static IReadOnlyList<TransitionDetail> Outgoing(DialogDetail detail, Guid questionId)
    {
        ArgumentNullException.ThrowIfNull(detail);

        return
        [
            .. detail.Transitions
                .Where(transition => transition.FromQuestionId == questionId)
                .OrderBy(transition => transition.Priority)
        ];
    }

    /// <summary>
    /// Prüft die Übergänge <b>einer</b> Ausgangsfrage.
    /// </summary>
    /// <param name="outgoing">Die Übergänge einer Ausgangsfrage in Auswertungsreihenfolge.</param>
    /// <returns>Die anzuzeigenden Warnungen (leer, wenn alles stimmig ist).</returns>
    public static IReadOnlyList<GraphWarning> Analyze(IReadOnlyList<TransitionDetail> outgoing)
    {
        ArgumentNullException.ThrowIfNull(outgoing);

        if (outgoing.Count == 0)
        {
            return [];
        }

        // Die Ausgangsfrage ist für alle Übergänge dieselbe – sie trägt die Warnungen, die keinem
        // einzelnen Übergang anzulasten sind.
        var from = outgoing[0].FromQuestionId;

        var warnings = new List<GraphWarning>();
        var defaults = outgoing.Where(transition => transition.IsDefault).ToList();
        var unconditional = outgoing
            .Select((transition, index) => (Transition: transition, Index: index))
            .FirstOrDefault(entry =>
                !entry.Transition.IsDefault && string.IsNullOrWhiteSpace(entry.Transition.Expression));

        if (defaults.Count == 0 && unconditional.Transition is null)
        {
            warnings.Add(GraphWarning.ForQuestion(
                from,
                "Kein Default-Übergang: Trifft zur Laufzeit keine Bedingung zu, bricht die Session mit "
                + "einem Fehler ab."));
        }

        if (defaults.Count > 1)
        {
            warnings.Add(GraphWarning.ForQuestion(
                from,
                "Mehrere Default-Übergänge – es greift nur der oberste."));
        }

        var decoratedDefault = defaults.FirstOrDefault(
            transition => !string.IsNullOrWhiteSpace(transition.Expression));
        if (decoratedDefault is not null)
        {
            warnings.Add(GraphWarning.ForTransition(
                decoratedDefault.Id,
                from,
                "Die Bedingung eines Default-Übergangs wird zur Laufzeit nicht ausgewertet."));
        }

        if (unconditional.Transition is not null && unconditional.Index < outgoing.Count - 1)
        {
            warnings.Add(GraphWarning.ForTransition(
                unconditional.Transition.Id,
                from,
                $"Der bedingungslose Übergang an Position {unconditional.Index + 1} greift immer – die "
                + "nachfolgenden Übergänge werden nie geprüft."));
        }

        return warnings;
    }

    /// <summary>
    /// Prüft die Übergänge des gesamten Graphen – Fragen in Dialog-Reihenfolge, Fragen ohne ausgehende
    /// Übergänge übersprungen (die enden regulär und sind kein Befund).
    /// </summary>
    /// <remarks>
    /// Übergänge mit unbekannter Ausgangsfrage bleiben hier außen vor: Sie werden nie ausgewertet und
    /// haben keinen Knoten, an dem eine Warnung hängen könnte. Der <c>DialogEditor</c> weist sie
    /// getrennt aus (<c>Orphans()</c>), die Graph-Ansicht ebenso.
    /// </remarks>
    /// <param name="detail">Der Dialog samt Graph.</param>
    /// <returns>Die offenen Warnungen; leer, wenn der Graph stimmig ist.</returns>
    public static IReadOnlyList<GraphWarning> Analyze(DialogDetail detail)
    {
        ArgumentNullException.ThrowIfNull(detail);

        return
        [
            .. from question in detail.Questions
               let outgoing = Outgoing(detail, question.Id)
               where outgoing.Count > 0
               from warning in Analyze(outgoing)
               select warning
        ];
    }
}
