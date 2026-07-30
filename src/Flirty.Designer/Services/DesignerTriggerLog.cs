using Flirty.Domain;

namespace Flirty.Designer.Services;

/// <summary>
/// A trigger event observed during a test run (a notification published by the engine).
/// </summary>
/// <param name="OccurredAt">The moment of observation (UTC).</param>
/// <param name="Scope">
/// The <see cref="TriggerScope"/> that corresponds to this notification – the same mapping the
/// core <c>WebhookNotificationHandler</c> uses when selecting the configured triggers.
/// </param>
/// <param name="Notification">The name of the notification contract (e.g. <c>DialogCompletedNotification</c>).</param>
/// <param name="QuestionId">The affected question, if the notification carries one.</param>
/// <param name="Detail">A short, human-readable extra piece of information.</param>
internal sealed record DesignerTriggerEntry(
    DateTimeOffset OccurredAt,
    TriggerScope Scope,
    string Notification,
    Guid? QuestionId,
    string Detail);

/// <summary>
/// Collects the trigger notifications published during a test run (#43), so the test runner
/// can show <b>what</b> actually fired.
/// </summary>
/// <remarks>
/// <para>
/// Registered as <c>Scoped</c>, the log lives per Blazor circuit. Because the
/// <see cref="FlirtyRuntimeGateway"/> runs each engine step in a <b>fresh</b> child scope, the
/// notification handlers constructed there would otherwise get an empty throwaway instance. That is why
/// the gateway passes the circuit's list into the child scope via <see cref="Adopt"/> – the same pattern
/// (and same reason) as with <see cref="ActiveConnectionProfile.Adopt"/>.
/// </para>
/// <para>
/// Deliberately without synchronization: Blazor Server serializes the render/event processing of a
/// circuit, and the test runner only ever runs one engine step at a time.
/// </para>
/// </remarks>
internal sealed class DesignerTriggerLog
{
    private List<DesignerTriggerEntry> _entries = [];

    /// <summary>The events observed so far, in chronological order.</summary>
    public IReadOnlyList<DesignerTriggerEntry> Entries => _entries;

    /// <summary>Records an observed event.</summary>
    /// <param name="entry">The event to log.</param>
    public void Add(DesignerTriggerEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        _entries.Add(entry);
    }

    /// <summary>Clears the log – called by the test runner when a new run starts.</summary>
    public void Clear() => _entries = [];

    /// <summary>
    /// Adopts the event list of the calling circuit into <b>this</b> scope, so that the notification
    /// handlers constructed in the child scope write into the same list.
    /// </summary>
    /// <param name="parent">The log of the calling circuit.</param>
    public void Adopt(DesignerTriggerLog parent)
    {
        ArgumentNullException.ThrowIfNull(parent);

        _entries = parent._entries;
    }
}
