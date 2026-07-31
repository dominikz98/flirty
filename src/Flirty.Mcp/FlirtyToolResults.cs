using Flirty.Runtime.Admin;

namespace Flirty.Mcp;

// The tools serialize the core Flirty.Runtime[.Admin] records directly – Flirty.AspNetCore's DTO layer
// is deliberately not rebuilt. Half of it are …Request records that only exist because HTTP splits its
// input across route and body; a tool call is one flat argument object, so the tool method parameters
// ARE the request shape. The other half would be a field-for-field copy of records that are already
// public and fully documented.
//
// The wrappers below cover the only places where the core has no usable shape: Mediator.Unit (where
// HTTP answers 204) and the non-object returns. They exist because a non-object structuredContent is
// protocol-version dependent – wrapped as {"result": …} for clients before SEP-2106, bare afterwards –
// so every tool must return an object.

/// <summary>
/// Acknowledgement of a tool call whose command returns <c>Mediator.Unit</c> (where the HTTP surface
/// answers <c>204 No Content</c>).
/// </summary>
/// <param name="Succeeded">Always <see langword="true"/>: a failure arrives as an error result instead.</param>
internal sealed record FlirtyAck(bool Succeeded)
{
    /// <summary>The single acknowledgement instance.</summary>
    internal static FlirtyAck Instance { get; } = new(true);
}

/// <summary>The dialog list – the object wrapper around the result of <c>ListDialogsQuery</c>.</summary>
/// <param name="Dialogs">The configured dialogs, sorted by key and version.</param>
internal sealed record FlirtyDialogList(IReadOnlyList<DialogSummary> Dialogs);

/// <summary>
/// The number of running sessions on a dialog version – the object wrapper around the
/// <see cref="int"/> result of <c>CountActiveSessionsQuery</c>.
/// </summary>
/// <param name="DialogId">The dialog version that was counted.</param>
/// <param name="ActiveSessions">The number of sessions still in progress.</param>
internal sealed record FlirtyActiveSessionCount(Guid DialogId, int ActiveSessions);

/// <summary>
/// The stored canvas positions of a dialog – the object wrapper around the array result of
/// <c>SetDialogLayoutCommand</c>, returned by <c>flirty_layout_set</c>.
/// </summary>
/// <param name="Entries">The layout rows, sorted by element kind and element id.</param>
internal sealed record FlirtyDialogLayout(IReadOnlyList<DialogLayoutDetail> Entries);
