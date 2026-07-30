namespace Flirty.Samples.Web;

/// <summary>
/// An entry of the <see cref="TriggerLog"/>: records that the in-process handler received a
/// completion notification.
/// </summary>
/// <param name="DialogKey">The business key of the completed dialog.</param>
/// <param name="SessionId">The id of the completed session.</param>
/// <param name="AnswerCount">Number of answers given at the time of completion.</param>
/// <param name="ReceivedAt">Time at which the handler received the notification.</param>
public sealed record TriggerLogEntry(string DialogKey, Guid SessionId, int AnswerCount, DateTimeOffset ReceivedAt);

/// <summary>
/// Thread-safe in-memory sink for the in-process triggers received by the own
/// <see cref="DemoDialogCompletedHandler"/>. Registered as a singleton and read by the endpoint
/// <c>GET /demo/triggers</c>, so that the chat UI can make the firing of the handler visible.
/// </summary>
public sealed class TriggerLog
{
    private readonly object _gate = new();
    private readonly List<TriggerLogEntry> _entries = [];

    /// <summary>Appends a trigger entry to the log.</summary>
    /// <param name="entry">The entry to record.</param>
    public void Add(TriggerLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_gate)
        {
            _entries.Add(entry);
        }
    }

    /// <summary>Returns a snapshot of all triggers recorded so far (newest last).</summary>
    /// <returns>An immutable copy of the entries.</returns>
    public IReadOnlyList<TriggerLogEntry> Snapshot()
    {
        lock (_gate)
        {
            return _entries.ToArray();
        }
    }
}
