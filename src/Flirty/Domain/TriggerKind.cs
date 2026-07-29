namespace Flirty.Domain;

/// <summary>
/// Defines over which channel a <see cref="TriggerDefinition"/> notifies the host
/// application.
/// </summary>
public enum TriggerKind
{
    /// <summary>
    /// In-process notification via a Mediator notification; the host app reacts
    /// with its own <c>INotificationHandler&lt;T&gt;</c>.
    /// </summary>
    InProcess = 0,

    /// <summary>Outgoing HTTP webhook to a configured URL.</summary>
    Webhook = 1,
}
