using Flirty.Domain;

namespace Flirty.AspNetCore.Dtos.Admin;

/// <summary>
/// A single entry in the layout batch: the position of an element on the graph canvas.
/// </summary>
/// <param name="ElementKind">The kind of the positioned element.</param>
/// <param name="ElementId">Reference to the element (today always a question id).</param>
/// <param name="X">The horizontal canvas coordinate in px; must not be negative.</param>
/// <param name="Y">The vertical canvas coordinate in px; must not be negative.</param>
public sealed record DialogLayoutEntryRequest(
    LayoutElementKind ElementKind,
    Guid ElementId,
    int X,
    int Y);

/// <summary>
/// Request body for setting canvas positions
/// (<c>PUT {prefix}/dialogs/{dialogId}/layout</c>). <b>Merge, not replacement:</b> named elements are
/// created or updated, unnamed ones remain untouched. For a full discard use
/// <c>DELETE {prefix}/dialogs/{dialogId}/layout</c>.
/// </summary>
/// <param name="Entries">The positions to set; at least one, at most one per element.</param>
public sealed record SetDialogLayoutRequest(IReadOnlyList<DialogLayoutEntryRequest> Entries);

/// <summary>
/// Response with a stored canvas position.
/// </summary>
/// <param name="Id">The primary key of the layout row.</param>
/// <param name="DialogId">The foreign key to the associated dialog.</param>
/// <param name="ElementKind">The kind of the positioned element.</param>
/// <param name="ElementId">Reference to the element (today always a question id).</param>
/// <param name="X">The horizontal canvas coordinate in px.</param>
/// <param name="Y">The vertical canvas coordinate in px.</param>
public sealed record DialogLayoutResponse(
    Guid Id,
    Guid DialogId,
    LayoutElementKind ElementKind,
    Guid ElementId,
    int X,
    int Y);
