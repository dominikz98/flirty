using System.ComponentModel.DataAnnotations;
using Flirty.Runtime.Admin;

namespace Flirty.Designer.Models;

/// <summary>
/// Form model of the branching editor (#40) – for creating a transition in the dialog editor and
/// for its detail page. Deliberately mutable (settable properties), so that the Blazor <c>EditForm</c>
/// can bind directly to it.
/// </summary>
/// <remarks>
/// The question references are <see cref="Guid"/>? instead of <see cref="Guid"/>, so that an
/// <c>InputSelect</c> without a preselection can bind to them and <see cref="RequiredAttribute"/> takes effect –
/// with <c>Guid.Empty</c> the required check would be ineffective. The condition expression is
/// <b>not</b> checked here: that is done by the <c>IExpressionEvaluator</c> against the sample context
/// (<see cref="Flirty.Designer.Services.DesignerExpressionContext"/>).
/// </remarks>
internal sealed class TransitionFormModel
{
    /// <summary>Reference to the source question of the transition.</summary>
    [Required(ErrorMessage = "Bitte eine Ausgangsfrage wählen.")]
    public Guid? FromQuestionId { get; set; }

    /// <summary>Reference to the target question of the transition.</summary>
    [Required(ErrorMessage = "Bitte eine Zielfrage wählen.")]
    public Guid? TargetQuestionId { get; set; }

    /// <summary>The condition expression; empty means "unconditionally matching".</summary>
    public string? Expression { get; set; }

    /// <summary>Indicates whether this transition is the default (takes effect when no condition matches).</summary>
    public bool IsDefault { get; set; }

    /// <summary>Creates a form model from an existing transition.</summary>
    /// <param name="transition">The transition view from the admin CRUD.</param>
    /// <returns>The filled form model.</returns>
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
    /// Normalizes the expression for persistence: an empty/whitespace-only expression
    /// becomes <see langword="null"/> (unconditional), instead of landing as an empty string in the column.
    /// </summary>
    /// <returns>The expression to store or <see langword="null"/>.</returns>
    public string? NormalizedExpression()
        => string.IsNullOrWhiteSpace(Expression) ? null : Expression.Trim();
}
