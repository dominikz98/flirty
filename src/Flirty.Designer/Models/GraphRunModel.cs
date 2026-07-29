using Flirty.Domain;

namespace Flirty.Designer.Models;

/// <summary>Eine im Testlauf gegebene Antwort – der Inhalt eines besuchten Knotens.</summary>
/// <param name="Sequence">Die fortlaufende Position innerhalb der Session (Identität für das Editieren).</param>
/// <param name="IterationIndex">
/// Der nullbasierte Iterationsindex innerhalb einer Schleife oder <see langword="null"/> außerhalb.
/// </param>
/// <param name="Value">Der gespeicherte rohe JSON-Antwortwert – das, womit die Bedingungen rechnen.</param>
/// <param name="Display">Der lesbare Wert (Options-Beschriftung statt Rohwert, <c>true</c> → „Ja“).</param>
/// <param name="AnsweredAt">Der Zeitpunkt der Erfassung.</param>
public sealed record GraphRunAnswer(
    int Sequence,
    int? IterationIndex,
    string Value,
    string Display,
    DateTimeOffset AnsweredAt);

/// <summary>
/// Ein im Lauf besuchter Knoten: die Frage samt <b>allen</b> Antworten, die in diesem Lauf auf sie
/// gegeben wurden – innerhalb einer Schleife also eine je Iteration.
/// </summary>
/// <param name="QuestionId">Die besuchte Frage.</param>
/// <param name="Answers">Die Antworten in der Reihenfolge ihrer <see cref="GraphRunAnswer.Sequence"/>.</param>
/// <param name="IsCurrent">
/// Ob die Frage gerade offen ist. Das ist unabhängig von <see cref="Answers"/>: Die Einstiegsfrage ist
/// offen, bevor sie beantwortet wurde, und eine Schleifenfrage ist in der nächsten Iteration erneut offen.
/// </param>
public sealed record GraphRunVisit(
    Guid QuestionId,
    IReadOnlyList<GraphRunAnswer> Answers,
    bool IsCurrent);

/// <summary>Eine im Lauf gegriffene Kante.</summary>
/// <remarks>
/// <see cref="IsAmbiguous"/> ist der ehrliche Teil: Die Engine hält nicht fest, <b>welcher</b> Übergang
/// gegriffen hat (<c>SessionAnswer</c> trägt keine <c>TransitionId</c>). Abgeleitet wird der Pfad aus der
/// Antwortfolge, und die kennt nur das Fragenpaar. Gibt es zwischen denselben zwei Fragen mehrere
/// Übergänge, sind sie damit nicht unterscheidbar – dann sind alle markiert und alle als mehrdeutig
/// ausgewiesen, statt einen davon zu behaupten.
/// </remarks>
/// <param name="TransitionId">Der Übergang.</param>
/// <param name="Count">Wie oft das zugehörige Fragenpaar durchlaufen wurde (in Schleifen mehrfach).</param>
/// <param name="IsAmbiguous">Ob zwischen demselben Fragenpaar mehrere Übergänge liegen.</param>
public sealed record GraphRunEdgeUse(Guid TransitionId, int Count, bool IsAmbiguous);

/// <summary>Der Laufzustand einer Schleife – die Zahl am Bereichsrahmen.</summary>
/// <param name="LoopId">Der Schleifen-Marker.</param>
/// <param name="CollectionKey">Sein Sammel-Schlüssel.</param>
/// <param name="Iterations">
/// Die Zahl der Iterationen der <b>jüngsten</b> Schleifen-Instanz (dieselbe Auswahl wie im
/// Core-<c>LoopResolver</c>); <c>0</c>, solange die Schleife nicht betreten wurde.
/// </param>
/// <param name="IsActive">Ob die aktuell offene Frage im Bereich dieser Schleife liegt.</param>
/// <param name="Body">Die Fragen des Bereichs, in Dialog-Reihenfolge.</param>
public sealed record GraphRunLoopState(
    Guid LoopId,
    string CollectionKey,
    int Iterations,
    bool IsActive,
    IReadOnlyList<Guid> Body);

/// <summary>
/// Ein im Lauf <b>publiziertes</b> Trigger-Ereignis – die Anzeigeform eines Eintrags aus dem
/// <c>DesignerTriggerLog</c>.
/// </summary>
/// <param name="OccurredAt">Der Zeitpunkt der Beobachtung.</param>
/// <param name="Scope">Der zugeordnete Auslöse-Zeitpunkt.</param>
/// <param name="QuestionId">
/// Die auslösende Frage oder <see langword="null"/>, wenn das Ereignis an keiner Frage hängt (Abschluss)
/// bzw. die Frage nicht mehr zum Dialog gehört – dann wird es dialogweit gezeigt statt verschwiegen.
/// </param>
/// <param name="Label">Die kurze Beschriftung des Chips.</param>
/// <param name="Title">Die vollständige Beschreibung für Tooltip und Screenreader.</param>
/// <param name="Detail">Die Kurzbeschreibung des Ereignisses (wie im Protokoll der Listenansicht).</param>
/// <param name="IsFresh">
/// Ob das Ereignis aus dem <b>letzten</b> Schritt stammt. Trägt das kurze Aufblitzen am auslösenden
/// Knoten; die Chips bleiben danach stehen.
/// </param>
public sealed record GraphRunTrigger(
    DateTimeOffset OccurredAt,
    TriggerScope Scope,
    Guid? QuestionId,
    string Label,
    string Title,
    string Detail,
    bool IsFresh);

