namespace Flirty.Runtime;

/// <summary>
/// Thrown when no <see cref="Flirty.Domain.DialogSession"/> exists for a given session id
/// – for example when submitting an answer via <see cref="SubmitAnswerCommand"/> or
/// <see cref="IFlirtyEngine.SubmitAnswerAsync"/>.
/// </summary>
public sealed class SessionNotFoundException : Exception
{
    /// <summary>Creates the exception without further details.</summary>
    public SessionNotFoundException()
    {
    }

    /// <summary>Creates the exception with the given message.</summary>
    /// <param name="message">The error message describing the cause.</param>
    public SessionNotFoundException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and a triggering exception.</summary>
    /// <param name="message">The error message describing the cause.</param>
    /// <param name="innerException">The exception that caused this exception.</param>
    public SessionNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// The primary key of the session that was not found, or <see langword="null"/>
    /// if it is not known.
    /// </summary>
    public Guid? SessionId { get; init; }

    /// <summary>
    /// Creates a <see cref="SessionNotFoundException"/> for the given
    /// <paramref name="sessionId"/> along with a meaningful message.
    /// </summary>
    /// <param name="sessionId">The primary key of the session that could not be resolved.</param>
    /// <returns>The prepared exception with <see cref="SessionId"/> set.</returns>
    public static SessionNotFoundException ForId(Guid sessionId)
        => new($"No session with the id '{sessionId}' found.")
        {
            SessionId = sessionId,
        };
}
