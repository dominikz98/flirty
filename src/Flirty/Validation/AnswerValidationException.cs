using System.ComponentModel.DataAnnotations;

namespace Flirty.Validation;

/// <summary>
/// Wird geworfen, wenn eine eingereichte Antwort die fachliche Validierung
/// (<see cref="IAnswerValidator"/>) nicht besteht – etwa ein typwidriger Wert, eine unbekannte Auswahl
/// oder ein Regelverstoß (Länge/Bereich/Muster). Leitet von <see cref="ValidationException"/> ab,
/// damit Host-Apps sie zusammen mit den Pipeline-Validierungsfehlern (DataAnnotations) über
/// <c>catch (ValidationException)</c> behandeln können, und trägt zusätzlich die
/// <see cref="QuestionId"/> und die einzelnen <see cref="Errors"/>.
/// </summary>
public sealed class AnswerValidationException : ValidationException
{
    /// <summary>Erstellt die Ausnahme ohne weitere Angaben.</summary>
    public AnswerValidationException()
    {
    }

    /// <summary>Erstellt die Ausnahme mit der angegebenen Meldung.</summary>
    /// <param name="message">Die Fehlermeldung, die die Ursache beschreibt.</param>
    public AnswerValidationException(string message)
        : base(message)
    {
    }

    /// <summary>Erstellt die Ausnahme mit Meldung und auslösender Ausnahme.</summary>
    /// <param name="message">Die Fehlermeldung, die die Ursache beschreibt.</param>
    /// <param name="innerException">Die Ausnahme, die diese Ausnahme ausgelöst hat.</param>
    public AnswerValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Die Id der Frage, deren Antwort ungültig war, oder <see langword="null"/>, wenn sie nicht
    /// bekannt ist.
    /// </summary>
    public Guid? QuestionId { get; init; }

    /// <summary>Die einzelnen Verstöße (menschlesbar), die zur Ablehnung geführt haben.</summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>
    /// Erstellt eine <see cref="AnswerValidationException"/> für die angegebene
    /// <paramref name="questionId"/> mit den einzelnen <paramref name="errors"/> und einer daraus
    /// zusammengesetzten Meldung.
    /// </summary>
    /// <param name="questionId">Die Id der Frage, deren Antwort abgelehnt wurde.</param>
    /// <param name="errors">Die einzelnen Verstöße.</param>
    /// <returns>Die vorbereitete Ausnahme mit gesetzter <see cref="QuestionId"/> und <see cref="Errors"/>.</returns>
    public static AnswerValidationException For(Guid questionId, IReadOnlyList<string> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        return new AnswerValidationException(
            $"Die Antwort auf die Frage '{questionId}' ist ungültig: {string.Join("; ", errors)}")
        {
            QuestionId = questionId,
            Errors = [.. errors],
        };
    }
}
