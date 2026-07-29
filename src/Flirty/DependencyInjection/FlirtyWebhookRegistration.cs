using Flirty.Domain;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Describes an outbound webhook registered via <see cref="FlirtyOptions.AddWebhook(TriggerScope, string, string)"/> (or the
/// long-standing <see cref="FlirtyOptions.AddWebhook(string, string)"/>):
/// which event (<paramref name="EventName"/> or <paramref name="Scope"/>) is to be delivered to which target URL
/// (<paramref name="Url"/>) – optionally filtered by a condition expression
/// (<paramref name="Expression"/>).
/// </summary>
/// <remarks>
/// Originally a stub in issue #34 (only <paramref name="EventName"/> + <paramref name="Url"/>). Since issue
/// #33 the built-in <c>WebhookNotificationHandler</c> consumes exactly these registrations and delivers
/// them via <c>IHttpClientFactory</c> with standard resilience (retry/timeout). Only
/// registrations with a set <paramref name="Scope"/> are delivered (via the
/// <see cref="TriggerScope"/> overload of <c>AddWebhook</c>); the stateless string overload
/// (<paramref name="Scope"/> = <see langword="null"/>) remains for compatibility, but is not delivered by the
/// handler.
/// </remarks>
/// <param name="EventName">
/// The domain event name of the webhook. For the <see cref="TriggerScope"/> overload it corresponds to the
/// name of the <paramref name="Scope"/> (e.g. <c>OnDialogCompleted</c>); for the string overload a freely
/// chosen identifier (e.g. <c>order-created</c>).
/// </param>
/// <param name="Url">The target URL to which the webhook is delivered via HTTP POST.</param>
/// <param name="Scope">
/// The trigger point in the dialog flow (see <see cref="TriggerScope"/>) that the built-in handler
/// matches on. <see langword="null"/> for registrations from the plain string overload – these are not
/// delivered.
/// </param>
/// <param name="Expression">
/// Optional condition expression that is evaluated via <see cref="Flirty.Expressions.IExpressionEvaluator"/>
/// and decides about firing. <see langword="null"/>/empty ⇒ unconditionally firing.
/// </param>
public sealed record FlirtyWebhookRegistration(
    string EventName,
    string Url,
    TriggerScope? Scope = null,
    string? Expression = null);
