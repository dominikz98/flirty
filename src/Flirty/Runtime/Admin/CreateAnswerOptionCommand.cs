using System.ComponentModel.DataAnnotations;
using Flirty.Domain;
using Flirty.Persistence;
using Mediator;

namespace Flirty.Runtime.Admin;

/// <summary>
/// Legt eine neue Antwortoption in der Frage <see cref="QuestionId"/> (Dialog <see cref="DialogId"/>)
/// an. Der fachliche Schlüssel muss innerhalb der Frage eindeutig sein.
/// </summary>
/// <param name="DialogId">Die Id des Dialogs, zu dem die Frage gehört.</param>
/// <param name="QuestionId">Die Id der Frage, zu der die Option gehört.</param>
/// <param name="Key">Der fachliche, stabile Schlüssel der Option.</param>
/// <param name="Label">Der angezeigte Beschriftungstext der Option.</param>
/// <param name="Value">Der bei Auswahl gespeicherte Wert der Option.</param>
/// <param name="Order">Der Reihenfolge-Index der Option innerhalb der Frage.</param>
public sealed record CreateAnswerOptionCommand(
    Guid DialogId,
    Guid QuestionId,
    [property: Required] string Key,
    [property: Required] string Label,
    [property: Required] string Value,
    int Order) : ICommand<AnswerOptionDetail>;

/// <summary>Handler für <see cref="CreateAnswerOptionCommand"/>.</summary>
internal sealed class CreateAnswerOptionCommandHandler
    : ICommandHandler<CreateAnswerOptionCommand, AnswerOptionDetail>
{
    private readonly IDialogAdminStore _store;

    /// <summary>Erstellt den Handler über den angegebenen <see cref="IDialogAdminStore"/>.</summary>
    /// <param name="store">Das schreibende Repository für den Konfigurationsgraphen.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> ist <see langword="null"/>.</exception>
    public CreateAnswerOptionCommandHandler(IDialogAdminStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    /// <exception cref="ConfigurationNotFoundException">
    /// Keine Frage mit der angegebenen Id im angegebenen Dialog existiert.
    /// </exception>
    /// <exception cref="InvalidOperationException">In der Frage existiert bereits eine Option mit diesem Schlüssel.</exception>
    public async ValueTask<AnswerOptionDetail> Handle(
        CreateAnswerOptionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var question = await _store.GetQuestionAsync(command.QuestionId, cancellationToken);
        if (question is null || question.DialogId != command.DialogId)
        {
            throw ConfigurationNotFoundException.ForQuestion(command.QuestionId);
        }

        if (question.Options.Any(option => option.Key == command.Key))
        {
            throw new InvalidOperationException(
                $"In der Frage '{command.QuestionId}' existiert bereits eine Option mit dem Schlüssel '{command.Key}'.");
        }

        var option = new AnswerOption
        {
            Id = Guid.NewGuid(),
            QuestionId = command.QuestionId,
            Key = command.Key,
            Label = command.Label,
            Value = command.Value,
            Order = command.Order,
        };

        _store.Add(option);
        await _store.SaveChangesAsync(cancellationToken);

        return AdminProjection.ToDetail(option);
    }
}
