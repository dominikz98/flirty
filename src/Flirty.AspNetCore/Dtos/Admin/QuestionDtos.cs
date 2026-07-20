using Flirty.Domain;

namespace Flirty.AspNetCore.Dtos.Admin;

/// <summary>
/// Anfrage-Körper zum Anlegen einer Frage in einem Dialog
/// (<c>POST {prefix}/dialogs/{dialogId}/questions</c>).
/// </summary>
/// <param name="Key">Der fachliche, stabile Schlüssel der Frage (im Dialog eindeutig).</param>
/// <param name="Text">Der angezeigte Fragetext.</param>
/// <param name="Type">Der Antworttyp der Frage.</param>
/// <param name="Order">Der Reihenfolge-Index der Frage innerhalb des Dialogs.</param>
/// <param name="IsRequired">Gibt an, ob eine Antwort erforderlich ist.</param>
/// <param name="ValidationRules">Optionale Validierungsregeln als JSON.</param>
public sealed record CreateQuestionRequest(
    string Key,
    string Text,
    QuestionType Type,
    int Order,
    bool IsRequired,
    string? ValidationRules);

/// <summary>
/// Anfrage-Körper zum Ändern einer Frage
/// (<c>PUT {prefix}/dialogs/{dialogId}/questions/{questionId}</c>).
/// </summary>
/// <param name="Key">Der fachliche, stabile Schlüssel der Frage (im Dialog eindeutig).</param>
/// <param name="Text">Der angezeigte Fragetext.</param>
/// <param name="Type">Der Antworttyp der Frage.</param>
/// <param name="Order">Der Reihenfolge-Index der Frage innerhalb des Dialogs.</param>
/// <param name="IsRequired">Gibt an, ob eine Antwort erforderlich ist.</param>
/// <param name="ValidationRules">Optionale Validierungsregeln als JSON.</param>
public sealed record UpdateQuestionRequest(
    string Key,
    string Text,
    QuestionType Type,
    int Order,
    bool IsRequired,
    string? ValidationRules);

/// <summary>
/// Antwort mit einer Frage samt ihren Antwortoptionen.
/// </summary>
/// <param name="Id">Der Primärschlüssel der Frage.</param>
/// <param name="DialogId">Der Fremdschlüssel auf den zugehörigen Dialog.</param>
/// <param name="Key">Der fachliche, stabile Schlüssel der Frage.</param>
/// <param name="Text">Der angezeigte Fragetext.</param>
/// <param name="Type">Der Antworttyp der Frage.</param>
/// <param name="Order">Der Reihenfolge-Index der Frage innerhalb des Dialogs.</param>
/// <param name="IsRequired">Gibt an, ob eine Antwort erforderlich ist.</param>
/// <param name="ValidationRules">Optionale Validierungsregeln als JSON.</param>
/// <param name="Options">Die Antwortoptionen der Frage, nach <c>Order</c> sortiert.</param>
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
