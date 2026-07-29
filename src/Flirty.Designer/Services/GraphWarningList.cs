using Flirty.Designer.Models;
using Flirty.Runtime.Admin;

namespace Flirty.Designer.Services;

/// <summary>
/// Fasst die Warnungen eines Graphen als Textliste zusammen – jede mit ihrem <b>Verursacher</b> davor.
/// Das ist die Fassung, die der <c>DialogEditor</c> im Veröffentlichungs-Abschnitt zeigt und an der die
/// Rückfrage vor dem Veröffentlichen hängt.
/// </summary>
/// <remarks>
/// <para>
/// Quelle ist <see cref="DialogGraphModel.AllWarnings"/> und damit der <b>ganze</b> Graph: Dialog,
/// Knoten (einschließlich der Erreichbarkeit), Kanten und Schleifen. Bis #118 speiste sich die
/// Rückfrage nur aus dem <see cref="TransitionWarningAnalyzer"/>; eine unerreichbare Frage – vom Graphen
/// deutlich ausgewiesen – ließ sich deshalb ohne Rückfrage veröffentlichen. Der Defekt war nicht die
/// eine fehlende Warnung, sondern die <b>handverlesene Auswahl</b>: Jede künftige Warnungsart wäre
/// wieder herausgefallen. Über <see cref="DialogGraphModel.AllWarnings"/> ist die Liste strukturell
/// geschlossen.
/// </para>
/// <para>
/// Eigener Service und nicht im <c>@code</c>-Block, weil <c>tests/Flirty.Tests/Designer</c> keine
/// Komponenten rendert (kein bUnit): Was im Razor liegt, ist nicht prüfbar. Dieselbe Abgrenzung wie bei
/// <see cref="GraphEditing"/>.
/// </para>
/// <para>
/// Die <b>Wortlaute</b> entstehen hier nicht – sie kommen unverändert aus
/// <see cref="TransitionWarningAnalyzer"/>, <see cref="LoopAnalyzer"/> und
/// <see cref="DialogGraphBuilder"/> und sind Vertrag gegenüber Tests und E2E-Suite. Diese Klasse setzt
/// ausschließlich das Präfix davor.
/// </para>
/// </remarks>
internal static class GraphWarningList
{
    /// <summary>
    /// Beschreibt alle Warnungen des Graphen als Liste, jede mit dem Schlüssel ihres Verursachers davor.
    /// </summary>
    /// <param name="detail">Der Dialog samt Graph – Quelle der Frage- und Schleifen-Schlüssel.</param>
    /// <param name="model">Das Zeichenmodell, aus dem die Warnungen stammen.</param>
    /// <returns>
    /// Die Warnungen in der Reihenfolge von <see cref="DialogGraphModel.AllWarnings"/>; leer, wenn der
    /// Graph stimmig ist.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="detail"/> oder <paramref name="model"/> ist <see langword="null"/>.
    /// </exception>
    public static IReadOnlyList<string> Describe(DialogDetail detail, DialogGraphModel model)
    {
        ArgumentNullException.ThrowIfNull(detail);
        ArgumentNullException.ThrowIfNull(model);

        return [.. model.AllWarnings.Select(warning => Line(detail, warning))];
    }

    /// <summary>Setzt einer Warnung ihren Verursacher voran – sofern sie einen hat.</summary>
    private static string Line(DialogDetail detail, GraphWarning warning)
    {
        var origin = Origin(detail, warning);
        return origin is null ? warning.Text : $"{origin}: {warning.Text}";
    }

    /// <summary>
    /// Der Schlüssel des verursachenden Elements, oder <see langword="null"/> für eine Warnung am Dialog
    /// als Ganzem.
    /// </summary>
    /// <remarks>
    /// <see cref="GraphWarning.QuestionId"/> trägt bereits die Bezugsfrage – bei einer Frage sie selbst,
    /// bei einem Übergang seine Ausgangsfrage. Nur der Schleifen-Marker hat keine (er hängt am Rahmen,
    /// nicht an einer Frage) und wird über seinen <c>CollectionKey</c> benannt; eine Warnung am Dialog
    /// bleibt ohne Präfix, weil ihr Verursacher der Dialog selbst ist.
    /// </remarks>
    private static string? Origin(DialogDetail detail, GraphWarning warning)
        => warning switch
        {
            { QuestionId: { } questionId } => QuestionKey(detail, questionId),
            { Kind: GraphElementKind.Loop, ElementId: { } loopId } => LoopKey(detail, loopId),
            _ => null,
        };

    /// <summary>
    /// Der fachliche Schlüssel einer Frage. Der Fallback ist Absicht: Ein Verweis auf eine nicht (mehr)
    /// vorhandene Frage ist selbst ein Befund und darf nicht als leeres Präfix verschwinden.
    /// </summary>
    private static string QuestionKey(DialogDetail detail, Guid questionId)
        => detail.Questions.FirstOrDefault(question => question.Id == questionId)?.Key
            ?? $"unbekannt ({questionId})";

    /// <summary>Der <c>CollectionKey</c> eines Schleifen-Markers, mit demselben Fallback.</summary>
    private static string LoopKey(DialogDetail detail, Guid loopId)
        => detail.Loops.FirstOrDefault(loop => loop.Id == loopId)?.CollectionKey
            ?? $"unbekannt ({loopId})";
}
