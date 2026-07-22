using Flirty.Domain;

namespace Flirty.AspNetCore.Dtos.Admin;

/// <summary>
/// Anfrage-Körper zum Anlegen einer Trigger-Definition
/// (<c>POST {prefix}/dialogs/{dialogId}/triggers</c>).
/// </summary>
/// <param name="Scope">Der Zeitpunkt im Dialogablauf, zu dem der Trigger auslöst.</param>
/// <param name="QuestionId">
/// Die Frage, auf die bei <see cref="TriggerScope.AfterQuestion"/> gehört wird (dort erforderlich); bei
/// allen anderen Zeitpunkten <see langword="null"/>.
/// </param>
/// <param name="Kind">Der Kanal (<see cref="TriggerKind.Webhook"/> oder <see cref="TriggerKind.InProcess"/>).</param>
/// <param name="Config">
/// Die kanal-spezifische Konfiguration als JSON (Schema: <see cref="TriggerConfig"/>, z. B.
/// <c>{"url":"https://host.example/hook","name":"order-created"}</c>). Bei
/// <see cref="TriggerKind.Webhook"/> ist eine absolute http-/https-URL Pflicht.
/// </param>
/// <param name="Expression">Optionaler Bedingungsausdruck; <see langword="null"/>/leer = bedingungslos.</param>
public sealed record CreateTriggerRequest(
    TriggerScope Scope,
    Guid? QuestionId,
    TriggerKind Kind,
    string Config,
    string? Expression);

/// <summary>
/// Anfrage-Körper zum Ändern einer Trigger-Definition
/// (<c>PUT {prefix}/dialogs/{dialogId}/triggers/{triggerId}</c>).
/// </summary>
/// <param name="Scope">Der Zeitpunkt im Dialogablauf, zu dem der Trigger auslöst.</param>
/// <param name="QuestionId">Die Frage bei <see cref="TriggerScope.AfterQuestion"/>, sonst <see langword="null"/>.</param>
/// <param name="Kind">Der Kanal, über den die Host-Anwendung benachrichtigt wird.</param>
/// <param name="Config">Die kanal-spezifische Konfiguration als JSON (Schema: <see cref="TriggerConfig"/>).</param>
/// <param name="Expression">Optionaler Bedingungsausdruck; <see langword="null"/>/leer = bedingungslos.</param>
public sealed record UpdateTriggerRequest(
    TriggerScope Scope,
    Guid? QuestionId,
    TriggerKind Kind,
    string Config,
    string? Expression);

/// <summary>
/// Antwort mit einer Trigger-Definition.
/// </summary>
/// <param name="Id">Der Primärschlüssel der Trigger-Definition.</param>
/// <param name="DialogId">Der Fremdschlüssel auf den zugehörigen Dialog.</param>
/// <param name="Scope">Der Zeitpunkt im Dialogablauf, zu dem der Trigger auslöst.</param>
/// <param name="QuestionId">Die Frage bei <see cref="TriggerScope.AfterQuestion"/>, sonst <see langword="null"/>.</param>
/// <param name="Kind">Der Kanal, über den die Host-Anwendung benachrichtigt wird.</param>
/// <param name="Config">Die kanal-spezifische Konfiguration als JSON.</param>
/// <param name="Expression">Optionaler Bedingungsausdruck.</param>
public sealed record TriggerResponse(
    Guid Id,
    Guid DialogId,
    TriggerScope Scope,
    Guid? QuestionId,
    TriggerKind Kind,
    string Config,
    string? Expression);
