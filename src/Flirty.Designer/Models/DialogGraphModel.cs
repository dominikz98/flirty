using Flirty.Domain;
using Flirty.Runtime.Admin;

namespace Flirty.Designer.Models;

/// <summary>
/// Was auf dem Canvas ausgewählt ist. <see langword="null"/> bedeutet: nichts ausgewählt.
/// </summary>
/// <param name="Kind">Die Art des gewählten Elements.</param>
/// <param name="Id">Sein Primärschlüssel.</param>
public sealed record GraphSelection(GraphElementKind Kind, Guid Id);

/// <summary>Ein Trigger als Anhängsel an einem Knoten oder an einem Scope-Marker.</summary>
/// <param name="TriggerId">Der Trigger.</param>
/// <param name="Label">Die kurze Beschriftung des Chips (Kanal und Ziel).</param>
/// <param name="Title">Die vollständige Beschreibung für Tooltip und Screenreader.</param>
/// <param name="Kind">Der Kanal – bestimmt die Einfärbung.</param>
public sealed record GraphTriggerChip(Guid TriggerId, string Label, string Title, TriggerKind Kind);

/// <summary>Eine Frage als Knoten auf dem Canvas.</summary>
/// <param name="QuestionId">Die Frage.</param>
/// <param name="Key">Ihr Schlüssel – die Kopfzeile der Karte.</param>
/// <param name="Text">Der gekürzte Fragetext.</param>
/// <param name="FullText">Der vollständige Fragetext (Tooltip).</param>
/// <param name="TypeLabel">Der Anzeigetext des Fragetyps.</param>
/// <param name="IsRequired">Ob die Frage als Pflichtfrage konfiguriert ist.</param>
/// <param name="OptionCount">Die Zahl der Antwortoptionen (nur bei Auswahl-Typen von Belang).</param>
/// <param name="UsesOptions">Ob der Fragetyp überhaupt Antwortoptionen kennt.</param>
/// <param name="X">Linke obere Ecke in px.</param>
/// <param name="Y">Linke obere Ecke in px.</param>
/// <param name="IsStart">Ob die Frage die Einstiegsfrage des Dialogs ist.</param>
/// <param name="IsTerminal">
/// Ob die Frage keinen ausgehenden Übergang hat. Das ist <b>kein Fehler</b>, sondern der reguläre
/// Dialogabschluss: <c>TransitionResolver.ResolveTransitionTarget</c> liefert dort <see langword="null"/>.
/// </param>
/// <param name="IsUnreachable">Ob von der Einstiegsfrage aus kein Pfad hierher führt.</param>
/// <param name="IsLoopEntry">Ob die Frage Einstiegsfrage einer Schleife ist.</param>
/// <param name="IsLoopBreaking">Ob die Frage Breaking Question einer Schleife ist.</param>
/// <param name="InLoop">Ob die Frage im Bereich einer Schleife liegt.</param>
/// <param name="Triggers">Die Trigger mit <see cref="TriggerScope.AfterQuestion"/> auf diese Frage.</param>
/// <param name="Warnings">Die Warnungen, die an dieser Frage hängen.</param>
/// <param name="AriaLabel">Die vollständige Beschreibung für Screenreader.</param>
public sealed record GraphNode(
    Guid QuestionId,
    string Key,
    string Text,
    string FullText,
    string TypeLabel,
    bool IsRequired,
    int OptionCount,
    bool UsesOptions,
    double X,
    double Y,
    bool IsStart,
    bool IsTerminal,
    bool IsUnreachable,
    bool IsLoopEntry,
    bool IsLoopBreaking,
    bool InLoop,
    IReadOnlyList<GraphTriggerChip> Triggers,
    IReadOnlyList<GraphWarning> Warnings,
    string AriaLabel);

/// <summary>Ein Übergang als Kante auf dem Canvas.</summary>
/// <param name="TransitionId">Der Übergang.</param>
/// <param name="FromQuestionId">Die Ausgangsfrage.</param>
/// <param name="TargetQuestionId">Die Zielfrage.</param>
/// <param name="Path">Der SVG-Pfad.</param>
/// <param name="Shape">Die Zeichenform.</param>
/// <param name="LabelX">Ankerpunkt der Beschriftung.</param>
/// <param name="LabelY">Ankerpunkt der Beschriftung.</param>
/// <param name="Label">Die gekürzte Beschriftung (Bedingung bzw. „Default“).</param>
/// <param name="Position">Die 1-basierte Auswertungsposition innerhalb der Ausgangsfrage.</param>
/// <param name="IsDefault">Ob es der Default-Übergang ist.</param>
/// <param name="IsBackJump">
/// Ob der Übergang auf eine frühere Frage <b>in der Listenreihenfolge</b> zeigt – dieselbe Aussage, die
/// der <c>DialogEditor</c> als Badge „Rücksprung“ zeigt. Bewusst nicht identisch mit
/// <see cref="GraphEdgeShape.BackJump"/>: Das ist eine Aussage über die <i>Schichtung</i> des Layouts.
/// Beide sind richtig, aber sie beantworten verschiedene Fragen.
/// </param>
/// <param name="Warnings">Die Warnungen, die an dieser Kante hängen.</param>
/// <param name="AriaLabel">Die vollständige Beschreibung für Screenreader.</param>
public sealed record GraphEdge(
    Guid TransitionId,
    Guid FromQuestionId,
    Guid TargetQuestionId,
    string Path,
    GraphEdgeShape Shape,
    double LabelX,
    double LabelY,
    string Label,
    int Position,
    bool IsDefault,
    bool IsBackJump,
    IReadOnlyList<GraphWarning> Warnings,
    string AriaLabel);

