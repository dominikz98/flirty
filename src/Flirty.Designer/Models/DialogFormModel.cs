using System.ComponentModel.DataAnnotations;
using Flirty.Runtime.Admin;

namespace Flirty.Designer.Models;

/// <summary>
/// Formular-Modell der Dialog-Editoren (#38). Bewusst veränderbar (settable Properties), damit die
/// Blazor-<c>EditForm</c> direkt daran binden kann; die Annotationen spiegeln die des
/// <c>CreateDialogCommand</c>/<c>UpdateDialogCommand</c>, damit Verstöße schon im Browser auffallen und
/// nicht erst im <c>ValidationPipelineBehavior</c> der Engine.
/// </summary>
internal sealed class DialogFormModel
{
    /// <summary>Der fachliche, stabile Schlüssel des Dialogs (muss eindeutig sein).</summary>
    [Required(ErrorMessage = "Bitte einen Schlüssel angeben.")]
    public string Key { get; set; } = string.Empty;

    /// <summary>Der Anzeigename des Dialogs.</summary>
    [Required(ErrorMessage = "Bitte einen Namen angeben.")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Die optionale Beschreibung des Dialogs.</summary>
    public string? Description { get; set; }

    /// <summary>Die optionale Einstiegsfrage des Dialogs (Voraussetzung zum Veröffentlichen).</summary>
    public Guid? StartQuestionId { get; set; }

    /// <summary>Erzeugt ein Formular-Modell aus den Metadaten eines bestehenden Dialogs.</summary>
    /// <param name="summary">Die Dialog-Metadaten.</param>
    /// <returns>Das befüllte Formular-Modell.</returns>
    public static DialogFormModel From(DialogSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        return new DialogFormModel
        {
            Key = summary.Key,
            Name = summary.Name,
            Description = summary.Description,
            StartQuestionId = summary.StartQuestionId,
        };
    }
}
