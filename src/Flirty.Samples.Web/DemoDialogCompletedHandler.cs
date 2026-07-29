using Flirty.Runtime;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Flirty.Samples.Web;

/// <summary>
/// The web sample's sample in-process handler: reacts to the <see cref="DialogCompletedNotification"/>
/// published by the engine and records the completion in the <see cref="TriggerLog"/>,
/// which the chat UI displays via <c>GET /demo/triggers</c>. Demonstrates the in-process trigger back-channel
/// (registration via <c>AddFlirtyHandler&lt;DialogCompletedNotification, DemoDialogCompletedHandler&gt;()</c>).
/// </summary>
public sealed class DemoDialogCompletedHandler : INotificationHandler<DialogCompletedNotification>
{
    private readonly TriggerLog _triggerLog;
    private readonly ILogger<DemoDialogCompletedHandler> _logger;

    /// <summary>Initializes the handler with the trigger sink and the logger.</summary>
    /// <param name="triggerLog">The in-memory sink for the display in the chat UI.</param>
    /// <param name="logger">The logger for an additional server output.</param>
    public DemoDialogCompletedHandler(TriggerLog triggerLog, ILogger<DemoDialogCompletedHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(triggerLog);
        ArgumentNullException.ThrowIfNull(logger);
        _triggerLog = triggerLog;
        _logger = logger;
    }

    /// <summary>Processes the completion notification by recording a trigger entry.</summary>
    /// <param name="notification">The triggered completion notification.</param>
    /// <param name="cancellationToken">Token to cancel (not needed here).</param>
    /// <returns>A completed <see cref="ValueTask"/>.</returns>
    public ValueTask Handle(DialogCompletedNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        _triggerLog.Add(new TriggerLogEntry(
            notification.DialogKey, notification.SessionId, notification.Answers.Count, DateTimeOffset.UtcNow));

        _logger.LogInformation(
            "In-Process-Trigger: Dialog '{DialogKey}' abgeschlossen (Session {SessionId}, {AnswerCount} Antworten).",
            notification.DialogKey, notification.SessionId, notification.Answers.Count);

        return ValueTask.CompletedTask;
    }
}
