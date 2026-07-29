using Flirty.Domain;

namespace Flirty.AspNetCore.Dtos;

/// <summary>
/// Response body of <c>GET /flirty/sessions/{id}</c>: the current state of a session – status, the
/// (possibly) currently open question and the answers given so far – for restoring a survey.
/// Mapped from <see cref="Flirty.Runtime.ResumeDialogResult"/>.
/// </summary>
/// <param name="SessionId">The primary key of the queried session.</param>
/// <param name="Status">The current lifecycle status of the session.</param>
/// <param name="CurrentQuestion">
/// The currently open question or <see langword="null"/> if the session no longer has an open question
/// (completed or abandoned).
/// </param>
/// <param name="Answers">
/// The answers given so far in ascending order; empty if no answer has been recorded yet.
/// </param>
public sealed record SessionStateResponse(
    Guid SessionId,
    SessionStatus Status,
    QuestionDto? CurrentQuestion,
    IReadOnlyList<SessionAnswerDto> Answers);