/// <summary>
/// Eine Schleife als Bereichsrahmen um die Knoten ihres Bodys – kein eigener Knoten, weil sie im
/// Domänenmodell keiner ist: <c>LoopDefinition</c> ist ein Marker über dem Branching.
/// </summary>
/// <param name="LoopId">Der Schleifen-Marker.</param>
/// <param name="CollectionKey">Der Sammel-Schlüssel – die Beschriftung des Rahmens.</param>
/// <param name="X">Linke obere Ecke in px.</param>
/// <param name="Y">Linke obere Ecke in px.</param>
/// <param name="Width">Breite in px.</param>
/// <param name="Height">Höhe in px.</param>
/// <param name="EntryKey">Schlüssel der Einstiegsfrage (leer, wenn sie fehlt).</param>
/// <param name="BreakingKey">Schlüssel der Breaking Question (leer, wenn sie fehlt).</param>
/// <param name="Warnings">Die Warnungen, die an diesem Marker hängen.</param>
public sealed record GraphLoopFrame(
    Guid LoopId,
    string CollectionKey,
    double X,
    double Y,
    double Width,
    double Height,
    string EntryKey,
    string BreakingKey,
    IReadOnlyList<GraphWarning> Warnings);

/// <summary>
/// Ein Marker für die Trigger, die an keiner einzelnen Frage hängen – Start und Abschluss des Dialogs.
/// </summary>
/// <param name="Title">Die Beschriftung.</param>
/// <param name="X">Linke obere Ecke in px.</param>
/// <param name="Y">Linke obere Ecke in px.</param>
/// <param name="Triggers">Die Trigger dieses Zeitpunkts.</param>
public sealed record GraphScopeMarker(
    string Title,
    double X,
    double Y,
    IReadOnlyList<GraphTriggerChip> Triggers);

/// <summary>
/// Der gesamte Dialog-Graph, fertig zum Zeichnen: Knoten, Kanten, Schleifenrahmen, Scope-Marker und
/// alle Warnungen – jede an dem Element, das sie verursacht.
/// </summary>
/// <remarks>
/// Wird von <see cref="Flirty.Designer.Services.DialogGraphBuilder"/> <b>einmal nach dem Laden</b>
/// gebaut und in einem Feld gehalten. Aus dem Markup heraus aufgerufen liefe die ganze Anordnung bei
/// jedem Render erneut, also bei jedem Klick.
/// </remarks>
/// <param name="Dialog">Die Kopfdaten des Dialogs.</param>
/// <param name="Nodes">Die Knoten, sortiert nach Schicht und Spalte – zugleich die Tab-Reihenfolge.</param>
/// <param name="Edges">Die Kanten.</param>
/// <param name="Loops">Die Schleifen-Rahmen.</param>
/// <param name="StartMarker">Der Start-Marker, falls er Trigger trägt.</param>
/// <param name="EndMarker">Der Abschluss-Marker, falls er Trigger trägt.</param>
/// <param name="DialogWarnings">Warnungen ohne Elementbezug (etwa die fehlende Einstiegsfrage).</param>
/// <param name="OrphanTransitions">
/// Übergänge, deren Ausgangs- oder Zielfrage nicht (mehr) zum Dialog gehört. Sie sind nicht zeichenbar
/// und werden getrennt ausgewiesen, statt still zu verschwinden.
/// </param>
/// <param name="OrphanTriggers">Trigger, die auf eine nicht (mehr) vorhandene Frage zeigen.</param>
/// <param name="Summary">Eine Kurzfassung des Graphen in Worten – die Alternative zum Bild.</param>
/// <param name="MinY">Obere Kante der Zeichenfläche (negativ, wenn ein Start-Marker darüber liegt).</param>
/// <param name="Width">Breite der Zeichenfläche in px.</param>
/// <param name="Height">Höhe der Zeichenfläche in px.</param>
public sealed record DialogGraphModel(
    DialogSummary Dialog,
    IReadOnlyList<GraphNode> Nodes,
    IReadOnlyList<GraphEdge> Edges,
    IReadOnlyList<GraphLoopFrame> Loops,
    GraphScopeMarker? StartMarker,
    GraphScopeMarker? EndMarker,
    IReadOnlyList<GraphWarning> DialogWarnings,
    IReadOnlyList<TransitionDetail> OrphanTransitions,
    IReadOnlyList<TriggerDetail> OrphanTriggers,
    string Summary,
    double MinY,
    double Width,
    double Height)
{
    /// <summary>Alle Warnungen des Graphen, unabhängig davon, woran sie hängen.</summary>
    public IReadOnlyList<GraphWarning> AllWarnings =>
    [
        .. DialogWarnings,
        .. Nodes.SelectMany(node => node.Warnings),
        .. Edges.SelectMany(edge => edge.Warnings),
        .. Loops.SelectMany(loop => loop.Warnings),
    ];

    /// <summary>Findet einen Knoten anhand seiner Frage.</summary>
    /// <param name="questionId">Die gesuchte Frage.</param>
    /// <returns>Der Knoten oder <see langword="null"/>.</returns>
    public GraphNode? Node(Guid questionId)
        => Nodes.FirstOrDefault(node => node.QuestionId == questionId);

    /// <summary>Findet eine Kante anhand ihres Übergangs.</summary>
    /// <param name="transitionId">Der gesuchte Übergang.</param>
    /// <returns>Die Kante oder <see langword="null"/>.</returns>
    public GraphEdge? Edge(Guid transitionId)
        => Edges.FirstOrDefault(edge => edge.TransitionId == transitionId);
}
