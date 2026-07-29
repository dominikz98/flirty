namespace Flirty.AspNetCore.Dtos.Admin;

/// <summary>
/// Request body for creating a loop marker
/// (<c>POST {prefix}/dialogs/{dialogId}/loops</c>). The cycle itself arises from the transitions –
/// the marker only adds the metadata layer on top.
/// </summary>
/// <param name="CollectionKey">Key under which the answers collected per iteration live in the expression context.</param>
/// <param name="EntryQuestionId">Reference to the entry question of the loop (target of the loop-back transition).</param>
/// <param name="BreakingQuestionId">Reference to the breaking question (whose exit transition leaves the cycle).</param>
public sealed record CreateLoopRequest(string CollectionKey, Guid EntryQuestionId, Guid BreakingQuestionId);

/// <summary>
/// Request body for changing a loop marker
/// (<c>PUT {prefix}/dialogs/{dialogId}/loops/{loopId}</c>).
/// </summary>
/// <param name="CollectionKey">Key under which the answers collected per iteration live in the expression context.</param>
/// <param name="EntryQuestionId">Reference to the entry question of the loop.</param>
/// <param name="BreakingQuestionId">Reference to the breaking question.</param>
public sealed record UpdateLoopRequest(string CollectionKey, Guid EntryQuestionId, Guid BreakingQuestionId);

/// <summary>
/// Response with a loop marker.
/// </summary>
/// <param name="Id">The primary key of the loop definition.</param>
/// <param name="DialogId">The foreign key to the associated dialog.</param>
/// <param name="CollectionKey">Key under which the answers collected per iteration live in the expression context.</param>
/// <param name="EntryQuestionId">Reference to the entry question of the loop.</param>
/// <param name="BreakingQuestionId">Reference to the breaking question.</param>
public sealed record LoopResponse(
    Guid Id,
    Guid DialogId,
    string CollectionKey,
    Guid EntryQuestionId,
    Guid BreakingQuestionId);
