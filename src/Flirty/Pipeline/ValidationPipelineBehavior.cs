using System.ComponentModel.DataAnnotations;
using Mediator;

namespace Flirty.Pipeline;

/// <summary>
/// Mediator-Pipeline-Behavior, das eingehende Nachrichten anhand ihrer
/// <see cref="ValidationAttribute"/>-Annotationen (System.ComponentModel.DataAnnotations)
/// prüft und bei Verstößen eine <see cref="ValidationException"/> wirft, bevor der Handler
/// aufgerufen wird.
/// </summary>
/// <remarks>
/// Skelett aus Issue #14. Die fachliche Antwort-Validierung (Antworttyp + <c>ValidationRules</c>
/// über <c>IAnswerValidator</c>) folgt separat in Issue #30.
/// </remarks>
/// <typeparam name="TMessage">Der Nachrichtentyp (Command, Query oder Notification).</typeparam>
/// <typeparam name="TResponse">Der von der Nachricht erwartete Antworttyp.</typeparam>
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
                $"Validierung von '{typeof(TMessage).Name}' fehlgeschlagen: {errors}");
        }

        return next(message, cancellationToken);
    }
}
