using Flirty.Domain;
using Flirty.Runtime;
using Mediator;

namespace Flirty.Designer.Services;

/// <summary>
/// In-process handlers that write the four trigger notifications of the engine into the
/// <see cref="DesignerTriggerLog"/> of the running test run (#43).
/// </summary>
/// <remarks>
/// <para>
/// They are registered in <c>Program.cs</c> via <c>AddFlirtyHandler&lt;TNotification, THandler&gt;()</c>.
/// They thereby run in <b>every</b> scope of the designer – even outside a test run. That is
/// harmless: without an engine pass none of the notifications is published, and without a log passed through via
/// <see cref="DesignerTriggerLog.Adopt"/> they write into a throwaway instance of the
/// respective scope.
/// </para>
/// <para>
/// The assigned <see cref="TriggerScope"/> mirrors the assignment of the core <c>WebhookNotificationHandler</c>:
/// only so does the log match the <c>TriggerDefinition</c>s configured on the dialog that the runner
/// shows beside it.
/// </para>
/// </remarks>
internal static class DesignerTriggerLogHandlers
{
    /// <summary>Logs the start of a dialog (<see cref="TriggerScope.OnDialogStarted"/>).</summary>
    internal sealed class DialogStarted : INotificationHandler<DialogStartedNotification>
    {
        private readonly DesignerTriggerLog _log;

        /// <summary>Creates the handler.</summary>
        /// <param name="log">The log of the running test run.</param>
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

    /// <summary>Logs a captured answer (<see cref="TriggerScope.AfterAnswer"/>).</summary>
    internal sealed class AnswerSubmitted : INotificationHandler<AnswerSubmittedNotification>
    {
        private readonly DesignerTriggerLog _log;

        /// <summary>Creates the handler.</summary>
        /// <param name="log">The log of the running test run.</param>
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

    /// <summary>Logs the transition result (<see cref="TriggerScope.AfterQuestion"/>).</summary>
    internal sealed class QuestionAnswered : INotificationHandler<QuestionAnsweredNotification>
    {
        private readonly DesignerTriggerLog _log;

        /// <summary>Creates the handler.</summary>
        /// <param name="log">The log of the running test run.</param>
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

    /// <summary>Logs the completion of the dialog (<see cref="TriggerScope.OnDialogCompleted"/>).</summary>
    internal sealed class DialogCompleted : INotificationHandler<DialogCompletedNotification>
    {
        private readonly DesignerTriggerLog _log;

        /// <summary>Creates the handler.</summary>
        /// <param name="log">The log of the running test run.</param>
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
