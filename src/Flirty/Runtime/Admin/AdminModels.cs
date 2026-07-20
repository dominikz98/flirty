using Flirty.Domain;

namespace Flirty.Runtime.Admin;

/// <summary>
/// Navigationsfreie Sicht auf einen <see cref="Dialog"/> (nur die Metadaten, ohne den
/// Konfigurationsgraphen). Ergebnis der Dialog-CRUD-Commands und der Dialog-Liste.
/// </summary>
/// <param name="Id">Der Primärschlüssel des Dialogs.</param>
/// <param name="Key">Der fachliche, stabile Schlüssel des Dialogs.</param>
/// <param name="Name">Der Anzeigename des Dialogs.</param>
/// <param name="Description">Die optionale Beschreibung des Dialogs.</param>
/// <param name="Version">Die Versionsnummer des Dialogs.</param>
/// <param name="IsPublished">Gibt an, ob der Dialog veröffentlicht (produktiv startbar) ist.</param>
/// <param name="StartQuestionId">Verweis auf die Einstiegsfrage oder <see langword="null"/>.</param>
/// <param name="CreatedAt">Zeitpunkt der Erstellung.</param>
/// <param name="UpdatedAt">Zeitpunkt der letzten Änderung.</param>
public sealed record DialogSummary(
    Guid Id,
    string Key,
    string Name,
    string? Description,
    int Version,
    bool IsPublished,
    Guid? StartQuestionId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Navigationsfreie Sicht auf einen <see cref="Dialog"/> samt seinem im Rahmen des Admin-CRUD
/// verwalteten Graphen (Fragen inkl. Optionen und Übergänge). Ergebnis von <c>GetDialogQuery</c>.
/// </summary>
/// <param name="Dialog">Die Dialog-Metadaten.</param>
/// <param name="Questions">Die Fragen des Dialogs (inklusive ihrer Antwortoptionen), nach <c>Order</c> sortiert.</param>
/// <param name="Transitions">Die bedingten Übergänge des Dialogs, nach <c>Priority</c> sortiert.</param>
public sealed record DialogDetail(
    DialogSummary Dialog,
    IReadOnlyList<QuestionDetail> Questions,
    IReadOnlyList<TransitionDetail> Transitions);

/// <summary>
/// Navigationsfreie Sicht auf eine <see cref="Question"/> für das Admin-CRUD (mit allen
/// konfigurierbaren Feldern, anders als die schlanke Laufzeit-Sicht <see cref="QuestionView"/>).
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
public sealed record QuestionDetail(
    Guid Id,
    Guid DialogId,
    string Key,
    string Text,
    QuestionType Type,
    int Order,
    bool IsRequired,
    string? ValidationRules,
    IReadOnlyList<AnswerOptionDetail> Options);

/// <summary>
/// Navigationsfreie Sicht auf eine <see cref="AnswerOption"/> für das Admin-CRUD.
/// </summary>
/// <param name="Id">Der Primärschlüssel der Antwortoption.</param>
/// <param name="QuestionId">Der Fremdschlüssel auf die zugehörige Frage.</param>
/// <param name="Key">Der fachliche, stabile Schlüssel der Option.</param>
/// <param name="Label">Der angezeigte Beschriftungstext der Option.</param>
/// <param name="Value">Der bei Auswahl gespeicherte Wert der Option.</param>
/// <param name="Order">Der Reihenfolge-Index der Option innerhalb der Frage.</param>
public sealed record AnswerOptionDetail(
    Guid Id,
    Guid QuestionId,
    string Key,
    string Label,
    string Value,
    int Order);

/// <summary>
/// Navigationsfreie Sicht auf einen <see cref="Transition"/> (Branching-Übergang) für das Admin-CRUD.
/// </summary>
/// <param name="Id">Der Primärschlüssel des Übergangs.</param>
/// <param name="DialogId">Der Fremdschlüssel auf den zugehörigen Dialog.</param>
/// <param name="FromQuestionId">Verweis auf die Ausgangsfrage.</param>
/// <param name="TargetQuestionId">Verweis auf die Zielfrage.</param>
/// <param name="Expression">Optionaler Bedingungsausdruck; <see langword="null"/>/leer = bedingungslos.</param>
/// <param name="Priority">Priorität für die Auswertungsreihenfolge (kleinerer Wert = früher).</param>
/// <param name="IsDefault">Gibt an, ob dieser Übergang der Default ist.</param>
public sealed record TransitionDetail(
    Guid Id,
    Guid DialogId,
    Guid FromQuestionId,
    Guid TargetQuestionId,
    string? Expression,
    int Priority,
    bool IsDefault);
