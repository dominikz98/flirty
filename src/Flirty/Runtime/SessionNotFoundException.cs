namespace Flirty.Runtime;

/// <summary>
/// Wird geworfen, wenn zu einer angegebenen Session-Id keine <see cref="Flirty.Domain.DialogSession"/>
/// existiert – etwa beim Einreichen einer Antwort über <see cref="SubmitAnswerCommand"/> bzw.
/// <see cref="IFlirtyEngine.SubmitAnswerAsync"/>.
/// </summary>
public sealed class SessionNotFoundException : Exception
{
    /// <summary>Erstellt die Ausnahme ohne weitere Angaben.</summary>
    public SessionNotFoundException()
    {
    }

    /// <summary>Erstellt die Ausnahme mit der angegebenen Meldung.</summary>
    /// <param name="message">Die Fehlermeldung, die die Ursache beschreibt.</param>
    public SessionNotFoundException(string message)
        : base(message)
    {
    }

    /// <summary>Erstellt die Ausnahme mit Meldung und auslösender Ausnahme.</summary>
    /// <param name="message">Die Fehlermeldung, die die Ursache beschreibt.</param>
    /// <param name="innerException">Die Ausnahme, die diese Ausnahme ausgelöst hat.</param>
    public SessionNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Der Primärschlüssel der Session, die nicht gefunden wurde, oder <see langword="null"/>,
    /// wenn er nicht bekannt ist.
    /// </summary>
    public Guid? SessionId { get; init; }

    /// <summary>
    /// Erstellt eine <see cref="SessionNotFoundException"/> für die angegebene
    /// <paramref name="sessionId"/> samt aussagekräftiger Meldung.
    /// </summary>
    /// <param name="sessionId">Der Primärschlüssel der Session, die nicht aufgelöst werden konnte.</param>
    /// <returns>Die vorbereitete Ausnahme mit gesetztem <see cref="SessionId"/>.</returns>
    public static SessionNotFoundException ForId(Guid sessionId)
        => new($"Keine Session mit der Id '{sessionId}' gefunden.")
        {
            SessionId = sessionId,
        };
}
