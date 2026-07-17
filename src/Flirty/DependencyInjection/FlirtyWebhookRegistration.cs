namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Beschreibt einen über <see cref="FlirtyOptions.AddWebhook(string, string)"/> registrierten
/// Outbound-Webhook: welches Ereignis (<paramref name="EventName"/>) an welche Ziel-URL
/// (<paramref name="Url"/>) ausgeliefert werden soll.
/// </summary>
/// <remarks>
/// Stub aus Issue #34: <c>AddFlirty</c> sammelt diese Registrierungen und stellt sie als
/// <see cref="System.Collections.Generic.IReadOnlyList{T}"/> im Container bereit. Die eigentliche
/// Auslieferung (Notification-Publikation aus den Command-Handlern + Outbound-<c>INotificationHandler</c>
/// mit <c>IHttpClientFactory</c>, Retry/Timeout) folgt in EPIC 4 (Trigger, Meilenstein M2) und
/// konsumiert genau diese Registrierungen.
/// </remarks>
/// <param name="EventName">Der fachliche Ereignisname, der den Webhook auslöst (z. B. <c>order-created</c>).</param>
/// <param name="Url">Die Ziel-URL, an die der Webhook per HTTP ausgeliefert wird.</param>
public sealed record FlirtyWebhookRegistration(string EventName, string Url);
