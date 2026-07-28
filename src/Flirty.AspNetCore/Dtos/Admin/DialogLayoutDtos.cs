using Flirty.Domain;

namespace Flirty.AspNetCore.Dtos.Admin;

/// <summary>
/// Ein einzelner Eintrag im Layout-Batch: die Position eines Elements auf dem Graph-Canvas.
/// </summary>
/// <param name="ElementKind">Die Art des positionierten Elements.</param>
/// <param name="ElementId">Verweis auf das Element (heute stets eine Frage-Id).</param>
/// <param name="X">Die waagerechte Canvas-Koordinate in px; darf nicht negativ sein.</param>
/// <param name="Y">Die senkrechte Canvas-Koordinate in px; darf nicht negativ sein.</param>
public sealed record DialogLayoutEntryRequest(
    LayoutElementKind ElementKind,
    Guid ElementId,
    int X,
    int Y);

/// <summary>
/// Anfrage-Körper zum Setzen von Canvas-Positionen
/// (<c>PUT {prefix}/dialogs/{dialogId}/layout</c>). <b>Merge, kein Ersatz:</b> Genannte Elemente werden
/// angelegt bzw. aktualisiert, nicht genannte bleiben unangetastet. Zum vollständigen Verwerfen dient
/// <c>DELETE {prefix}/dialogs/{dialogId}/layout</c>.
/// </summary>
/// <param name="Entries">Die zu setzenden Positionen; mindestens eine, je Element höchstens eine.</param>
public sealed record SetDialogLayoutRequest(IReadOnlyList<DialogLayoutEntryRequest> Entries);

/// <summary>
/// Antwort mit einer gespeicherten Canvas-Position.
/// </summary>
/// <param name="Id">Der Primärschlüssel der Layout-Zeile.</param>
/// <param name="DialogId">Der Fremdschlüssel auf den zugehörigen Dialog.</param>
/// <param name="ElementKind">Die Art des positionierten Elements.</param>
/// <param name="ElementId">Verweis auf das Element (heute stets eine Frage-Id).</param>
/// <param name="X">Die waagerechte Canvas-Koordinate in px.</param>
/// <param name="Y">Die senkrechte Canvas-Koordinate in px.</param>
public sealed record DialogLayoutResponse(
    Guid Id,
    Guid DialogId,
    LayoutElementKind ElementKind,
    Guid ElementId,
    int X,
    int Y);
