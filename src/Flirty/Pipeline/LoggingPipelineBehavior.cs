using System.Diagnostics;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Flirty.Pipeline;

/// <summary>
/// Mediator-Pipeline-Behavior, das jede über den Mediator laufende Nachricht protokolliert:
/// den Beginn der Verarbeitung, den erfolgreichen Abschluss (inkl. Dauer) sowie Fehler.
/// </summary>
/// <typeparam name="TMessage">Der Nachrichtentyp (Command, Query oder Notification).</typeparam>
/// <typeparam name="TResponse">Der von der Nachricht erwartete Antworttyp.</typeparam>
public sealed class LoggingPipelineBehavior<TMessage, TResponse> : IPipelineBehavior<TMessage, TResponse>
    where TMessage : notnull, IMessage
{
    private readonly ILogger<LoggingPipelineBehavior<TMessage, TResponse>> _logger;

    /// <summary>
    /// Initialisiert eine neue Instanz der
    /// <see cref="LoggingPipelineBehavior{TMessage, TResponse}"/>-Klasse.
    /// </summary>
    /// <param name="logger">Der Logger für die Pipeline-Protokollierung.</param>
    public LoggingPipelineBehavior(ILogger<LoggingPipelineBehavior<TMessage, TResponse>> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        var messageType = typeof(TMessage).Name;
        _logger.LogInformation("Mediator verarbeitet {MessageType}", messageType);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await next(message, cancellationToken);
            stopwatch.Stop();
            _logger.LogInformation(
                "Mediator hat {MessageType} in {ElapsedMilliseconds} ms verarbeitet",
                messageType,
                stopwatch.ElapsedMilliseconds);
            return response;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            _logger.LogError(
                exception,
                "Mediator hat {MessageType} nach {ElapsedMilliseconds} ms mit Fehler abgebrochen",
                messageType,
                stopwatch.ElapsedMilliseconds);
            throw;
        }
    }
}
