namespace Flirty.Designer.Models;

/// <summary>
/// Die Art eines Graph-Elements – Ziel einer <see cref="GraphWarning"/> und zugleich Art der Auswahl in
/// <see cref="GraphSelection"/>.
/// </summary>
/// <remarks>
/// Die beiden Rollen decken sich nicht vollständig: <see cref="Trigger"/> ist ausschließlich auswählbar
/// (es gibt keine Warnung an einem Trigger), und <see cref="Dialog"/> trägt als Auswahl die
/// Scope-Marker – die Trigger ohne Frage-Bezug. Ein zweites Enum dafür wäre ein Duplikat mit vier
/// identischen Werten.
/// </remarks>
public enum GraphElementKind
{
    /// <summary>
    /// Der Dialog als Ganzes: eine Warnung ohne einzelnes Element als Ursache – bzw., als Auswahl, die
    /// Scope-Marker mit den Triggern ohne Frage-Bezug (#103).
    /// </summary>
    Dialog = 0,

    /// <summary>Eine Frage (Knoten auf dem Canvas).</summary>
    Question = 1,

    /// <summary>Ein Übergang (Kante auf dem Canvas).</summary>
    Transition = 2,

    /// <summary>Ein Schleifen-Marker (Bereichsrahmen auf dem Canvas).</summary>
    Loop = 3,

    /// <summary>Ein einzelner Trigger (Chip am Knoten oder am Scope-Marker) – nur als Auswahl (#103).</summary>
    Trigger = 4,
}

/// <summary>
/// Eine Konfigurationswarnung <b>mit Ortsangabe</b>: derselbe Befund, den der <c>DialogEditor</c> als
/// Fließtext zeigt, zusätzlich dem Element zugeordnet, das ihn verursacht.
/// </summary>
/// <remarks>
/// <para>
/// Die Graph-Ansicht (#101) muss Warnungen <b>am betroffenen Knoten bzw. an der betroffenen Kante</b>
/// anzeigen, die Listenansicht braucht weiterhin Fließtext je Ausgangsfrage. Beides aus derselben
/// Quelle zu speisen ist der ganze Zweck dieses Typs – eine zweite Warnungslogik neben
/// <see cref="Flirty.Designer.Services.TransitionWarningAnalyzer"/> und
/// <see cref="Flirty.Designer.Services.LoopAnalyzer"/> würde unweigerlich auseinanderlaufen.
/// </para>
/// <para>
/// <see cref="Text"/> ist bewusst der <b>unveränderte</b> Wortlaut, den der Designer schon immer
/// gezeigt hat: Die Texte sind Vertrag gegenüber den Tests und der E2E-Suite.
/// </para>
/// <para>
/// Der Typ ist <see langword="public"/>, weil Razor-Komponenten <see langword="public"/> generiert
/// werden und ein <see langword="internal"/> Typ an einem <c>[Parameter]</c> zu CS0053 führt – unter
/// <c>TreatWarningsAsErrors</c> also zum Buildfehler. Dieselbe Begründung wie bei
/// <see cref="AnswerChoice"/>.
/// </para>
/// </remarks>
/// <param name="Kind">Die Art des betroffenen Elements.</param>
/// <param name="ElementId">
/// Der Primärschlüssel des betroffenen Elements; <see langword="null"/> bei
/// <see cref="GraphElementKind.Dialog"/>.
/// </param>
/// <param name="QuestionId">
/// Die Bezugsfrage für Gruppierung und Präfix in der Listenansicht: bei einer Frage identisch zu
/// <paramref name="ElementId"/>, bei einem Übergang dessen Ausgangsfrage, sonst <see langword="null"/>.
/// </param>
/// <param name="Text">Der Warntext – wortgleich zu dem, was der <c>DialogEditor</c> anzeigt.</param>
public sealed record GraphWarning(
    GraphElementKind Kind,
    Guid? ElementId,
    Guid? QuestionId,
    string Text)
{
    /// <summary>Eine Warnung, die an einer Frage hängt (Knoten).</summary>
    /// <param name="questionId">Die betroffene Frage.</param>
    /// <param name="text">Der Warntext.</param>
    /// <returns>Die verortete Warnung.</returns>
    public static GraphWarning ForQuestion(Guid questionId, string text)
        => new(GraphElementKind.Question, questionId, questionId, text);

    /// <summary>Eine Warnung, die an einem Übergang hängt (Kante).</summary>
    /// <param name="transitionId">Der betroffene Übergang.</param>
    /// <param name="fromQuestionId">Dessen Ausgangsfrage – die Bezugsfrage der Listenansicht.</param>
    /// <param name="text">Der Warntext.</param>
    /// <returns>Die verortete Warnung.</returns>
    public static GraphWarning ForTransition(Guid transitionId, Guid fromQuestionId, string text)
        => new(GraphElementKind.Transition, transitionId, fromQuestionId, text);

    /// <summary>Eine Warnung, die an einem Schleifen-Marker hängt (Bereichsrahmen).</summary>
    /// <param name="loopId">Der betroffene Marker.</param>
    /// <param name="text">Der Warntext.</param>
    /// <returns>Die verortete Warnung.</returns>
    public static GraphWarning ForLoop(Guid loopId, string text)
        => new(GraphElementKind.Loop, loopId, null, text);

    /// <summary>Eine Warnung, die den Dialog als Ganzes betrifft (kein einzelnes Element).</summary>
    /// <param name="text">Der Warntext.</param>
    /// <returns>Die Warnung ohne Elementbezug.</returns>
    public static GraphWarning ForDialog(string text)
        => new(GraphElementKind.Dialog, null, null, text);
}
