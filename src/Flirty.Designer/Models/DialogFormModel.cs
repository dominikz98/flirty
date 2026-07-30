using System.ComponentModel.DataAnnotations;
using Flirty.Runtime.Admin;

namespace Flirty.Designer.Models;

/// <summary>
/// Form model of the dialog editors (#38). Deliberately mutable (settable properties), so that the
/// Blazor <c>EditForm</c> can bind directly to it; the annotations mirror those of the
/// <c>CreateDialogCommand</c>/<c>UpdateDialogCommand</c>, so that violations already show in the browser and
/// not only in the engine's <c>ValidationPipelineBehavior</c>.
/// </summary>
internal sealed class DialogFormModel
{
    /// <summary>The stable, domain-level key of the dialog (must be unique).</summary>
    [Required(ErrorMessage = "Please enter a key.")]
    public string Key { get; set; } = string.Empty;

    /// <summary>The display name of the dialog.</summary>
    [Required(ErrorMessage = "Please enter a name.")]
    public string Name { get; set; } = string.Empty;

    /// <summary>The optional description of the dialog.</summary>
    public string? Description { get; set; }

    /// <summary>The optional entry question of the dialog (prerequisite for publishing).</summary>
    public Guid? StartQuestionId { get; set; }

    /// <summary>Creates a form model from the metadata of an existing dialog.</summary>
    /// <param name="summary">The dialog metadata.</param>
    /// <returns>The populated form model.</returns>
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
