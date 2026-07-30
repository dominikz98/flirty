using Flirty.Domain;

namespace Flirty.AspNetCore.Dtos.Admin;

/// <summary>
/// Request body for creating a question in a dialog
/// (<c>POST {prefix}/dialogs/{dialogId}/questions</c>).
/// </summary>
/// <param name="Key">The business, stable key of the question (unique within the dialog).</param>
/// <param name="Text">The displayed question text.</param>
/// <param name="Type">The answer type of the question.</param>
/// <param name="Order">The order index of the question within the dialog.</param>
/// <param name="IsRequired">Indicates whether an answer is required.</param>
/// <param name="ValidationRules">Optional validation rules as JSON.</param>
public sealed record CreateQuestionRequest(
    string Key,
    string Text,
    QuestionType Type,
    int Order,
    bool IsRequired,
    string? ValidationRules);

/// <summary>
/// Request body for changing a question
/// (<c>PUT {prefix}/dialogs/{dialogId}/questions/{questionId}</c>).
/// </summary>
/// <param name="Key">The business, stable key of the question (unique within the dialog).</param>
/// <param name="Text">The displayed question text.</param>
/// <param name="Type">The answer type of the question.</param>
/// <param name="Order">The order index of the question within the dialog.</param>
/// <param name="IsRequired">Indicates whether an answer is required.</param>
/// <param name="ValidationRules">Optional validation rules as JSON.</param>
public sealed record UpdateQuestionRequest(
    string Key,
    string Text,
    QuestionType Type,
    int Order,
    bool IsRequired,
    string? ValidationRules);

/// <summary>
/// Response with a question together with its answer options.
/// </summary>
/// <param name="Id">The primary key of the question.</param>
/// <param name="DialogId">The foreign key to the associated dialog.</param>
/// <param name="Key">The business, stable key of the question.</param>
/// <param name="Text">The displayed question text.</param>
/// <param name="Type">The answer type of the question.</param>
/// <param name="Order">The order index of the question within the dialog.</param>
/// <param name="IsRequired">Indicates whether an answer is required.</param>
/// <param name="ValidationRules">Optional validation rules as JSON.</param>
/// <param name="Options">The answer options of the question, sorted by <c>Order</c>.</param>
public sealed record QuestionResponse(
    Guid Id,
    Guid DialogId,
    string Key,
    string Text,
    QuestionType Type,
    int Order,
    bool IsRequired,
    string? ValidationRules,
    IReadOnlyList<AnswerOptionResponse> Options);
