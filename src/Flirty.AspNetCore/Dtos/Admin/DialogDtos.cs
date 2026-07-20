namespace Flirty.AspNetCore.Dtos.Admin;

/// <summary>
/// Anfrage-Körper zum Anlegen eines Dialogs (<c>POST {prefix}/dialogs</c>). Version, Veröffentlichungs-
/// status und Einstiegsfrage werden serverseitig gesetzt bzw. nachträglich per Update festgelegt.
/// </summary>
/// <param name="Key">Der fachliche, stabile Schlüssel des Dialogs (muss eindeutig sein).</param>
/// <param name="Name">Der Anzeigename des Dialogs.</param>
/// <param name="Description">Die optionale Beschreibung des Dialogs.</param>
public sealed record CreateDialogRequest(string Key, string Name, string? Description);

/// <summary>
/// Anfrage-Körper zum Ändern eines Dialogs (<c>PUT {prefix}/dialogs/{id}</c>).
/// </summary>
/// <param name="Key">Der fachliche, stabile Schlüssel des Dialogs (muss eindeutig bleiben).</param>
/// <param name="Name">Der Anzeigename des Dialogs.</param>
/// <param name="Description">Die optionale Beschreibung des Dialogs.</param>
/// <param name="StartQuestionId">Optionaler Verweis auf die Einstiegsfrage dieses Dialogs.</param>
public sealed record UpdateDialogRequest(string Key, string Name, string? Description, Guid? StartQuestionId);

/// <summary>
/// Antwort mit den Metadaten eines Dialogs (ohne Graph). Ergebnis der Dialog-CRUD-Endpunkte und der
/// Dialog-Liste.
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
public sealed record DialogResponse(
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
/// Antwort mit einem Dialog samt seinem im Admin-CRUD verwalteten Graphen (Fragen inkl. Optionen und
/// Übergänge). Ergebnis von <c>GET {prefix}/dialogs/{id}</c>.
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
/// <param name="Questions">Die Fragen des Dialogs (inkl. Optionen), nach <c>Order</c> sortiert.</param>
/// <param name="Transitions">Die Übergänge des Dialogs, nach <c>Priority</c> sortiert.</param>
public sealed record DialogDetailResponse(
    Guid Id,
    string Key,
    string Name,
    string? Description,
    int Version,
    bool IsPublished,
    Guid? StartQuestionId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<QuestionResponse> Questions,
    IReadOnlyList<TransitionResponse> Transitions);
