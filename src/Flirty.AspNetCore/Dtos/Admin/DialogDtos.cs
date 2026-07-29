namespace Flirty.AspNetCore.Dtos.Admin;

/// <summary>
/// Request body for creating a dialog (<c>POST {prefix}/dialogs</c>). Version, publication
/// status and entry question are set server-side or defined subsequently via update.
/// </summary>
/// <param name="Key">The business, stable key of the dialog (must be unique).</param>
/// <param name="Name">The display name of the dialog.</param>
/// <param name="Description">The optional description of the dialog.</param>
public sealed record CreateDialogRequest(string Key, string Name, string? Description);

/// <summary>
/// Request body for changing a dialog (<c>PUT {prefix}/dialogs/{id}</c>).
/// </summary>
/// <param name="Key">The business, stable key of the dialog (must stay unique).</param>
/// <param name="Name">The display name of the dialog.</param>
/// <param name="Description">The optional description of the dialog.</param>
/// <param name="StartQuestionId">Optional reference to this dialog's entry question.</param>
public sealed record UpdateDialogRequest(string Key, string Name, string? Description, Guid? StartQuestionId);

/// <summary>
/// Response with a dialog's metadata (without the graph). Result of the dialog CRUD endpoints and the
/// dialog list.
/// </summary>
/// <param name="Id">The primary key of the dialog.</param>
/// <param name="Key">The business, stable key of the dialog.</param>
/// <param name="Name">The display name of the dialog.</param>
/// <param name="Description">The optional description of the dialog.</param>
/// <param name="Version">The version number of the dialog.</param>
/// <param name="IsPublished">Indicates whether the dialog is published (productively startable).</param>
/// <param name="StartQuestionId">Reference to the entry question or <see langword="null"/>.</param>
/// <param name="CreatedAt">Time of creation.</param>
/// <param name="UpdatedAt">Time of the last change.</param>
public sealed record DialogResponse(
    Guid Id,
    string Key,
    string Name,
    string? Description,
    int Version,
    bool IsPublished,
    Guid? StartQuestionId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Response with a dialog together with its graph managed in the admin CRUD (questions incl. options,
/// transitions, loop markers and triggers) as well as the stored canvas positions. Result of
/// <c>GET {prefix}/dialogs/{id}</c>.
/// </summary>
/// <param name="Id">The primary key of the dialog.</param>
/// <param name="Key">The business, stable key of the dialog.</param>
/// <param name="Name">The display name of the dialog.</param>
/// <param name="Description">The optional description of the dialog.</param>
/// <param name="Version">The version number of the dialog.</param>
/// <param name="IsPublished">Indicates whether the dialog is published (productively startable).</param>
/// <param name="StartQuestionId">Reference to the entry question or <see langword="null"/>.</param>
/// <param name="CreatedAt">Time of creation.</param>
/// <param name="UpdatedAt">Time of the last change.</param>
/// <param name="Questions">The questions of the dialog (incl. options), sorted by <c>Order</c>.</param>
/// <param name="Transitions">The transitions of the dialog, sorted by <c>Priority</c>.</param>
/// <param name="Loops">The loop markers of the dialog, sorted by <c>CollectionKey</c>.</param>
/// <param name="Triggers">The trigger definitions of the dialog, sorted by time and channel.</param>
/// <param name="Layout">
/// The stored canvas positions of the dialog, sorted by element kind and element id. Pure
/// display data of the designer; without a row the auto-layout arranges there.
/// </param>
public sealed record DialogDetailResponse(
    Guid Id,
    string Key,
    string Name,
    string? Description,
    int Version,
    bool IsPublished,
    Guid? StartQuestionId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<QuestionResponse> Questions,
    IReadOnlyList<TransitionResponse> Transitions,
    IReadOnlyList<LoopResponse> Loops,
    IReadOnlyList<TriggerResponse> Triggers,
    IReadOnlyList<DialogLayoutResponse> Layout);

/// <summary>
/// Response to <c>POST {prefix}/dialogs/{id}/abandon-sessions</c>: number of sessions set from
/// running to abandoned.
/// </summary>
/// <param name="DialogId">The dialog version whose sessions were ended.</param>
/// <param name="AbandonedSessions">The number of ended sessions (<c>0</c> if none were running).</param>
public sealed record AbandonSessionsResponse(Guid DialogId, int AbandonedSessions);
