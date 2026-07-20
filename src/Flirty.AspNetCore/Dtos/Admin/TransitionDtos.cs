namespace Flirty.AspNetCore.Dtos.Admin;

/// <summary>
/// Anfrage-Körper zum Anlegen eines bedingten Übergangs
/// (<c>POST {prefix}/dialogs/{dialogId}/transitions</c>).
/// </summary>
/// <param name="FromQuestionId">Verweis auf die Ausgangsfrage.</param>
/// <param name="TargetQuestionId">Verweis auf die Zielfrage.</param>
/// <param name="Expression">Optionaler Bedingungsausdruck; <see langword="null"/>/leer = bedingungslos.</param>
/// <param name="Priority">Priorität für die Auswertungsreihenfolge (kleinerer Wert = früher).</param>
/// <param name="IsDefault">Gibt an, ob dieser Übergang der Default ist.</param>
public sealed record CreateTransitionRequest(
    Guid FromQuestionId,
    Guid TargetQuestionId,
    string? Expression,
    int Priority,
    bool IsDefault);

/// <summary>
/// Anfrage-Körper zum Ändern eines bedingten Übergangs
/// (<c>PUT {prefix}/dialogs/{dialogId}/transitions/{transitionId}</c>).
/// </summary>
/// <param name="FromQuestionId">Verweis auf die Ausgangsfrage.</param>
/// <param name="TargetQuestionId">Verweis auf die Zielfrage.</param>
/// <param name="Expression">Optionaler Bedingungsausdruck; <see langword="null"/>/leer = bedingungslos.</param>
/// <param name="Priority">Priorität für die Auswertungsreihenfolge (kleinerer Wert = früher).</param>
/// <param name="IsDefault">Gibt an, ob dieser Übergang der Default ist.</param>
public sealed record UpdateTransitionRequest(
    Guid FromQuestionId,
    Guid TargetQuestionId,
    string? Expression,
    int Priority,
    bool IsDefault);

/// <summary>
/// Antwort mit einem bedingten Übergang (Branching).
/// </summary>
/// <param name="Id">Der Primärschlüssel des Übergangs.</param>
/// <param name="DialogId">Der Fremdschlüssel auf den zugehörigen Dialog.</param>
/// <param name="FromQuestionId">Verweis auf die Ausgangsfrage.</param>
/// <param name="TargetQuestionId">Verweis auf die Zielfrage.</param>
/// <param name="Expression">Optionaler Bedingungsausdruck; <see langword="null"/>/leer = bedingungslos.</param>
/// <param name="Priority">Priorität für die Auswertungsreihenfolge (kleinerer Wert = früher).</param>
/// <param name="IsDefault">Gibt an, ob dieser Übergang der Default ist.</param>
public sealed record TransitionResponse(
    Guid Id,
    Guid DialogId,
    Guid FromQuestionId,
    Guid TargetQuestionId,
    string? Expression,
    int Priority,
    bool IsDefault);
