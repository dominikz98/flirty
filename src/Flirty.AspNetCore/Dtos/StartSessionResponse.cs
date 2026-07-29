namespace Flirty.AspNetCore.Dtos;

/// <summary>
/// Response body of <c>POST /flirty/sessions</c>: the (newly created or resumed) session
/// together with the question currently to be answered. Mapped from <see cref="Flirty.Runtime.StartDialogResult"/>.
/// </summary>
/// <param name="SessionId">The primary key of the running session.</param>
/// <param name="IsResumed">
/// <see langword="true"/> if a session that was already running was resumed; <see langword="false"/>
/// for a fresh start.
/// </param>
/// <param name="CurrentQuestion">The currently open question to present to the user.</param>
public sealed record StartSessionResponse(Guid SessionId, bool IsResumed, QuestionDto CurrentQuestion);
