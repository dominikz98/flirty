using System.ComponentModel.DataAnnotations;
using Flirty.Runtime.Admin;

namespace Flirty.Designer.Models;

/// <summary>
/// Formular-Modell des Branching-Editors (#40) – für das Anlegen eines Übergangs im Dialog-Editor und
/// für seine Detailseite. Bewusst veränderbar (settable Properties), damit die Blazor-<c>EditForm</c>
/// direkt daran binden kann.
/// </summary>
/// <remarks>
/// Die Frage-Verweise sind <see cref="Guid"/>? statt <see cref="Guid"/>, damit ein
/// <c>InputSelect</c> ohne Vorauswahl an sie binden kann und <see cref="RequiredAttribute"/> greift –
/// bei <c>Guid.Empty</c> wäre die Pflichtprüfung wirkungslos. Der Bedingungsausdruck wird hier
/// <b>nicht</b> geprüft: das übernimmt der <c>IExpressionEvaluator</c> gegen den Musterkontext
/// (<see cref="Flirty.Designer.Services.DesignerExpressionContext"/>).
/// </remarks>
internal sealed class TransitionFormModel
{
    /// <summary>Verweis auf die Ausgangsfrage des Übergangs.</summary>
    [Required(ErrorMessage = "Bitte eine Ausgangsfrage wählen.")]
    public Guid? FromQuestionId { get; set; }

    /// <summary>Verweis auf die Zielfrage des Übergangs.</summary>
    [Required(ErrorMessage = "Bitte eine Zielfrage wählen.")]
    public Guid? TargetQuestionId { get; set; }

    /// <summary>Der Bedingungsausdruck; leer bedeutet „bedingungslos zutreffend".</summary>
    public string? Expression { get; set; }

    /// <summary>Gibt an, ob dieser Übergang der Default ist (greift, wenn keine Bedingung zutrifft).</summary>
    public bool IsDefault { get; set; }

    /// <summary>Erzeugt ein Formular-Modell aus einem bestehenden Übergang.</summary>
    /// <param name="transition">Die Übergangs-Sicht aus dem Admin-CRUD.</param>
    /// <returns>Das befüllte Formular-Modell.</returns>
    public static TransitionFormModel From(TransitionDetail transition)
    {
        ArgumentNullException.ThrowIfNull(transition);

        return new TransitionFormModel
        {
            FromQuestionId = transition.FromQuestionId,
            TargetQuestionId = transition.TargetQuestionId,
            Expression = transition.Expression,
            IsDefault = transition.IsDefault,
        };
    }

    /// <summary>
    /// Normalisiert den Ausdruck für die Persistenz: Ein leerer/nur aus Leerraum bestehender Ausdruck
    /// wird zu <see langword="null"/> (bedingungslos), statt als leere Zeichenkette in der Spalte zu landen.
    /// </summary>
    /// <returns>Der zu speichernde Ausdruck oder <see langword="null"/>.</returns>
    public string? NormalizedExpression()
        => string.IsNullOrWhiteSpace(Expression) ? null : Expression.Trim();
}
