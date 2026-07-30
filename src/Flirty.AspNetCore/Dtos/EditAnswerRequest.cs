namespace Flirty.AspNetCore.Dtos;

/// <summary>
/// Request body for <c>PUT /flirty/sessions/{id}/answers/{questionId}</c>: overwrites the answer already
/// given to an earlier question. Session id and question id come from the route; this body carries the new
/// value and optionally the loop iteration. Mapped onto the
/// <see cref="Flirty.Runtime.EditAnswerCommand"/>.
/// </summary>
/// <param name="Value">
/// The new answer value as raw JSON text (format depending on the question type, e.g. the value of a
/// selection option).
/// </param>
/// <param name="IterationIndex">
/// Optional zero-based iteration index, to edit the answer of a specific iteration within a loop;
/// <see langword="null"/> edits the earliest answer of the question.
/// </param>
public sealed record EditAnswerRequest(string Value, int? IterationIndex = null);
