namespace Flirty.AspNetCore.Dtos.Admin;

/// <summary>
/// Anfrage-Körper zum Anlegen einer Antwortoption
/// (<c>POST {prefix}/dialogs/{dialogId}/questions/{questionId}/options</c>).
/// </summary>
/// <param name="Key">Der fachliche, stabile Schlüssel der Option (in der Frage eindeutig).</param>
/// <param name="Label">Der angezeigte Beschriftungstext der Option.</param>
/// <param name="Value">Der bei Auswahl gespeicherte Wert der Option.</param>
/// <param name="Order">Der Reihenfolge-Index der Option innerhalb der Frage.</param>
public sealed record CreateAnswerOptionRequest(string Key, string Label, string Value, int Order);

/// <summary>
/// Anfrage-Körper zum Ändern einer Antwortoption
/// (<c>PUT {prefix}/dialogs/{dialogId}/questions/{questionId}/options/{optionId}</c>).
/// </summary>
/// <param name="Key">Der fachliche, stabile Schlüssel der Option (in der Frage eindeutig).</param>
/// <param name="Label">Der angezeigte Beschriftungstext der Option.</param>
/// <param name="Value">Der bei Auswahl gespeicherte Wert der Option.</param>
/// <param name="Order">Der Reihenfolge-Index der Option innerhalb der Frage.</param>
public sealed record UpdateAnswerOptionRequest(string Key, string Label, string Value, int Order);

/// <summary>
/// Antwort mit einer Antwortoption.
/// </summary>
/// <param name="Id">Der Primärschlüssel der Antwortoption.</param>
/// <param name="QuestionId">Der Fremdschlüssel auf die zugehörige Frage.</param>
/// <param name="Key">Der fachliche, stabile Schlüssel der Option.</param>
/// <param name="Label">Der angezeigte Beschriftungstext der Option.</param>
/// <param name="Value">Der bei Auswahl gespeicherte Wert der Option.</param>
/// <param name="Order">Der Reihenfolge-Index der Option innerhalb der Frage.</param>
public sealed record AnswerOptionResponse(
    Guid Id,
    Guid QuestionId,
    string Key,
    string Label,
    string Value,
    int Order);
