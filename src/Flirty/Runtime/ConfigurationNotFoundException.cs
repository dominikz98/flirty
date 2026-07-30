using Flirty.Domain;

namespace Flirty.Runtime;

/// <summary>
/// Thrown when an element of the configuration aggregate addressed via admin CRUD
/// (<see cref="Dialog"/>, <see cref="Question"/>, <see cref="AnswerOption"/>, <see cref="Transition"/>
/// or <see cref="LoopDefinition"/>) does not exist for its given id – or a child does not belong to the
/// parent element named in the route. The endpoint filter maps this exception to
/// <c>404 Not Found</c>. To be distinguished from <see cref="DialogNotFoundException"/>, which describes
/// the runtime case "no <b>published</b> dialog for the key".
/// </summary>
public sealed class ConfigurationNotFoundException : Exception
{
    /// <summary>Creates the exception without further details.</summary>
    public ConfigurationNotFoundException()
    {
    }

    /// <summary>Creates the exception with the given message.</summary>
    /// <param name="message">The error message describing the cause.</param>
    public ConfigurationNotFoundException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and a triggering exception.</summary>
    /// <param name="message">The error message describing the cause.</param>
    /// <param name="innerException">The exception that caused this exception.</param>
    public ConfigurationNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Creates a <see cref="ConfigurationNotFoundException"/> for a <see cref="Dialog"/> not found
    /// with the given <paramref name="dialogId"/>.
    /// </summary>
    /// <param name="dialogId">The primary key of the dialog that was not found.</param>
    /// <returns>The prepared exception.</returns>
    public static ConfigurationNotFoundException ForDialog(Guid dialogId)
        => new($"No dialog with the id '{dialogId}' found.");

    /// <summary>
    /// Creates a <see cref="ConfigurationNotFoundException"/> for a <see cref="Question"/> not found
    /// with the given <paramref name="questionId"/> (in the addressed dialog).
    /// </summary>
    /// <param name="questionId">The primary key of the question that was not found.</param>
    /// <returns>The prepared exception.</returns>
    public static ConfigurationNotFoundException ForQuestion(Guid questionId)
        => new($"No question with the id '{questionId}' found in the given dialog.");

    /// <summary>
    /// Creates a <see cref="ConfigurationNotFoundException"/> for an <see cref="AnswerOption"/> not found
    /// with the given <paramref name="optionId"/> (in the addressed question).
    /// </summary>
    /// <param name="optionId">The primary key of the answer option that was not found.</param>
    /// <returns>The prepared exception.</returns>
    public static ConfigurationNotFoundException ForAnswerOption(Guid optionId)
        => new($"No answer option with the id '{optionId}' found in the given question.");

    /// <summary>
    /// Creates a <see cref="ConfigurationNotFoundException"/> for a <see cref="Transition"/> not found
    /// with the given <paramref name="transitionId"/> (in the addressed dialog).
    /// </summary>
    /// <param name="transitionId">The primary key of the transition that was not found.</param>
    /// <returns>The prepared exception.</returns>
    public static ConfigurationNotFoundException ForTransition(Guid transitionId)
        => new($"No transition with the id '{transitionId}' found in the given dialog.");

    /// <summary>
    /// Creates a <see cref="ConfigurationNotFoundException"/> for a <see cref="LoopDefinition"/> not found
    /// with the given <paramref name="loopId"/> (in the addressed dialog).
    /// </summary>
    /// <param name="loopId">The primary key of the loop marker that was not found.</param>
    /// <returns>The prepared exception.</returns>
    public static ConfigurationNotFoundException ForLoop(Guid loopId)
        => new($"No loop with the id '{loopId}' found in the given dialog.");

    /// <summary>
    /// Creates a <see cref="ConfigurationNotFoundException"/> for a <see cref="TriggerDefinition"/> not found
    /// with the given <paramref name="triggerId"/> (in the addressed dialog).
    /// </summary>
    /// <param name="triggerId">The primary key of the trigger definition that was not found.</param>
    /// <returns>The prepared exception.</returns>
    public static ConfigurationNotFoundException ForTrigger(Guid triggerId)
        => new($"No trigger with the id '{triggerId}' found in the given dialog.");
}
