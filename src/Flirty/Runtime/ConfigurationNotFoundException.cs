using Flirty.Domain;

namespace Flirty.Runtime;

/// <summary>
/// Wird geworfen, wenn ein per Admin-CRUD adressiertes Element des Konfigurations-Aggregats
/// (<see cref="Dialog"/>, <see cref="Question"/>, <see cref="AnswerOption"/>, <see cref="Transition"/>
/// oder <see cref="LoopDefinition"/>) nicht zu seiner angegebenen Id existiert – oder ein Kind nicht zu dem
/// in der Route genannten Eltern-Element gehört. Der Endpunkt-Filter bildet diese Ausnahme auf
/// <c>404 Not Found</c> ab. Abzugrenzen von <see cref="DialogNotFoundException"/>, die den
/// Laufzeit-Fall „kein <b>veröffentlichter</b> Dialog zum Schlüssel" beschreibt.
/// </summary>
public sealed class ConfigurationNotFoundException : Exception
{
    /// <summary>Erstellt die Ausnahme ohne weitere Angaben.</summary>
    public ConfigurationNotFoundException()
    {
    }

    /// <summary>Erstellt die Ausnahme mit der angegebenen Meldung.</summary>
    /// <param name="message">Die Fehlermeldung, die die Ursache beschreibt.</param>
    public ConfigurationNotFoundException(string message)
        : base(message)
    {
    }

    /// <summary>Erstellt die Ausnahme mit Meldung und auslösender Ausnahme.</summary>
    /// <param name="message">Die Fehlermeldung, die die Ursache beschreibt.</param>
    /// <param name="innerException">Die Ausnahme, die diese Ausnahme ausgelöst hat.</param>
    public ConfigurationNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Erstellt eine <see cref="ConfigurationNotFoundException"/> für einen nicht gefundenen
    /// <see cref="Dialog"/> mit der angegebenen <paramref name="dialogId"/>.
    /// </summary>
    /// <param name="dialogId">Der Primärschlüssel des nicht gefundenen Dialogs.</param>
    /// <returns>Die vorbereitete Ausnahme.</returns>
    public static ConfigurationNotFoundException ForDialog(Guid dialogId)
        => new($"Kein Dialog mit der Id '{dialogId}' gefunden.");

    /// <summary>
    /// Erstellt eine <see cref="ConfigurationNotFoundException"/> für eine nicht gefundene
    /// <see cref="Question"/> mit der angegebenen <paramref name="questionId"/> (im adressierten Dialog).
    /// </summary>
    /// <param name="questionId">Der Primärschlüssel der nicht gefundenen Frage.</param>
    /// <returns>Die vorbereitete Ausnahme.</returns>
    public static ConfigurationNotFoundException ForQuestion(Guid questionId)
        => new($"Keine Frage mit der Id '{questionId}' im angegebenen Dialog gefunden.");

    /// <summary>
    /// Erstellt eine <see cref="ConfigurationNotFoundException"/> für eine nicht gefundene
    /// <see cref="AnswerOption"/> mit der angegebenen <paramref name="optionId"/> (in der adressierten Frage).
    /// </summary>
    /// <param name="optionId">Der Primärschlüssel der nicht gefundenen Antwortoption.</param>
    /// <returns>Die vorbereitete Ausnahme.</returns>
    public static ConfigurationNotFoundException ForAnswerOption(Guid optionId)
        => new($"Keine Antwortoption mit der Id '{optionId}' in der angegebenen Frage gefunden.");

    /// <summary>
    /// Erstellt eine <see cref="ConfigurationNotFoundException"/> für einen nicht gefundenen
    /// <see cref="Transition"/> mit der angegebenen <paramref name="transitionId"/> (im adressierten Dialog).
    /// </summary>
    /// <param name="transitionId">Der Primärschlüssel des nicht gefundenen Übergangs.</param>
    /// <returns>Die vorbereitete Ausnahme.</returns>
    public static ConfigurationNotFoundException ForTransition(Guid transitionId)
        => new($"Kein Übergang mit der Id '{transitionId}' im angegebenen Dialog gefunden.");

    /// <summary>
    /// Erstellt eine <see cref="ConfigurationNotFoundException"/> für einen nicht gefundenen
    /// <see cref="LoopDefinition"/> mit der angegebenen <paramref name="loopId"/> (im adressierten Dialog).
    /// </summary>
    /// <param name="loopId">Der Primärschlüssel des nicht gefundenen Schleifen-Markers.</param>
    /// <returns>Die vorbereitete Ausnahme.</returns>
    public static ConfigurationNotFoundException ForLoop(Guid loopId)
        => new($"Keine Schleife mit der Id '{loopId}' im angegebenen Dialog gefunden.");

    /// <summary>
    /// Erstellt eine <see cref="ConfigurationNotFoundException"/> für eine nicht gefundene
    /// <see cref="TriggerDefinition"/> mit der angegebenen <paramref name="triggerId"/> (im adressierten Dialog).
    /// </summary>
    /// <param name="triggerId">Der Primärschlüssel der nicht gefundenen Trigger-Definition.</param>
    /// <returns>Die vorbereitete Ausnahme.</returns>
    public static ConfigurationNotFoundException ForTrigger(Guid triggerId)
        => new($"Kein Trigger mit der Id '{triggerId}' im angegebenen Dialog gefunden.");
}
