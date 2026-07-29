namespace Flirty.AspNetCore.Dtos.Admin;

/// <summary>
/// Request body for creating a conditional transition
/// (<c>POST {prefix}/dialogs/{dialogId}/transitions</c>).
/// </summary>
/// <param name="FromQuestionId">Reference to the source question.</param>
/// <param name="TargetQuestionId">Reference to the target question.</param>
/// <param name="Expression">Optional condition expression; <see langword="null"/>/empty = unconditional.</param>
/// <param name="Priority">Priority for the evaluation order (smaller value = earlier).</param>
/// <param name="IsDefault">Indicates whether this transition is the default.</param>
public sealed record CreateTransitionRequest(
    Guid FromQuestionId,
    Guid TargetQuestionId,
    string? Expression,
    int Priority,
    bool IsDefault);

/// <summary>
/// Request body for changing a conditional transition
/// (<c>PUT {prefix}/dialogs/{dialogId}/transitions/{transitionId}</c>).
/// </summary>
/// <param name="FromQuestionId">Reference to the source question.</param>
/// <param name="TargetQuestionId">Reference to the target question.</param>
/// <param name="Expression">Optional condition expression; <see langword="null"/>/empty = unconditional.</param>
/// <param name="Priority">Priority for the evaluation order (smaller value = earlier).</param>
/// <param name="IsDefault">Indicates whether this transition is the default.</param>
public sealed record UpdateTransitionRequest(
    Guid FromQuestionId,
    Guid TargetQuestionId,
    string? Expression,
    int Priority,
    bool IsDefault);

/// <summary>
/// Response with a conditional transition (branching).
/// </summary>
/// <param name="Id">The primary key of the transition.</param>
/// <param name="DialogId">The foreign key to the associated dialog.</param>
/// <param name="FromQuestionId">Reference to the source question.</param>
/// <param name="TargetQuestionId">Reference to the target question.</param>
/// <param name="Expression">Optional condition expression; <see langword="null"/>/empty = unconditional.</param>
/// <param name="Priority">Priority for the evaluation order (smaller value = earlier).</param>
/// <param name="IsDefault">Indicates whether this transition is the default.</param>
public sealed record TransitionResponse(
    Guid Id,
    Guid DialogId,
    Guid FromQuestionId,
    Guid TargetQuestionId,
    string? Expression,
    int Priority,
    bool IsDefault);
