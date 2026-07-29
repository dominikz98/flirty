using System.ComponentModel.DataAnnotations;
using Flirty.Domain;
using Flirty.Persistence;
using Mediator;

namespace Flirty.Runtime.Admin;

/// <summary>
/// Sets the canvas positions of the elements named in <see cref="Entries"/> of the dialog
/// <see cref="DialogId"/> – a <b>batch upsert</b>: existing rows are updated, missing ones
/// are created, <b>ones not named stay untouched</b>. For a full discard use
/// <see cref="ResetDialogLayoutCommand"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>This command deliberately does not run under <c>DialogEditGuard</c></b> – unlike the 16
/// graph commands. Coordinates do not touch the session semantics: sessions pin
/// <c>DialogId</c>/<c>DialogVersion</c> and follow Guids, not pixels. If the layout ran through the guard,
/// a <i>published</i> dialog could not even be arranged clearly, and every move would answer with 409.
/// Because <see cref="DialogLayout"/> is its own table, this is
/// no bypass of the publish lock (ADR 0005) but its edge – reasoned in ADR 0007.
/// </para>
/// <para>
/// <see cref="DialogLayoutEntry.ElementId"/> stays <b>unchecked</b>: the same convention as with the
/// FK-free question references of a <see cref="LoopDefinition"/>. Orphaned rows are cleaned up by
/// <c>DeleteQuestionCommand</c>.
/// </para>
/// <para>
/// The batch exists for the case that a gesture moves several elements: a drag gesture in the
/// designer must produce exactly <b>one</b> message, not one per element.
/// </para>
/// </remarks>
/// <param name="DialogId">The id of the dialog whose layout is set.</param>
/// <param name="Entries">The positions to set; at least one, at most one per element.</param>
public sealed record SetDialogLayoutCommand(
    Guid DialogId,
    IReadOnlyList<DialogLayoutEntry> Entries)
    : ICommand<IReadOnlyList<DialogLayoutDetail>>, IValidatableObject
{
    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Entries is null || Entries.Count == 0)
        {
            yield return new ValidationResult(
                "No position was passed – a layout batch needs at least one entry.",
                [nameof(Entries)]);
            yield break;
        }

        // A duplicate element in the same batch is not intent but a mistake of the caller:
        // which of the two positions should win is not determinable.
        if (Entries.Select(entry => (entry.ElementKind, entry.ElementId)).Distinct().Count() != Entries.Count)
        {
            yield return new ValidationResult(
                "An element occurs multiple times in the batch – at most one position per element is allowed.",
                [nameof(Entries)]);
        }

        if (Entries.Any(entry => entry.X < 0 || entry.Y < 0))
        {
            yield return new ValidationResult(
                "Canvas coordinates must not be negative – the origin is at the top left.",
                [nameof(Entries)]);
        }
    }
}

/// <summary>Handler for <see cref="SetDialogLayoutCommand"/>.</summary>
internal sealed class SetDialogLayoutCommandHandler
    : ICommandHandler<SetDialogLayoutCommand, IReadOnlyList<DialogLayoutDetail>>
{
    private readonly IDialogAdminStore _store;

    /// <summary>Creates the handler over the given <see cref="IDialogAdminStore"/>.</summary>
    /// <param name="store">The writing repository for the configuration graph.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is <see langword="null"/>.</exception>
    public SetDialogLayoutCommandHandler(IDialogAdminStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    /// <exception cref="ConfigurationNotFoundException">No dialog with the given id exists.</exception>
    public async ValueTask<IReadOnlyList<DialogLayoutDetail>> Handle(
        SetDialogLayoutCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // The dialog must exist – but it may be published. There is deliberately NO
        // DialogEditGuard here; the reasoning stands at the command and in ADR 0007.
        _ = await _store.GetDialogAsync(command.DialogId, cancellationToken)
            ?? throw ConfigurationNotFoundException.ForDialog(command.DialogId);

        var existing = await _store.GetLayoutAsync(command.DialogId, cancellationToken);
        var byElement = existing.ToDictionary(row => (row.ElementKind, row.ElementId));

        foreach (var entry in command.Entries)
        {
            if (byElement.TryGetValue((entry.ElementKind, entry.ElementId), out var row))
            {
                row.X = entry.X;
                row.Y = entry.Y;
                continue;
            }

            var created = new DialogLayout
            {
                Id = Guid.NewGuid(),
                DialogId = command.DialogId,
                ElementKind = entry.ElementKind,
                ElementId = entry.ElementId,
                X = entry.X,
                Y = entry.Y,
            };

            byElement[(entry.ElementKind, entry.ElementId)] = created;
            _store.Add(created);
        }

        await _store.SaveChangesAsync(cancellationToken);

        // Return the full layout, not only the set rows: the caller can replace its
        // state with it instead of merging it itself.
        return AdminProjection.ToDetail(byElement.Values);
    }
}
