using System.ComponentModel.DataAnnotations;
using Flirty.Designer.Services;
using Flirty.Runtime.Admin;

namespace Flirty.Designer.Models;

/// <summary>
/// Formular-Modell des Loop-Editors (#41) – für das Anlegen einer Schleife im Dialog-Editor und für
/// ihre Detailseite. Bewusst veränderbar (settable Properties), damit die Blazor-<c>EditForm</c> direkt
/// daran binden kann.
/// </summary>
/// <remarks>
/// Die Frage-Verweise sind <see cref="Guid"/>? statt <see cref="Guid"/>, damit ein <c>InputSelect</c>
/// ohne Vorauswahl an sie binden kann und <see cref="RequiredAttribute"/> greift – bei
/// <see cref="Guid.Empty"/> wäre die Pflichtprüfung wirkungslos (Muster aus
/// <see cref="TransitionFormModel"/>). Der <see cref="CollectionKey"/> muss ein gültiger Bezeichner
/// sein, weil er im Ausdruckskontext als Variable gebunden wird (<c>skills.Count &gt; 0</c>).
/// </remarks>
internal sealed class LoopFormModel
{
    /// <summary>Schlüssel, unter dem die je Iteration gesammelten Antworten im Ausdruckskontext liegen.</summary>
    [Required(ErrorMessage = "Bitte einen Collection-Schlüssel angeben.")]
    [RegularExpression(
        "^[A-Za-z_][A-Za-z0-9_]*$",
        ErrorMessage = "Nur Buchstaben, Ziffern und Unterstrich, nicht mit einer Ziffer beginnend.")]
    public string CollectionKey { get; set; } = string.Empty;

    /// <summary>Verweis auf die Einstiegsfrage der Schleife (Ziel des Rücksprungs).</summary>
    [Required(ErrorMessage = "Bitte eine Einstiegsfrage wählen.")]
    public Guid? EntryQuestionId { get; set; }

    /// <summary>Verweis auf die Breaking Question (deren Exit-Übergang den Zyklus verlässt).</summary>
    [Required(ErrorMessage = "Bitte eine Breaking Question wählen.")]
    public Guid? BreakingQuestionId { get; set; }

    /// <summary>Erzeugt ein Formular-Modell aus einem bestehenden Schleifen-Marker.</summary>
    /// <param name="loop">Die Schleifen-Sicht aus dem Admin-CRUD.</param>
    /// <returns>Das befüllte Formular-Modell.</returns>
    public static LoopFormModel From(LoopDetail loop)
    {
        ArgumentNullException.ThrowIfNull(loop);

        return new LoopFormModel
        {
            CollectionKey = loop.CollectionKey,
            EntryQuestionId = loop.EntryQuestionId,
            BreakingQuestionId = loop.BreakingQuestionId,
        };
    }

    /// <summary>
    /// Schlägt zum Schlüssel der Einstiegsfrage einen Collection-Schlüssel vor: der Plural im Sinne der
    /// Doku (<c>skill</c> → <c>skills</c>, <c>position</c> → <c>positions</c>, vgl.
    /// <c>docs/LOOPS.md</c>). Ist das Ergebnis kein referenzierbarer Bezeichner oder bereits als
    /// Frage-/Collection-Schlüssel vergeben, wird bewusst <b>nichts</b> vorgeschlagen – ein stiller
    /// Ausweichname wäre schwerer nachzuvollziehen als ein leeres Pflichtfeld.
    /// </summary>
    /// <param name="entryQuestionKey">Der Schlüssel der Einstiegsfrage.</param>
    /// <param name="detail">Der Dialog samt Graph, gegen den auf Kollisionen geprüft wird.</param>
    /// <returns>Der Vorschlag oder eine leere Zeichenkette.</returns>
    public static string SuggestCollectionKey(string entryQuestionKey, DialogDetail detail)
    {
        ArgumentNullException.ThrowIfNull(detail);

        var candidate = $"{entryQuestionKey}s";

        if (!DesignerExpressionContext.IsBindable(candidate)
            || detail.Questions.Any(question => string.Equals(question.Key, candidate, StringComparison.Ordinal))
            || detail.Loops.Any(loop => string.Equals(loop.CollectionKey, candidate, StringComparison.Ordinal)))
        {
            return string.Empty;
        }

        return candidate;
    }
}
