using Flirty.Domain;

namespace Flirty.Runtime.Admin;

/// <summary>
/// Bildet die EF-Core-Entities des Konfigurations-Aggregats auf die navigationsfreien
/// Admin-Projektions-Records ab, damit die getrackten Entities die Handler nicht verlassen.
/// </summary>
internal static class AdminProjection
{
    /// <summary>Projiziert die Metadaten eines <see cref="Dialog"/> auf eine <see cref="DialogSummary"/>.</summary>
    /// <param name="dialog">Der zu projizierende Dialog.</param>
    /// <returns>Die navigationsfreie Metadaten-Sicht.</returns>
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
    /// Projiziert einen <see cref="Dialog"/> samt geladenem Graphen (Fragen inkl. Optionen,
    /// Übergänge, Schleifen-Marker, Trigger und Canvas-Layout) auf ein <see cref="DialogDetail"/>.
    /// Fragen und Optionen werden nach <c>Order</c>, Übergänge nach <c>Priority</c>, Schleifen nach
    /// <c>CollectionKey</c>, Trigger nach <c>Scope</c>/<c>Kind</c>/<c>Config</c> und Layout-Zeilen nach
    /// <c>ElementKind</c>/<c>ElementId</c> sortiert (Trigger und Layout haben keine eigene Reihenfolge –
    /// die Sortierung dient nur einer stabilen Anzeige).
    /// </summary>
    /// <param name="dialog">Der Dialog mit geladenen Navigationen.</param>
    /// <returns>Die navigationsfreie Detail-Sicht des Dialog-Graphen.</returns>
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

    /// <summary>Projiziert eine <see cref="Question"/> (inkl. Optionen) auf ein <see cref="QuestionDetail"/>.</summary>
    /// <param name="question">Die zu projizierende Frage mit geladenen Optionen.</param>
    /// <returns>Die navigationsfreie Frage-Sicht.</returns>
    public static QuestionDetail ToDetail(Question question)
        => new(
            question.Id,
            question.DialogId,
            question.Key,
            question.Text,
            question.Type,
            question.Order,
            question.IsRequired,
            question.ValidationRules,
            [.. question.Options.OrderBy(option => option.Order).Select(ToDetail)]);

    /// <summary>Projiziert eine <see cref="AnswerOption"/> auf ein <see cref="AnswerOptionDetail"/>.</summary>
    /// <param name="option">Die zu projizierende Antwortoption.</param>
    /// <returns>Die navigationsfreie Options-Sicht.</returns>
    public static AnswerOptionDetail ToDetail(AnswerOption option)
        => new(option.Id, option.QuestionId, option.Key, option.Label, option.Value, option.Order);

    /// <summary>Projiziert einen <see cref="Transition"/> auf ein <see cref="TransitionDetail"/>.</summary>
    /// <param name="transition">Der zu projizierende Übergang.</param>
    /// <returns>Die navigationsfreie Übergangs-Sicht.</returns>
    public static TransitionDetail ToDetail(Transition transition)
        => new(
            transition.Id,
            transition.DialogId,
            transition.FromQuestionId,
            transition.TargetQuestionId,
            transition.Expression,
            transition.Priority,
            transition.IsDefault);

    /// <summary>Projiziert eine <see cref="LoopDefinition"/> auf ein <see cref="LoopDetail"/>.</summary>
    /// <param name="loop">Der zu projizierende Schleifen-Marker.</param>
    /// <returns>Die navigationsfreie Schleifen-Sicht.</returns>
    public static LoopDetail ToDetail(LoopDefinition loop)
        => new(loop.Id, loop.DialogId, loop.CollectionKey, loop.EntryQuestionId, loop.BreakingQuestionId);

    /// <summary>Projiziert eine <see cref="TriggerDefinition"/> auf ein <see cref="TriggerDetail"/>.</summary>
    /// <param name="trigger">Die zu projizierende Trigger-Definition.</param>
    /// <returns>Die navigationsfreie Trigger-Sicht.</returns>
    public static TriggerDetail ToDetail(TriggerDefinition trigger)
        => new(
            trigger.Id,
            trigger.DialogId,
            trigger.Scope,
            trigger.QuestionId,
            trigger.Kind,
            trigger.Config,
            trigger.Expression);

    /// <summary>Projiziert eine <see cref="DialogLayout"/>-Zeile auf ein <see cref="DialogLayoutDetail"/>.</summary>
    /// <param name="layout">Die zu projizierende Layout-Zeile.</param>
    /// <returns>Die navigationsfreie Layout-Sicht.</returns>
    public static DialogLayoutDetail ToDetail(DialogLayout layout)
        => new(layout.Id, layout.DialogId, layout.ElementKind, layout.ElementId, layout.X, layout.Y);

    /// <summary>
    /// Sortiert Layout-Zeilen in eine stabile Anzeigereihenfolge und projiziert sie. Die Entity kennt
    /// keine eigene Reihenfolge – die Sortierung dient nur einer wiederholbaren Ausgabe.
    /// </summary>
    /// <param name="layout">Die zu projizierenden Layout-Zeilen.</param>
    /// <returns>Die sortierte, navigationsfreie Layout-Sicht.</returns>
    public static IReadOnlyList<DialogLayoutDetail> ToDetail(IEnumerable<DialogLayout> layout)
        => [.. layout
            .OrderBy(entry => entry.ElementKind)
            .ThenBy(entry => entry.ElementId)
            .Select(ToDetail)];
}
