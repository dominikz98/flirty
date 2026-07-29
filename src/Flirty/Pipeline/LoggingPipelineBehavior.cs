using System.Diagnostics;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Flirty.Pipeline;

/// <summary>
/// Mediator pipeline behavior that logs every message running through the mediator:
/// the start of processing, the successful completion (incl. duration) as well as errors.
/// </summary>
/// <typeparam name="TMessage">The message type (command, query or notification).</typeparam>
/// <typeparam name="TResponse">The response type expected by the message.</typeparam>
public sealed class LoggingPipelineBehavior<TMessage, TResponse> : IPipelineBehavior<TMessage, TResponse>
    where TMessage : notnull, IMessage
{
    private readonly ILogger<LoggingPipelineBehavior<TMessage, TResponse>> _logger;

    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="LoggingPipelineBehavior{TMessage, TResponse}"/> class.
    /// </summary>
    /// <param name="logger">The logger for the pipeline logging.</param>
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
        _logger.LogInformation("Mediator processes {MessageType}", messageType);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await next(message, cancellationToken);
            stopwatch.Stop();
            _logger.LogInformation(
                "Mediator processed {MessageType} in {ElapsedMilliseconds} ms",
                messageType,
                stopwatch.ElapsedMilliseconds);
            return response;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            _logger.LogError(
                exception,
                "Mediator aborted {MessageType} after {ElapsedMilliseconds} ms with an error",
                messageType,
                stopwatch.ElapsedMilliseconds);
            throw;
        }
    }
}
