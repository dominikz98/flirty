using Flirty.Runtime;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Flirty.Samples.Web;

/// <summary>
/// Example in-process handler of the web sample: reacts to the <see cref="DialogCompletedNotification"/>
/// published by the engine and records the completion in the <see cref="TriggerLog"/>,
/// which the chat UI shows via <c>GET /demo/triggers</c>. Demonstrates the in-process trigger back channel
/// (registration via <c>AddFlirtyHandler&lt;DialogCompletedNotification, DemoDialogCompletedHandler&gt;()</c>).
/// </summary>
public sealed class DemoDialogCompletedHandler : INotificationHandler<DialogCompletedNotification>
{
    private readonly TriggerLog _triggerLog;
    private readonly ILogger<DemoDialogCompletedHandler> _logger;

    /// <summary>Initializes the handler with the trigger sink and the logger.</summary>
    /// <param name="triggerLog">The in-memory sink for display in the chat UI.</param>
    /// <param name="logger">The logger for additional server output.</param>
    public DemoDialogCompletedHandler(TriggerLog triggerLog, ILogger<DemoDialogCompletedHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(triggerLog);
        ArgumentNullException.ThrowIfNull(logger);
        _triggerLog = triggerLog;
        _logger = logger;
    }

    /// <summary>Handles the completion notification by recording a trigger entry.</summary>
    /// <param name="notification">The fired completion notification.</param>
    /// <param name="cancellationToken">Token for cancellation (not needed here).</param>
    /// <returns>A completed <see cref="ValueTask"/>.</returns>
    public ValueTask Handle(DialogCompletedNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        _triggerLog.Add(new TriggerLogEntry(
            notification.DialogKey, notification.SessionId, notification.Answers.Count, DateTimeOffset.UtcNow));

        _logger.LogInformation(
            "In-process trigger: dialog '{DialogKey}' completed (session {SessionId}, {AnswerCount} answers).",
            notification.DialogKey, notification.SessionId, notification.Answers.Count);

        return ValueTask.CompletedTask;
    }
}
