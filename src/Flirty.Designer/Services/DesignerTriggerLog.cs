using Flirty.Domain;

namespace Flirty.Designer.Services;

/// <summary>
/// Ein im Testlauf beobachtetes Trigger-Ereignis (eine von der Engine publizierte Notification).
/// </summary>
/// <param name="OccurredAt">Der Zeitpunkt der Beobachtung (UTC).</param>
/// <param name="Scope">
/// Der <see cref="TriggerScope"/>, der dieser Notification entspricht – dieselbe Zuordnung, die der
/// Core-<c>WebhookNotificationHandler</c> beim Auswählen der konfigurierten Trigger verwendet.
/// </param>
/// <param name="Notification">Der Name des Notification-Contracts (z. B. <c>DialogCompletedNotification</c>).</param>
/// <param name="QuestionId">Die betroffene Frage, sofern die Notification eine trägt.</param>
/// <param name="Detail">Eine kurze, menschenlesbare Zusatzinformation.</param>
internal sealed record DesignerTriggerEntry(
    DateTimeOffset OccurredAt,
    TriggerScope Scope,
    string Notification,
    Guid? QuestionId,
    string Detail);

/// <summary>
/// Sammelt die während eines Testlaufs (#43) publizierten Trigger-Notifications, damit der Test-Runner
/// zeigen kann, <b>was</b> tatsächlich gefeuert hat.
/// </summary>
/// <remarks>
/// <para>
/// Als <c>Scoped</c> registriert lebt der Log pro Blazor-Circuit. Weil der
/// <see cref="FlirtyRuntimeGateway"/> jeden Engine-Schritt in einem <b>frischen</b> Kind-Scope ausführt,
/// bekämen die dort konstruierten Notification-Handler sonst eine leere Wegwerf-Instanz. Deshalb reicht
/// das Gateway die Liste des Circuits per <see cref="Adopt"/> in den Kind-Scope durch – dasselbe Muster
/// (und derselbe Grund) wie bei <see cref="ActiveConnectionProfile.Adopt"/>.
/// </para>
/// <para>
/// Bewusst ohne Synchronisierung: Blazor Server serialisiert die Render-/Event-Verarbeitung eines
/// Circuits, und der Test-Runner führt immer nur einen Engine-Schritt zur Zeit aus.
/// </para>
/// </remarks>
internal sealed class DesignerTriggerLog
{
    private List<DesignerTriggerEntry> _entries = [];

    /// <summary>Die bisher beobachteten Ereignisse in chronologischer Reihenfolge.</summary>
    public IReadOnlyList<DesignerTriggerEntry> Entries => _entries;

    /// <summary>Hält ein beobachtetes Ereignis fest.</summary>
    /// <param name="entry">Das zu protokollierende Ereignis.</param>
    public void Add(DesignerTriggerEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        _entries.Add(entry);
    }

    /// <summary>Leert das Protokoll – vom Test-Runner beim Start eines neuen Laufs aufgerufen.</summary>
    public void Clear() => _entries = [];

    /// <summary>
    /// Übernimmt die Ereignisliste des aufrufenden Circuits in <b>diesen</b> Scope, damit die im
    /// Kind-Scope konstruierten Notification-Handler in dieselbe Liste schreiben.
    /// </summary>
    /// <param name="parent">Der Log des aufrufenden Circuits.</param>
    public void Adopt(DesignerTriggerLog parent)
    {
        ArgumentNullException.ThrowIfNull(parent);

        _entries = parent._entries;
    }
}