/// <summary>
/// Der Laufzustand über dem Zeichenmodell (#104): besuchte Knoten, gegriffene Kanten, Iterationszahlen
/// und publizierte Trigger – die Antwort auf „welchen Weg nimmt der Dialog?“.
/// </summary>
/// <remarks>
/// <para>
/// Bewusst ein <b>eigenes</b> Modell neben <see cref="DialogGraphModel"/> statt zusätzlicher Felder
/// darin: Die Editor-Ansicht (#101–#103) kennt keinen Lauf, und der Laufzustand wechselt bei jedem
/// Schritt, während das Zeichenmodell nur bei einer Graph-Änderung neu entsteht. Gemeinsam sind allein
/// die Schlüssel – Frage-, Übergangs- und Schleifen-Ids.
/// </para>
/// <para>
/// Gebaut wird es von <see cref="Flirty.Designer.Services.GraphRunAnalyzer"/> nach jedem Engine-Schritt.
/// </para>
/// </remarks>
/// <param name="Status">Der Status der Session.</param>
/// <param name="CurrentQuestionId">Die aktuell offene Frage oder <see langword="null"/>.</param>
/// <param name="Visits">Die besuchten Knoten in der Reihenfolge ihres ersten Besuchs.</param>
/// <param name="TakenEdges">Die gegriffenen Kanten.</param>
/// <param name="Loops">Der Laufzustand je Schleifen-Marker, in der Reihenfolge von <c>DialogDetail.Loops</c>.</param>
/// <param name="Triggers">Die publizierten Ereignisse in chronologischer Reihenfolge.</param>
/// <param name="Summary">Der Lauf in Worten – die Alternative zum Bild (Screenreader).</param>
public sealed record GraphRunOverlay(
    SessionStatus Status,
    Guid? CurrentQuestionId,
    IReadOnlyList<GraphRunVisit> Visits,
    IReadOnlyList<GraphRunEdgeUse> TakenEdges,
    IReadOnlyList<GraphRunLoopState> Loops,
    IReadOnlyList<GraphRunTrigger> Triggers,
    string Summary)
{
    /// <summary>Die Zahl der bisher erfassten Antworten – die Schrittzahl des Laufs.</summary>
    public int Steps => Visits.Sum(visit => visit.Answers.Count);

    /// <summary>Findet den Besuch einer Frage.</summary>
    /// <param name="questionId">Die gesuchte Frage.</param>
    /// <returns>Der Besuch oder <see langword="null"/>, wenn die Frage im Lauf nicht vorkam.</returns>
    public GraphRunVisit? Visit(Guid questionId)
        => Visits.FirstOrDefault(visit => visit.QuestionId == questionId);

    /// <summary>Findet die Nutzung einer Kante.</summary>
    /// <param name="transitionId">Der gesuchte Übergang.</param>
    /// <returns>Die Nutzung oder <see langword="null"/>, wenn der Übergang nicht gegriffen hat.</returns>
    public GraphRunEdgeUse? Edge(Guid transitionId)
        => TakenEdges.FirstOrDefault(edge => edge.TransitionId == transitionId);

    /// <summary>Findet den Laufzustand eines Schleifen-Markers.</summary>
    /// <param name="loopId">Der gesuchte Marker.</param>
    /// <returns>Der Zustand oder <see langword="null"/>.</returns>
    public GraphRunLoopState? Loop(Guid loopId)
        => Loops.FirstOrDefault(loop => loop.LoopId == loopId);

    /// <summary>Die Ereignisse, die an einer bestimmten Frage hängen.</summary>
    /// <param name="questionId">Die Frage.</param>
    /// <returns>Die Ereignisse in chronologischer Reihenfolge.</returns>
    public IReadOnlyList<GraphRunTrigger> TriggersOf(Guid questionId)
        => [.. Triggers.Where(trigger => trigger.QuestionId == questionId)];

    /// <summary>Die Ereignisse ohne Frage-Bezug – Start und Abschluss des Dialogs.</summary>
    public IReadOnlyList<GraphRunTrigger> DialogTriggers
        => [.. Triggers.Where(trigger => trigger.QuestionId is null)];

    /// <summary>Die Schleifen, in deren Bereich eine Frage liegt.</summary>
    /// <param name="questionId">Die Frage.</param>
    /// <returns>Die Schleifen-Zustände.</returns>
    public IReadOnlyList<GraphRunLoopState> LoopsOf(Guid questionId)
        => [.. Loops.Where(loop => loop.Body.Contains(questionId))];
}
