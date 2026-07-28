using System.ComponentModel.DataAnnotations;
using Flirty.Domain;
using Flirty.Persistence;
using Mediator;

namespace Flirty.Runtime.Admin;

/// <summary>
/// Setzt die Canvas-Positionen der in <see cref="Entries"/> genannten Elemente des Dialogs
/// <see cref="DialogId"/> – ein <b>Batch-Upsert</b>: Vorhandene Zeilen werden aktualisiert, fehlende
/// angelegt, <b>nicht genannte bleiben unangetastet</b>. Zum vollständigen Verwerfen dient
/// <see cref="ResetDialogLayoutCommand"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Dieser Command läuft bewusst nicht unter <c>DialogEditGuard</c></b> – anders als die 16
/// Graph-Commands. Koordinaten berühren die Session-Semantik nicht: Sessions pinnen
/// <c>DialogId</c>/<c>DialogVersion</c> und folgen Guids, nicht Pixeln. Liefe das Layout über den Guard,
/// ließe sich ein <i>veröffentlichter</i> Dialog nicht einmal übersichtlich anordnen, und jedes
/// Verschieben quittierte mit 409. Weil <see cref="DialogLayout"/> eine eigene Tabelle ist, ist das
/// keine Umgehung der Publish-Sperre (ADR 0005), sondern deren Grenze – begründet in ADR 0007.
/// </para>
/// <para>
/// <see cref="DialogLayoutEntry.ElementId"/> bleibt <b>ungeprüft</b>: dieselbe Konvention wie bei den
/// FK-losen Frage-Verweisen einer <see cref="LoopDefinition"/>. Verwaiste Zeilen räumt
/// <c>DeleteQuestionCommand</c> ab.
/// </para>
/// <para>
/// Der Batch existiert für den Fall, dass eine Geste mehrere Elemente verschiebt: Eine Zieh-Geste im
/// Designer darf genau <b>eine</b> Nachricht erzeugen, nicht eine je Element.
/// </para>
/// </remarks>
/// <param name="DialogId">Die Id des Dialogs, dessen Layout gesetzt wird.</param>
/// <param name="Entries">Die zu setzenden Positionen; mindestens eine, je Element höchstens eine.</param>
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
                "Es wurde keine Position übergeben – ein Layout-Batch braucht mindestens einen Eintrag.",
                [nameof(Entries)]);
            yield break;
        }

        // Ein doppeltes Element im selben Batch ist keine Absicht, sondern ein Fehler des Aufrufers:
        // Welche der beiden Positionen gewinnen soll, ist nicht bestimmbar.
        if (Entries.Select(entry => (entry.ElementKind, entry.ElementId)).Distinct().Count() != Entries.Count)
        {
            yield return new ValidationResult(
                "Ein Element kommt im Batch mehrfach vor – je Element ist höchstens eine Position zulässig.",
                [nameof(Entries)]);
        }

        if (Entries.Any(entry => entry.X < 0 || entry.Y < 0))
        {
            yield return new ValidationResult(
                "Canvas-Koordinaten dürfen nicht negativ sein – der Ursprung liegt oben links.",
                [nameof(Entries)]);
        }
    }
}

/// <summary>Handler für <see cref="SetDialogLayoutCommand"/>.</summary>
internal sealed class SetDialogLayoutCommandHandler
    : ICommandHandler<SetDialogLayoutCommand, IReadOnlyList<DialogLayoutDetail>>
{
    private readonly IDialogAdminStore _store;

    /// <summary>Erstellt den Handler über den angegebenen <see cref="IDialogAdminStore"/>.</summary>
    /// <param name="store">Das schreibende Repository für den Konfigurationsgraphen.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> ist <see langword="null"/>.</exception>
    public SetDialogLayoutCommandHandler(IDialogAdminStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    /// <exception cref="ConfigurationNotFoundException">Kein Dialog mit der angegebenen Id existiert.</exception>
    public async ValueTask<IReadOnlyList<DialogLayoutDetail>> Handle(
        SetDialogLayoutCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Der Dialog muss existieren – aber er darf veröffentlicht sein. Hier steht bewusst KEIN
        // DialogEditGuard; die Begründung steht am Command und in ADR 0007.
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

        // Das vollständige Layout zurückgeben, nicht nur die gesetzten Zeilen: Der Aufrufer kann seinen
        // Stand damit ersetzen, statt ihn selbst zusammenzuführen.
        return AdminProjection.ToDetail(byElement.Values);
    }
}
