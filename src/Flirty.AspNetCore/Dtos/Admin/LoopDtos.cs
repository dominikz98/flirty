namespace Flirty.AspNetCore.Dtos.Admin;

/// <summary>
/// Anfrage-Körper zum Anlegen eines Schleifen-Markers
/// (<c>POST {prefix}/dialogs/{dialogId}/loops</c>). Der Zyklus selbst entsteht über die Übergänge –
/// der Marker legt nur die Metadaten-Ebene darüber.
/// </summary>
/// <param name="CollectionKey">Schlüssel, unter dem die je Iteration gesammelten Antworten im Ausdruckskontext liegen.</param>
/// <param name="EntryQuestionId">Verweis auf die Einstiegsfrage der Schleife (Ziel des Loop-Back-Übergangs).</param>
/// <param name="BreakingQuestionId">Verweis auf die Breaking Question (deren Exit-Übergang den Zyklus verlässt).</param>
public sealed record CreateLoopRequest(string CollectionKey, Guid EntryQuestionId, Guid BreakingQuestionId);

/// <summary>
/// Anfrage-Körper zum Ändern eines Schleifen-Markers
/// (<c>PUT {prefix}/dialogs/{dialogId}/loops/{loopId}</c>).
/// </summary>
/// <param name="CollectionKey">Schlüssel, unter dem die je Iteration gesammelten Antworten im Ausdruckskontext liegen.</param>
/// <param name="EntryQuestionId">Verweis auf die Einstiegsfrage der Schleife.</param>
/// <param name="BreakingQuestionId">Verweis auf die Breaking Question.</param>
public sealed record UpdateLoopRequest(string CollectionKey, Guid EntryQuestionId, Guid BreakingQuestionId);

/// <summary>
/// Antwort mit einem Schleifen-Marker.
/// </summary>
/// <param name="Id">Der Primärschlüssel der Schleifen-Definition.</param>
/// <param name="DialogId">Der Fremdschlüssel auf den zugehörigen Dialog.</param>
/// <param name="CollectionKey">Schlüssel, unter dem die je Iteration gesammelten Antworten im Ausdruckskontext liegen.</param>
/// <param name="EntryQuestionId">Verweis auf die Einstiegsfrage der Schleife.</param>
/// <param name="BreakingQuestionId">Verweis auf die Breaking Question.</param>
public sealed record LoopResponse(
    Guid Id,
    Guid DialogId,
    string CollectionKey,
    Guid EntryQuestionId,
    Guid BreakingQuestionId);
