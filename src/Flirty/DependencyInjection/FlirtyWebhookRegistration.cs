using Flirty.Domain;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Beschreibt einen über <see cref="FlirtyOptions.AddWebhook(TriggerScope, string, string)"/> (bzw. den
/// altbekannten <see cref="FlirtyOptions.AddWebhook(string, string)"/>) registrierten Outbound-Webhook:
/// welches Ereignis (<paramref name="EventName"/> bzw. <paramref name="Scope"/>) an welche Ziel-URL
/// (<paramref name="Url"/>) – optional gefiltert durch einen Bedingungsausdruck
/// (<paramref name="Expression"/>) – ausgeliefert werden soll.
/// </summary>
/// <remarks>
/// Ursprung als Stub in Issue #34 (nur <paramref name="EventName"/> + <paramref name="Url"/>). Seit Issue
/// #33 konsumiert der eingebaute <c>WebhookNotificationHandler</c> genau diese Registrierungen und liefert
/// sie über <c>IHttpClientFactory</c> mit Standard-Resilience (Retry/Timeout) aus. Ausgeliefert werden
/// ausschließlich Registrierungen mit gesetztem <paramref name="Scope"/> (über den
/// <see cref="TriggerScope"/>-Overload von <c>AddWebhook</c>); der zustandslose String-Overload
/// (<paramref name="Scope"/> = <see langword="null"/>) bleibt aus Kompatibilität bestehen, wird vom
/// Handler aber nicht zugestellt.
/// </remarks>
/// <param name="EventName">
/// Der fachliche Ereignisname des Webhooks. Für den <see cref="TriggerScope"/>-Overload entspricht er dem
/// Namen des <paramref name="Scope"/> (z. B. <c>OnDialogCompleted</c>); für den String-Overload ein frei
/// gewählter Bezeichner (z. B. <c>order-created</c>).
/// </param>
/// <param name="Url">Die Ziel-URL, an die der Webhook per HTTP POST ausgeliefert wird.</param>
/// <param name="Scope">
/// Der Auslöse-Zeitpunkt im Dialogablauf (siehe <see cref="TriggerScope"/>), auf den der eingebaute Handler
/// matcht. <see langword="null"/> bei Registrierungen aus dem reinen String-Overload – diese werden nicht
/// ausgeliefert.
/// </param>
/// <param name="Expression">
/// Optionaler Bedingungsausdruck, der über <see cref="Flirty.Expressions.IExpressionEvaluator"/> ausgewertet
/// wird und über das Auslösen entscheidet. <see langword="null"/>/leer ⇒ bedingungslos auslösend.
/// </param>
public sealed record FlirtyWebhookRegistration(
    string EventName,
    string Url,
    TriggerScope? Scope = null,
    string? Expression = null);
