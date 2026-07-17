using Mediator;

namespace Flirty.Samples;

/// <summary>
/// Eigener In-Process-<see cref="INotificationHandler{TNotification}"/> des Console-Samples: reagiert
/// auf eine <see cref="DialogCompletedNotification"/> und schreibt eine Abschluss-Zusammenfassung
/// (Dialog-Schlüssel und alle gegebenen Antworten) in den injizierten <see cref="TextWriter"/>.
/// </summary>
/// <remarks>
/// Demonstriert, wie eine Host-App eigene Reaktionen „in die Engine hängt". Der <see cref="TextWriter"/>
/// wird per DI bereitgestellt (in der App die Konsole, im Test ein <see cref="StringWriter"/>), sodass
/// die Auslösung des Handlers beobachtbar und testbar ist.
/// </remarks>
public sealed class ConsoleDialogCompletedHandler : INotificationHandler<DialogCompletedNotification>
{
    private readonly TextWriter _output;

    /// <summary>
    /// Initialisiert den Handler mit dem Ziel-<see cref="TextWriter"/> für die Ausgabe.
    /// </summary>
    /// <param name="output">Der Writer, in den die Abschluss-Zusammenfassung geschrieben wird.</param>
    public ConsoleDialogCompletedHandler(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);
        _output = output;
    }

    /// <summary>
    /// Behandelt die <see cref="DialogCompletedNotification"/>, indem eine Zusammenfassung des
    /// abgeschlossenen Dialogs samt Antworten ausgegeben wird.
    /// </summary>
    /// <param name="notification">Die ausgelöste Abschluss-Notification.</param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs (hier nicht benötigt).</param>
    /// <returns>Ein abgeschlossener <see cref="ValueTask"/>.</returns>
    public ValueTask Handle(DialogCompletedNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        _output.WriteLine($"[Handler] Dialog '{notification.DialogKey}' abgeschlossen (Session {notification.SessionId}).");
        foreach (var answer in notification.Answers)
        {
            _output.WriteLine($"[Handler]   {answer.QuestionKey} = {answer.Value}");
        }

        return ValueTask.CompletedTask;
    }
}
