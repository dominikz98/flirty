using Flirty.Runtime;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Flirty.Samples.Web;

/// <summary>
/// Beispiel-In-Process-Handler der Web-Sample: reagiert auf die von der Engine publizierte
/// <see cref="DialogCompletedNotification"/> und protokolliert den Abschluss in den <see cref="TriggerLog"/>,
/// den die Chat-UI über <c>GET /demo/triggers</c> anzeigt. Demonstriert den In-Process-Trigger-Rückkanal
/// (Registrierung per <c>AddFlirtyHandler&lt;DialogCompletedNotification, DemoDialogCompletedHandler&gt;()</c>).
/// </summary>
public sealed class DemoDialogCompletedHandler : INotificationHandler<DialogCompletedNotification>
{
    private readonly TriggerLog _triggerLog;
    private readonly ILogger<DemoDialogCompletedHandler> _logger;

    /// <summary>Initialisiert den Handler mit der Trigger-Senke und dem Logger.</summary>
    /// <param name="triggerLog">Die In-Memory-Senke für die Anzeige in der Chat-UI.</param>
    /// <param name="logger">Der Logger für eine zusätzliche Server-Ausgabe.</param>
    public DemoDialogCompletedHandler(TriggerLog triggerLog, ILogger<DemoDialogCompletedHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(triggerLog);
        ArgumentNullException.ThrowIfNull(logger);
        _triggerLog = triggerLog;
        _logger = logger;
    }

    /// <summary>Verarbeitet die Abschluss-Notification, indem ein Trigger-Eintrag aufgezeichnet wird.</summary>
    /// <param name="notification">Die ausgelöste Abschluss-Notification.</param>
    /// <param name="cancellationToken">Token zum Abbrechen (hier nicht benötigt).</param>
    /// <returns>Ein abgeschlossener <see cref="ValueTask"/>.</returns>
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
