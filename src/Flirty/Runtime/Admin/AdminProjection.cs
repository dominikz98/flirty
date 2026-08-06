using Flirty.Domain;

namespace Flirty.Runtime.Admin;

/// <summary>
/// Maps the EF Core entities of the configuration aggregate to the navigation-free
/// admin projection records so that the tracked entities do not leave the handlers.
/// </summary>
internal static class AdminProjection
{
    /// <summary>Projects the metadata of a <see cref="Dialog"/> to a <see cref="DialogSummary"/>.</summary>
    /// <param name="dialog">The dialog to project.</param>
    /// <returns>The navigation-free metadata view.</returns>
    public static DialogSummary ToSummary(Dialog dialog)
        => new(
            dialog.Id,
            dialog.Key,
            dialog.Name,
            dialog.Description,
            dialog.Version,
            dialog.IsPublished,
            dialog.StartQuestionId,
            dialog.CreatedAt,
            dialog.UpdatedAt);

    /// <summary>
    /// Projects a <see cref="Dialog"/> along with its loaded graph (questions incl. options,
    /// transitions, loop markers, triggers and canvas layout) to a <see cref="DialogDetail"/>.
    /// Questions and options are sorted by <c>Order</c>, transitions by <c>Priority</c>, loops by
    /// <c>CollectionKey</c>, triggers by <c>Scope</c>/<c>Kind</c>/<c>Config</c> and layout rows by
    /// <c>ElementKind</c>/<c>ElementId</c> (triggers and layout have no order of their own –
    /// the sorting serves only a stable display).
    /// </summary>
    /// <param name="dialog">The dialog with loaded navigations.</param>
    /// <returns>The navigation-free detail view of the dialog graph.</returns>
    public static DialogDetail ToDetail(Dialog dialog)
        => new(
            ToSummary(dialog),
            [.. dialog.Questions.OrderBy(question => question.Order).Select(ToDetail)],
            [.. dialog.Transitions.OrderBy(transition => transition.Priority).Select(ToDetail)],
            [.. dialog.Loops.OrderBy(loop => loop.CollectionKey, StringComparer.Ordinal).Select(ToDetail)],
            [.. dialog.Triggers
                .OrderBy(trigger => trigger.Scope)
                .ThenBy(trigger => trigger.Kind)
                .ThenBy(trigger => trigger.Config, StringComparer.Ordinal)
                .Select(ToDetail)],
            ToDetail(dialog.Layout));

    /// <summary>Projects a <see cref="Question"/> (incl. options) to a <see cref="QuestionDetail"/>.</summary>
    /// <param name="question">The question to project with its loaded options.</param>
    /// <returns>The navigation-free question view.</returns>
    public static QuestionDetail ToDetail(Question question)
        => new(
            question.Id,
            question.DialogId,
            question.Key,
            question.Text,
            question.Type,
            question.CustomTypeKey,
            question.Order,
            question.IsRequired,
            question.ValidationRules,
            [.. question.Options.OrderBy(option => option.Order).Select(ToDetail)]);

    /// <summary>Projects an <see cref="AnswerOption"/> to an <see cref="AnswerOptionDetail"/>.</summary>
    /// <param name="option">The answer option to project.</param>
    /// <returns>The navigation-free option view.</returns>
    public static AnswerOptionDetail ToDetail(AnswerOption option)
        => new(option.Id, option.QuestionId, option.Key, option.Label, option.Value, option.Order);

    /// <summary>Projects a <see cref="Transition"/> to a <see cref="TransitionDetail"/>.</summary>
    /// <param name="transition">The transition to project.</param>
    /// <returns>The navigation-free transition view.</returns>
    public static TransitionDetail ToDetail(Transition transition)
        => new(
            transition.Id,
            transition.DialogId,
            transition.FromQuestionId,
            transition.TargetQuestionId,
            transition.Expression,
            transition.Priority,
            transition.IsDefault);

    /// <summary>Projects a <see cref="LoopDefinition"/> to a <see cref="LoopDetail"/>.</summary>
    /// <param name="loop">The loop marker to project.</param>
    /// <returns>The navigation-free loop view.</returns>
    public static LoopDetail ToDetail(LoopDefinition loop)
        => new(loop.Id, loop.DialogId, loop.CollectionKey, loop.EntryQuestionId, loop.BreakingQuestionId);

    /// <summary>Projects a <see cref="TriggerDefinition"/> to a <see cref="TriggerDetail"/>.</summary>
    /// <param name="trigger">The trigger definition to project.</param>
    /// <returns>The navigation-free trigger view.</returns>
    public static TriggerDetail ToDetail(TriggerDefinition trigger)
        => new(
            trigger.Id,
            trigger.DialogId,
            trigger.Scope,
            trigger.QuestionId,
            trigger.Kind,
            trigger.Config,
            trigger.Expression);

    /// <summary>Projects a <see cref="DialogLayout"/> row to a <see cref="DialogLayoutDetail"/>.</summary>
    /// <param name="layout">The layout row to project.</param>
    /// <returns>The navigation-free layout view.</returns>
    public static DialogLayoutDetail ToDetail(DialogLayout layout)
        => new(layout.Id, layout.DialogId, layout.ElementKind, layout.ElementId, layout.X, layout.Y);

    /// <summary>
    /// Sorts layout rows into a stable display order and projects them. The entity has
    /// no order of its own – the sorting serves only a repeatable output.
    /// </summary>
    /// <param name="layout">The layout rows to project.</param>
    /// <returns>The sorted, navigation-free layout view.</returns>
    public static IReadOnlyList<DialogLayoutDetail> ToDetail(IEnumerable<DialogLayout> layout)
        => [.. layout
            .OrderBy(entry => entry.ElementKind)
            .ThenBy(entry => entry.ElementId)
            .Select(ToDetail)];
}
