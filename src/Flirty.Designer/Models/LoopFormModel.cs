using System.ComponentModel.DataAnnotations;
using Flirty.Designer.Services;
using Flirty.Runtime.Admin;

namespace Flirty.Designer.Models;

/// <summary>
/// Form model of the loop editor (#41) – for creating a loop in the dialog editor and for its detail
/// page. Deliberately mutable (settable properties), so the Blazor <c>EditForm</c> can bind directly to
/// it.
/// </summary>
/// <remarks>
/// The question references are <see cref="Guid"/>? instead of <see cref="Guid"/>, so an
/// <c>InputSelect</c> without a preselection can bind to them and <see cref="RequiredAttribute"/> takes
/// effect – with <see cref="Guid.Empty"/> the required check would be ineffective (the pattern from
/// <see cref="TransitionFormModel"/>). The <see cref="CollectionKey"/> must be a valid identifier,
/// because it is bound as a variable in the expression context (<c>skills.Count &gt; 0</c>).
/// </remarks>
internal sealed class LoopFormModel
{
    /// <summary>Key under which the answers collected per iteration are held in the expression context.</summary>
    [Required(ErrorMessage = "Please enter a collection key.")]
    [RegularExpression(
        "^[A-Za-z_][A-Za-z0-9_]*$",
        ErrorMessage = "Only letters, digits and underscore, not starting with a digit.")]
    public string CollectionKey { get; set; } = string.Empty;

    /// <summary>Reference to the entry question of the loop (target of the back-jump).</summary>
    [Required(ErrorMessage = "Please choose an entry question.")]
    public Guid? EntryQuestionId { get; set; }

    /// <summary>Reference to the breaking question (whose exit transition leaves the cycle).</summary>
    [Required(ErrorMessage = "Please choose a breaking question.")]
    public Guid? BreakingQuestionId { get; set; }

    /// <summary>Creates a form model from an existing loop marker.</summary>
    /// <param name="loop">The loop view from the admin CRUD.</param>
    /// <returns>The populated form model.</returns>
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
    /// Suggests a collection key from the key of the entry question: the key with the suffix <c>_list</c>
    /// (<c>skill</c> → <c>skill_list</c>, <c>topping</c> → <c>topping_list</c>). A plain suffix on purpose,
    /// not an <c>s</c>-pluralization: the latter produces nonsense for keys whose stem does not pluralize
    /// with <c>s</c>, while <c>_list</c> reads cleanly for any key. If the result is not a referenceable
    /// identifier or is already taken as a question/collection key, <b>nothing</b> is suggested on purpose
    /// – a silent fallback name would be harder to follow than an empty required field.
    /// </summary>
    /// <param name="entryQuestionKey">The key of the entry question.</param>
    /// <param name="detail">The dialog including its graph, against which collisions are checked.</param>
    /// <returns>The suggestion or an empty string.</returns>
    public static string SuggestCollectionKey(string entryQuestionKey, DialogDetail detail)
    {
        ArgumentNullException.ThrowIfNull(detail);

        var candidate = $"{entryQuestionKey}_list";

        if (!DesignerExpressionContext.IsBindable(candidate)
            || detail.Questions.Any(question => string.Equals(question.Key, candidate, StringComparison.Ordinal))
            || detail.Loops.Any(loop => string.Equals(loop.CollectionKey, candidate, StringComparison.Ordinal)))
        {
            return string.Empty;
        }

        return candidate;
    }
}
