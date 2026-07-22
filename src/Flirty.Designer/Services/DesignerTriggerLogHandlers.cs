using Flirty.Domain;
using Flirty.Runtime;
using Mediator;

namespace Flirty.Designer.Services;

/// <summary>
/// In-Process-Handler, die die vier Trigger-Notifications der Engine in den
/// <see cref="DesignerTriggerLog"/> des laufenden Testlaufs schreiben (#43).
/// </summary>
/// <remarks>
/// <para>
/// Registriert werden sie in <c>Program.cs</c> über <c>AddFlirtyHandler&lt;TNotification, THandler&gt;()</c>.
/// Sie laufen damit in <b>jedem</b> Scope des Designers – auch außerhalb eines Testlaufs. Das ist
/// harmlos: Ohne Engine-Durchlauf wird keine der Notifications publiziert, und ohne einen per
/// <see cref="DesignerTriggerLog.Adopt"/> durchgereichten Log schreiben sie in eine Wegwerf-Instanz des
/// jeweiligen Scopes.
/// </para>
/// <para>
/// Der zugeordnete <see cref="TriggerScope"/> spiegelt die Zuordnung des Core-<c>WebhookNotificationHandler</c>:
/// nur so passt das Protokoll zu den am Dialog konfigurierten <c>TriggerDefinition</c>s, die der Runner
/// daneben anzeigt.
/// </para>
/// </remarks>
internal static class DesignerTriggerLogHandlers
{
    /// <summary>Protokolliert den Start eines Dialogs (<see cref="TriggerScope.OnDialogStarted"/>).</summary>
    internal sealed class DialogStarted : INotificationHandler<DialogStartedNotification>
    {
        private readonly DesignerTriggerLog _log;

        /// <summary>Erstellt den Handler.</summary>
        /// <param name="log">Das Protokoll des laufenden Testlaufs.</param>
        public DialogStarted(DesignerTriggerLog log) => _log = log;

        /// <inheritdoc />
        public ValueTask Handle(DialogStartedNotification notification, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(notification);

            _log.Add(new DesignerTriggerEntry(
                notification.StartedAt,
                TriggerScope.OnDialogStarted,
                nameof(DialogStartedNotification),
                notification.CurrentQuestionId,
                $"Session für „{notification.ExternalUserKey}“ gestartet."));

            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Protokolliert eine erfasste Antwort (<see cref="TriggerScope.AfterAnswer"/>).</summary>
    internal sealed class AnswerSubmitted : INotificationHandler<AnswerSubmittedNotification>
    {
        private readonly DesignerTriggerLog _log;

        /// <summary>Erstellt den Handler.</summary>
        /// <param name="log">Das Protokoll des laufenden Testlaufs.</param>
        public AnswerSubmitted(DesignerTriggerLog log) => _log = log;

        /// <inheritdoc />
        public ValueTask Handle(AnswerSubmittedNotification notification, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(notification);

            var iteration = notification.IterationIndex is { } index
                ? $" (Iteration {index + 1})"
                : string.Empty;

            _log.Add(new DesignerTriggerEntry(
                DateTimeOffset.UtcNow,
                TriggerScope.AfterAnswer,
                nameof(AnswerSubmittedNotification),
                notification.QuestionId,
                $"Antwort {notification.Value}{iteration} erfasst."));

            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Protokolliert das Übergangs-Ergebnis (<see cref="TriggerScope.AfterQuestion"/>).</summary>
    internal sealed class QuestionAnswered : INotificationHandler<QuestionAnsweredNotification>
    {
        private readonly DesignerTriggerLog _log;

        /// <summary>Erstellt den Handler.</summary>
        /// <param name="log">Das Protokoll des laufenden Testlaufs.</param>
        public QuestionAnswered(DesignerTriggerLog log) => _log = log;

        /// <inheritdoc />
        public ValueTask Handle(QuestionAnsweredNotification notification, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(notification);

            _log.Add(new DesignerTriggerEntry(
                DateTimeOffset.UtcNow,
                TriggerScope.AfterQuestion,
                nameof(QuestionAnsweredNotification),
                notification.QuestionId,
                notification.IsCompleted
                    ? "Kein weiterer Übergang – Dialog endet hier."
                    : "Übergang ausgewertet."));

            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Protokolliert den Abschluss des Dialogs (<see cref="TriggerScope.OnDialogCompleted"/>).</summary>
    internal sealed class DialogCompleted : INotificationHandler<DialogCompletedNotification>
    {
        private readonly DesignerTriggerLog _log;

        /// <summary>Erstellt den Handler.</summary>
        /// <param name="log">Das Protokoll des laufenden Testlaufs.</param>
        public DialogCompleted(DesignerTriggerLog log) => _log = log;

        /// <inheritdoc />
        public ValueTask Handle(DialogCompletedNotification notification, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(notification);

            _log.Add(new DesignerTriggerEntry(
                DateTimeOffset.UtcNow,
                TriggerScope.OnDialogCompleted,
                nameof(DialogCompletedNotification),
                QuestionId: null,
                $"Dialog abgeschlossen mit {notification.Answers.Count} Antwort(en)."));

            return ValueTask.CompletedTask;
        }
    }
}
