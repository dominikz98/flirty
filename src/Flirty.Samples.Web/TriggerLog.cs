namespace Flirty.Samples.Web;

/// <summary>
/// Ein Eintrag des <see cref="TriggerLog"/>: hält fest, dass der In-Process-Handler eine
/// Abschluss-Notification empfangen hat.
/// </summary>
/// <param name="DialogKey">Der fachliche Schlüssel des abgeschlossenen Dialogs.</param>
/// <param name="SessionId">Die Id der abgeschlossenen Session.</param>
/// <param name="AnswerCount">Anzahl der zum Abschlusszeitpunkt gegebenen Antworten.</param>
/// <param name="ReceivedAt">Zeitpunkt, zu dem der Handler die Notification empfangen hat.</param>
public sealed record TriggerLogEntry(string DialogKey, Guid SessionId, int AnswerCount, DateTimeOffset ReceivedAt);

/// <summary>
/// Thread-sichere In-Memory-Senke für die vom eigenen <see cref="DemoDialogCompletedHandler"/> empfangenen
/// In-Process-Trigger. Wird als Singleton registriert und vom Endpunkt <c>GET /demo/triggers</c> gelesen,
/// damit die Chat-UI die Auslösung des Handlers sichtbar machen kann.
/// </summary>
public sealed class TriggerLog
{
    private readonly object _gate = new();
    private readonly List<TriggerLogEntry> _entries = [];

    /// <summary>Hängt einen Trigger-Eintrag an das Protokoll an.</summary>
    /// <param name="entry">Der aufzuzeichnende Eintrag.</param>
    public void Add(TriggerLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_gate)
        {
            _entries.Add(entry);
        }
    }

    /// <summary>Liefert eine Momentaufnahme aller bisher aufgezeichneten Trigger (neueste zuletzt).</summary>
    /// <returns>Eine unveränderliche Kopie der Einträge.</returns>
    public IReadOnlyList<TriggerLogEntry> Snapshot()
    {
        lock (_gate)
        {
            return _entries.ToArray();
        }
    }
}
