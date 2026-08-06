using Flirty.Persistence;
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

// The four database results below (#129) are not wrappers around a core record - the engine has no
// command for "is this database reachable?". They are this package's own shapes, and two of them carry
// an explicit Succeeded flag on purpose: for flirty_db_test_connection "no" is the ANSWER, not a
// failure, exactly as it is for the designer's ConnectionTestResult.

/// <summary>
/// The wire projection of a declared database target: everything a client needs to choose one, and
/// nothing that could identify the server it lives on.
/// </summary>
/// <param name="Name">The name to put in the route, e.g. <c>/mcp/staging</c>.</param>
/// <param name="Provider">The database provider of the target.</param>
/// <param name="Description">The host's optional description, or <see langword="null"/>.</param>
/// <param name="IsDefault">Whether a route without a <c>{target}</c> segment serves this target.</param>
internal sealed record FlirtyMcpTargetInfo(
    string Name,
    FlirtyDatabaseProvider Provider,
    string? Description,
    bool IsDefault);

/// <summary>The declared database targets, returned by <c>flirty_db_list_targets</c>.</summary>
/// <param name="Targets">The declared targets, ordered by name. Empty when the server runs single-database.</param>
/// <param name="Note">
/// A hint when <paramref name="Targets"/> is empty, so an empty list is not mistaken for a failure or
/// for a permission problem; <see langword="null"/> otherwise.
/// </param>
internal sealed record FlirtyTargetList(IReadOnlyList<FlirtyMcpTargetInfo> Targets, string? Note);

/// <summary>The outcome of <c>flirty_db_test_connection</c>.</summary>
/// <param name="Succeeded">Whether the database answered.</param>
/// <param name="Message">A human-readable result, including the provider's message on a failure.</param>
internal sealed record FlirtyConnectionTest(bool Succeeded, string Message);

/// <summary>The EF Core migrations not yet applied, returned by <c>flirty_db_pending_migrations</c>.</summary>
/// <param name="Pending">The pending migration ids in the order EF Core would apply them; empty when up to date.</param>
internal sealed record FlirtyPendingMigrations(IReadOnlyList<string> Pending);

/// <summary>The outcome of <c>flirty_db_migrate</c>.</summary>
/// <param name="Applied">
/// The migration ids that were pending before the call and have now been applied; empty when the
/// database was already up to date.
/// </param>
internal sealed record FlirtyMigrationsApplied(IReadOnlyList<string> Applied);

/// <summary>
/// One custom question type the host declared, as seen by a client. A deliberate projection of
/// <see cref="Flirty.Validation.FlirtyQuestionType"/> rather than the record itself: that one carries the
/// CLR <c>Type</c> of the registered validator, which is a server-side implementation detail and has no
/// business on the wire. Note that <c>internal</c> is no protection here – <c>System.Text.Json</c>
/// ignores accessibility, and every wrapper in this file reaches the client in full – so the guarantee is
/// this projection having no such member, checked against the serialized text by a test.
/// </summary>
/// <param name="Key">The key to pass as <c>customTypeKey</c> when authoring a question.</param>
/// <param name="DisplayName">The human-readable name the host gave the type.</param>
/// <param name="Sample">
/// An example answer as JSON, or <see langword="null"/> when the host declared none.
/// </param>
internal sealed record FlirtyQuestionTypeInfo(string Key, string DisplayName, string? Sample);

/// <summary>The declared custom question types, returned by <c>flirty_question_type_list</c>.</summary>
/// <param name="QuestionTypes">The declared types, ordered by key. Empty when the host declared none.</param>
/// <param name="Note">
/// A hint when <paramref name="QuestionTypes"/> is empty, so an empty list is not mistaken for a failure
/// or a permission problem; <see langword="null"/> otherwise.
/// </param>
internal sealed record FlirtyQuestionTypeList(
    IReadOnlyList<FlirtyQuestionTypeInfo> QuestionTypes, string? Note);
