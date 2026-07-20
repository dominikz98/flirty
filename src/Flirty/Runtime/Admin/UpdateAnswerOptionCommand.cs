using System.ComponentModel.DataAnnotations;
using Flirty.Persistence;
using Mediator;

namespace Flirty.Runtime.Admin;

/// <summary>
/// Aktualisiert die Antwortoption <see cref="OptionId"/> in der Frage <see cref="QuestionId"/>
/// (Dialog <see cref="DialogId"/>). Der fachliche Schlüssel muss innerhalb der Frage eindeutig bleiben.
/// </summary>
/// <param name="DialogId">Die Id des Dialogs, zu dem die Frage gehört.</param>
/// <param name="QuestionId">Die Id der Frage, zu der die Option gehört.</param>
/// <param name="OptionId">Der Primärschlüssel der zu ändernden Option.</param>
/// <param name="Key">Der fachliche, stabile Schlüssel der Option.</param>
/// <param name="Label">Der angezeigte Beschriftungstext der Option.</param>
/// <param name="Value">Der bei Auswahl gespeicherte Wert der Option.</param>
/// <param name="Order">Der Reihenfolge-Index der Option innerhalb der Frage.</param>
public sealed record UpdateAnswerOptionCommand(
    Guid DialogId,
    Guid QuestionId,
    Guid OptionId,
    [property: Required] string Key,
    [property: Required] string Label,
    [property: Required] string Value,
    int Order) : ICommand<AnswerOptionDetail>;

/// <summary>Handler für <see cref="UpdateAnswerOptionCommand"/>.</summary>
internal sealed class UpdateAnswerOptionCommandHandler
    : ICommandHandler<UpdateAnswerOptionCommand, AnswerOptionDetail>
{
    private readonly IDialogAdminStore _store;

    /// <summary>Erstellt den Handler über den angegebenen <see cref="IDialogAdminStore"/>.</summary>
    /// <param name="store">Das schreibende Repository für den Konfigurationsgraphen.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> ist <see langword="null"/>.</exception>
    public UpdateAnswerOptionCommandHandler(IDialogAdminStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    /// <exception cref="ConfigurationNotFoundException">
    /// Die Frage (im Dialog) oder die Option (in der Frage) existiert nicht.
    /// </exception>
    /// <exception cref="InvalidOperationException">In der Frage existiert bereits eine andere Option mit diesem Schlüssel.</exception>
    public async ValueTask<AnswerOptionDetail> Handle(
        UpdateAnswerOptionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var question = await _store.GetQuestionAsync(command.QuestionId, cancellationToken);
        if (question is null || question.DialogId != command.DialogId)
        {
            throw ConfigurationNotFoundException.ForQuestion(command.QuestionId);
        }

        var option = question.Options.FirstOrDefault(candidate => candidate.Id == command.OptionId)
            ?? throw ConfigurationNotFoundException.ForAnswerOption(command.OptionId);

        if (question.Options.Any(candidate => candidate.Id != command.OptionId && candidate.Key == command.Key))
        {
            throw new InvalidOperationException(
                $"In der Frage '{command.QuestionId}' existiert bereits eine andere Option mit dem Schlüssel '{command.Key}'.");
        }

        option.Key = command.Key;
        option.Label = command.Label;
        option.Value = command.Value;
        option.Order = command.Order;

        await _store.SaveChangesAsync(cancellationToken);

        return AdminProjection.ToDetail(option);
    }
}
