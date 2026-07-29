using System.ComponentModel.DataAnnotations;
using Flirty.Runtime.Admin;

namespace Flirty.Designer.Models;

/// <summary>
/// Form model of the answer-option editor (#39). Deliberately mutable (settable properties), so that
/// the Blazor <c>EditForm</c> can bind directly to it; the annotations mirror those of the
/// <c>CreateAnswerOptionCommand</c>/<c>UpdateAnswerOptionCommand</c>.
/// </summary>
/// <remarks>
/// The order index is not maintained in the form, but assigned via the sort buttons of the
/// options table.
/// </remarks>
internal sealed class AnswerOptionFormModel
{
    /// <summary>The domain, stable key of the option (must be unique within the question).</summary>
    [Required(ErrorMessage = "Bitte einen Schlüssel angeben.")]
    public string Key { get; set; } = string.Empty;

    /// <summary>The displayed label text of the option.</summary>
    [Required(ErrorMessage = "Bitte eine Beschriftung angeben.")]
    public string Label { get; set; } = string.Empty;

    /// <summary>The value of the option stored on selection (which the answer validator checks).</summary>
    [Required(ErrorMessage = "Bitte einen Wert angeben.")]
    public string Value { get; set; } = string.Empty;

    /// <summary>Creates a form model from an existing answer option.</summary>
    /// <param name="option">The option view from the admin CRUD.</param>
    /// <returns>The filled form model.</returns>
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
