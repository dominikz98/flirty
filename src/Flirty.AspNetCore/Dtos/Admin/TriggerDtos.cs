using Flirty.Domain;

namespace Flirty.AspNetCore.Dtos.Admin;

/// <summary>
/// Request body for creating a trigger definition
/// (<c>POST {prefix}/dialogs/{dialogId}/triggers</c>).
/// </summary>
/// <param name="Scope">The point in the dialog flow at which the trigger fires.</param>
/// <param name="QuestionId">
/// The question listened to for <see cref="TriggerScope.AfterQuestion"/> (required there); for
/// all other points <see langword="null"/>.
/// </param>
/// <param name="Kind">The channel (<see cref="TriggerKind.Webhook"/> or <see cref="TriggerKind.InProcess"/>).</param>
/// <param name="Config">
/// The channel-specific configuration as JSON (schema: <see cref="TriggerConfig"/>, e.g.
/// <c>{"url":"https://host.example/hook","name":"order-created"}</c>). For
/// <see cref="TriggerKind.Webhook"/> an absolute http/https URL is mandatory.
/// </param>
/// <param name="Expression">Optional condition expression; <see langword="null"/>/empty = unconditional.</param>
public sealed record CreateTriggerRequest(
    TriggerScope Scope,
    Guid? QuestionId,
    TriggerKind Kind,
    string Config,
    string? Expression);

/// <summary>
/// Request body for changing a trigger definition
/// (<c>PUT {prefix}/dialogs/{dialogId}/triggers/{triggerId}</c>).
/// </summary>
/// <param name="Scope">The point in the dialog flow at which the trigger fires.</param>
/// <param name="QuestionId">The question for <see cref="TriggerScope.AfterQuestion"/>, otherwise <see langword="null"/>.</param>
/// <param name="Kind">The channel over which the host application is notified.</param>
/// <param name="Config">The channel-specific configuration as JSON (schema: <see cref="TriggerConfig"/>).</param>
/// <param name="Expression">Optional condition expression; <see langword="null"/>/empty = unconditional.</param>
public sealed record UpdateTriggerRequest(
    TriggerScope Scope,
    Guid? QuestionId,
    TriggerKind Kind,
    string Config,
    string? Expression);

/// <summary>
/// Response with a trigger definition.
/// </summary>
/// <param name="Id">The primary key of the trigger definition.</param>
/// <param name="DialogId">The foreign key to the associated dialog.</param>
/// <param name="Scope">The point in the dialog flow at which the trigger fires.</param>
/// <param name="QuestionId">The question for <see cref="TriggerScope.AfterQuestion"/>, otherwise <see langword="null"/>.</param>
/// <param name="Kind">The channel over which the host application is notified.</param>
/// <param name="Config">The channel-specific configuration as JSON.</param>
/// <param name="Expression">Optional condition expression.</param>
public sealed record TriggerResponse(
    Guid Id,
    Guid DialogId,
    TriggerScope Scope,
    Guid? QuestionId,
    TriggerKind Kind,
    string Config,
    string? Expression);
