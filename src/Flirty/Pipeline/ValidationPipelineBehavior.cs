using System.ComponentModel.DataAnnotations;
using Mediator;

namespace Flirty.Pipeline;

/// <summary>
/// Mediator pipeline behavior that checks incoming messages against their
/// <see cref="ValidationAttribute"/> annotations (System.ComponentModel.DataAnnotations)
/// and throws a <see cref="ValidationException"/> on violations before the handler is
/// called.
/// </summary>
/// <remarks>
/// Skeleton from issue #14. The domain answer validation (answer type + <c>ValidationRules</c>
/// via <c>IAnswerValidator</c>) follows separately in issue #30.
/// </remarks>
/// <typeparam name="TMessage">The message type (command, query or notification).</typeparam>
/// <typeparam name="TResponse">The response type expected by the message.</typeparam>
public sealed class ValidationPipelineBehavior<TMessage, TResponse> : IPipelineBehavior<TMessage, TResponse>
    where TMessage : notnull, IMessage
{
    /// <inheritdoc />
    public ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        var context = new ValidationContext(message);
        var results = new List<ValidationResult>();

        if (!Validator.TryValidateObject(message, context, results, validateAllProperties: true))
        {
            var errors = string.Join("; ", results.Select(result => result.ErrorMessage));
            throw new ValidationException(
                $"Validation of '{typeof(TMessage).Name}' failed: {errors}");
        }

        return next(message, cancellationToken);
    }
}
