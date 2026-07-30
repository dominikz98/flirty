namespace Flirty.AspNetCore.Dtos.Admin;

/// <summary>
/// Request body for creating an answer option
/// (<c>POST {prefix}/dialogs/{dialogId}/questions/{questionId}/options</c>).
/// </summary>
/// <param name="Key">The business, stable key of the option (unique within the question).</param>
/// <param name="Label">The displayed label text of the option.</param>
/// <param name="Value">The value of the option stored on selection.</param>
/// <param name="Order">The order index of the option within the question.</param>
public sealed record CreateAnswerOptionRequest(string Key, string Label, string Value, int Order);

/// <summary>
/// Request body for changing an answer option
/// (<c>PUT {prefix}/dialogs/{dialogId}/questions/{questionId}/options/{optionId}</c>).
/// </summary>
/// <param name="Key">The business, stable key of the option (unique within the question).</param>
/// <param name="Label">The displayed label text of the option.</param>
/// <param name="Value">The value of the option stored on selection.</param>
/// <param name="Order">The order index of the option within the question.</param>
public sealed record UpdateAnswerOptionRequest(string Key, string Label, string Value, int Order);

/// <summary>
/// Response with an answer option.
/// </summary>
/// <param name="Id">The primary key of the answer option.</param>
/// <param name="QuestionId">The foreign key to the associated question.</param>
/// <param name="Key">The business, stable key of the option.</param>
/// <param name="Label">The displayed label text of the option.</param>
/// <param name="Value">The value of the option stored on selection.</param>
/// <param name="Order">The order index of the option within the question.</param>
public sealed record AnswerOptionResponse(
    Guid Id,
    Guid QuestionId,
    string Key,
    string Label,
    string Value,
    int Order);
