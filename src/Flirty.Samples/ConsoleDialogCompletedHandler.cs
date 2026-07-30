using Flirty.Runtime;
using Mediator;

namespace Flirty.Samples;

/// <summary>
/// The console sample's own in-process <see cref="INotificationHandler{TNotification}"/>: reacts to the
/// <see cref="DialogCompletedNotification"/> published by the engine and writes a completion summary
/// (dialog key and all given answers) to the injected <see cref="TextWriter"/>.
/// </summary>
/// <remarks>
/// Demonstrates how a host app "hooks its own reactions into the engine": the handler is merely
/// registered via DI and is called automatically by the engine on dialog completion via
/// <see cref="IPublisher"/> (since EPIC 4). The <see cref="TextWriter"/> is provided via DI (in the app
/// the console, in the test a <see cref="StringWriter"/>), so that the handler's invocation is
/// observable and testable.
/// </remarks>
public sealed class ConsoleDialogCompletedHandler : INotificationHandler<DialogCompletedNotification>
{
    private readonly TextWriter _output;

    /// <summary>
    /// Initializes the handler with the target <see cref="TextWriter"/> for the output.
    /// </summary>
    /// <param name="output">The writer to which the completion summary is written.</param>
    public ConsoleDialogCompletedHandler(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);
        _output = output;
    }

    /// <summary>
    /// Handles the <see cref="DialogCompletedNotification"/> by writing a summary of the completed
    /// dialog together with its answers.
    /// </summary>
    /// <param name="notification">The raised completion notification.</param>
    /// <param name="cancellationToken">Token to cancel the operation (not needed here).</param>
    /// <returns>A completed <see cref="ValueTask"/>.</returns>
    public ValueTask Handle(DialogCompletedNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        _output.WriteLine($"[Handler] Dialog '{notification.DialogKey}' completed (session {notification.SessionId}).");
        foreach (var answer in notification.Answers)
        {
            _output.WriteLine($"[Handler]   {answer.QuestionKey} = {answer.Value}");
        }

        return ValueTask.CompletedTask;
    }
}
