namespace Flirty.Samples.Web;

/// <summary>
/// Ein empfangener Webhook: der Trigger-Name aus dem Header <c>X-Flirty-Event</c> und der rohe JSON-Body.
/// </summary>
/// <param name="Event">Der ausgelöste Trigger-Scope (z. B. <c>OnDialogCompleted</c>).</param>
/// <param name="Payload">Der rohe JSON-Body der zugestellten Notification.</param>
/// <param name="ReceivedAt">Zeitpunkt des Eingangs.</param>
public sealed record WebhookReceipt(string Event, string Payload, DateTimeOffset ReceivedAt);

/// <summary>
/// Thread-sichere In-Memory-Senke für die vom Inbound-Empfänger (<c>POST /demo/webhooks/flirty</c>)
/// entgegengenommenen Outbound-Webhooks der Engine. Wird als Singleton registriert und vom Endpunkt
/// <c>GET /demo/webhooks</c> gelesen, damit die Chat-UI den Outbound→Inbound-Rundlauf sichtbar macht.
/// </summary>
public sealed class WebhookInbox
{
    private readonly object _gate = new();
    private readonly List<WebhookReceipt> _receipts = [];

    /// <summary>Zeichnet einen eingegangenen Webhook auf.</summary>
    /// <param name="eventName">Der Wert des <c>X-Flirty-Event</c>-Headers.</param>
    /// <param name="payload">Der rohe JSON-Body der Zustellung.</param>
    public void Add(string eventName, string payload)
    {
        var receipt = new WebhookReceipt(eventName ?? string.Empty, payload ?? string.Empty, DateTimeOffset.UtcNow);
        lock (_gate)
        {
            _receipts.Add(receipt);
        }
    }

    /// <summary>Liefert eine Momentaufnahme aller bisher empfangenen Webhooks (neueste zuletzt).</summary>
    /// <returns>Eine unveränderliche Kopie der Einträge.</returns>
    public IReadOnlyList<WebhookReceipt> Snapshot()
    {
        lock (_gate)
        {
            return _receipts.ToArray();
        }
    }
}
