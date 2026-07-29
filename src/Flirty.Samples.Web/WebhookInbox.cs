namespace Flirty.Samples.Web;

/// <summary>
/// A received webhook: the trigger name from the header <c>X-Flirty-Event</c> and the raw JSON body.
/// </summary>
/// <param name="Event">The triggered trigger scope (e.g. <c>OnDialogCompleted</c>).</param>
/// <param name="Payload">The raw JSON body of the delivered notification.</param>
/// <param name="ReceivedAt">Point in time of arrival.</param>
public sealed record WebhookReceipt(string Event, string Payload, DateTimeOffset ReceivedAt);

/// <summary>
/// Thread-safe in-memory sink for the engine's outbound webhooks taken in by the inbound receiver
/// (<c>POST /demo/webhooks/flirty</c>). Registered as a singleton and read by the endpoint
/// <c>GET /demo/webhooks</c>, so that the chat UI makes the outbound→inbound round trip visible.
/// </summary>
public sealed class WebhookInbox
{
    private readonly object _gate = new();
    private readonly List<WebhookReceipt> _receipts = [];

    /// <summary>Records an incoming webhook.</summary>
    /// <param name="eventName">The value of the <c>X-Flirty-Event</c> header.</param>
    /// <param name="payload">The raw JSON body of the delivery.</param>
    public void Add(string eventName, string payload)
    {
        var receipt = new WebhookReceipt(eventName ?? string.Empty, payload ?? string.Empty, DateTimeOffset.UtcNow);
        lock (_gate)
        {
            _receipts.Add(receipt);
        }
    }

    /// <summary>Returns a snapshot of all webhooks received so far (newest last).</summary>
    /// <returns>An immutable copy of the entries.</returns>
    public IReadOnlyList<WebhookReceipt> Snapshot()
    {
        lock (_gate)
        {
            return _receipts.ToArray();
        }
    }
}
