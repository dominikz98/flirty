namespace Flirty.Domain;

/// <summary>
/// Legt fest, über welchen Kanal eine <see cref="TriggerDefinition"/> die Host-Anwendung
/// benachrichtigt.
/// </summary>
public enum TriggerKind
{
    /// <summary>
    /// In-Process-Benachrichtigung über eine Mediator-Notification; die Host-App reagiert
    /// mit einem eigenen <c>INotificationHandler&lt;T&gt;</c>.
    /// </summary>
    InProcess = 0,

    /// <summary>Ausgehender HTTP-Webhook an eine konfigurierte URL.</summary>
    Webhook = 1,
}
