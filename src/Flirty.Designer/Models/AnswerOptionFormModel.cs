using System.ComponentModel.DataAnnotations;
using Flirty.Runtime.Admin;

namespace Flirty.Designer.Models;

/// <summary>
/// Formular-Modell des Antwortoptionen-Editors (#39). Bewusst veränderbar (settable Properties), damit
/// die Blazor-<c>EditForm</c> direkt daran binden kann; die Annotationen spiegeln die des
/// <c>CreateAnswerOptionCommand</c>/<c>UpdateAnswerOptionCommand</c>.
/// </summary>
/// <remarks>
/// Der Reihenfolge-Index wird nicht im Formular gepflegt, sondern über die Sortier-Schaltflächen der
/// Options-Tabelle vergeben.
/// </remarks>
internal sealed class AnswerOptionFormModel
{
    /// <summary>Der fachliche, stabile Schlüssel der Option (muss in der Frage eindeutig sein).</summary>
    [Required(ErrorMessage = "Bitte einen Schlüssel angeben.")]
    public string Key { get; set; } = string.Empty;

    /// <summary>Der angezeigte Beschriftungstext der Option.</summary>
    [Required(ErrorMessage = "Bitte eine Beschriftung angeben.")]
    public string Label { get; set; } = string.Empty;

    /// <summary>Der bei Auswahl gespeicherte Wert der Option (den prüft der Antwort-Validator).</summary>
    [Required(ErrorMessage = "Bitte einen Wert angeben.")]
    public string Value { get; set; } = string.Empty;

    /// <summary>Erzeugt ein Formular-Modell aus einer bestehenden Antwortoption.</summary>
    /// <param name="option">Die Options-Sicht aus dem Admin-CRUD.</param>
    /// <returns>Das befüllte Formular-Modell.</returns>
    public static AnswerOptionFormModel From(AnswerOptionDetail option)
    {
        ArgumentNullException.ThrowIfNull(option);

        return new AnswerOptionFormModel
        {
            Key = option.Key,
            Label = option.Label,
            Value = option.Value,
        };
    }
}
