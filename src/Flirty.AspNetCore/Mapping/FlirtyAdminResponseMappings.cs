using Flirty.AspNetCore.Dtos.Admin;
using Flirty.Runtime.Admin;

namespace Flirty.AspNetCore.Mapping;

/// <summary>
/// Interne Abbildungen der navigationsfreien <c>Flirty.Runtime.Admin</c>-Ergebnis-Records auf die
/// serialisierbaren Admin-Response-DTOs. Wie bei den Laufzeit-Mappings bewusst intern gehalten: nur die
/// DTOs bilden den öffentlichen Contract, das Mapping ist Implementierungsdetail der Endpunkte.
/// </summary>
internal static class FlirtyAdminResponseMappings
{
    public static DialogResponse ToResponse(this DialogSummary summary)
        => new(
            summary.Id,
            summary.Key,
            summary.Name,
            summary.Description,
            summary.Version,
            summary.IsPublished,
            summary.StartQuestionId,
            summary.CreatedAt,
            summary.UpdatedAt);

    public static DialogDetailResponse ToResponse(this DialogDetail detail)
        => new(
            detail.Dialog.Id,
            detail.Dialog.Key,
            detail.Dialog.Name,
            detail.Dialog.Description,
            detail.Dialog.Version,
            detail.Dialog.IsPublished,
            detail.Dialog.StartQuestionId,
            detail.Dialog.CreatedAt,
            detail.Dialog.UpdatedAt,
            [.. detail.Questions.Select(question => question.ToResponse())],
            [.. detail.Transitions.Select(transition => transition.ToResponse())],
            [.. detail.Loops.Select(loop => loop.ToResponse())],
            [.. detail.Triggers.Select(trigger => trigger.ToResponse())]);

    public static QuestionResponse ToResponse(this QuestionDetail question)
        => new(
            question.Id,
            question.DialogId,
            question.Key,
            question.Text,
            question.Type,
            question.Order,
            question.IsRequired,
            question.ValidationRules,
            [.. question.Options.Select(option => option.ToResponse())]);

    public static AnswerOptionResponse ToResponse(this AnswerOptionDetail option)
        => new(option.Id, option.QuestionId, option.Key, option.Label, option.Value, option.Order);

    public static TransitionResponse ToResponse(this TransitionDetail transition)
        => new(
            transition.Id,
            transition.DialogId,
            transition.FromQuestionId,
            transition.TargetQuestionId,
            transition.Expression,
            transition.Priority,
            transition.IsDefault);

    public static LoopResponse ToResponse(this LoopDetail loop)
        => new(loop.Id, loop.DialogId, loop.CollectionKey, loop.EntryQuestionId, loop.BreakingQuestionId);

    public static TriggerResponse ToResponse(this TriggerDetail trigger)
        => new(
            trigger.Id,
            trigger.DialogId,
            trigger.Scope,
            trigger.QuestionId,
            trigger.Kind,
            trigger.Config,
            trigger.Expression);
}
