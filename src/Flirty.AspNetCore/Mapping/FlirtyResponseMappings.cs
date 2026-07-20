using Flirty.AspNetCore.Dtos;
using Flirty.Runtime;

namespace Flirty.AspNetCore.Mapping;

/// <summary>
/// Interne Abbildungen der navigationsfreien <c>Flirty.Runtime</c>-Ergebnis-Records auf die
/// serialisierbaren HTTP-Response-DTOs des Pakets <c>Flirty.AspNetCore</c>. Bewusst intern gehalten:
/// nur die DTOs selbst bilden den öffentlichen Contract, das Mapping ist Implementierungsdetail der
/// Endpunkte.
/// </summary>
internal static class FlirtyResponseMappings
{
    public static StartSessionResponse ToResponse(this StartDialogResult result)
        => new(result.SessionId, result.IsResumed, result.CurrentQuestion.ToDto());

    public static SubmitAnswerResponse ToResponse(this SubmitAnswerResult result)
        => new(result.SessionId, result.IsCompleted, result.NextQuestion?.ToDto());

    public static EditAnswerResponse ToResponse(this EditAnswerResult result)
        => new(result.SessionId, result.IsCompleted, result.NextQuestion?.ToDto(), result.InvalidatedAnswers);

    public static SessionStateResponse ToResponse(this ResumeDialogResult result)
        => new(
            result.SessionId,
            result.Status,
            result.CurrentQuestion?.ToDto(),
            [.. result.Answers.Select(answer => answer.ToDto())]);

    public static QuestionDto ToDto(this QuestionView view)
        => new(view.Id, view.Key, view.Text, view.Type, [.. view.Options.Select(option => option.ToDto())]);

    public static AnswerOptionDto ToDto(this AnswerOptionView view)
        => new(view.Id, view.Key, view.Label, view.Value);

    public static SessionAnswerDto ToDto(this SessionAnswerView view)
        => new(
            view.QuestionId,
            view.QuestionKey,
            view.Value,
            view.Sequence,
            view.AnsweredAt,
            view.LoopInstanceId,
            view.IterationIndex);
}
