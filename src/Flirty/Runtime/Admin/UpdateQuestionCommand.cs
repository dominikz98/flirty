using System.ComponentModel.DataAnnotations;
using Flirty.Domain;
using Flirty.Persistence;
using Mediator;

namespace Flirty.Runtime.Admin;

/// <summary>
/// Aktualisiert die Frage <see cref="QuestionId"/> im Dialog <see cref="DialogId"/> (In-Place). Der
/// fachliche Schlüssel muss innerhalb des Dialogs eindeutig bleiben.
/// </summary>
/// <param name="DialogId">Die Id des Dialogs, zu dem die Frage gehört.</param>
/// <param name="QuestionId">Der Primärschlüssel der zu ändernden Frage.</param>
/// <param name="Key">Der fachliche, stabile Schlüssel der Frage.</param>
/// <param name="Text">Der angezeigte Fragetext.</param>
/// <param name="Type">Der Antworttyp der Frage.</param>
/// <param name="Order">Der Reihenfolge-Index der Frage innerhalb des Dialogs.</param>
/// <param name="IsRequired">Gibt an, ob eine Antwort erforderlich ist.</param>
/// <param name="ValidationRules">Optionale Validierungsregeln als JSON.</param>
public sealed record UpdateQuestionCommand(
    Guid DialogId,
    Guid QuestionId,
    [property: Required] string Key,
    [property: Required] string Text,
    QuestionType Type,
    int Order,
    bool IsRequired,
    string? ValidationRules) : ICommand<QuestionDetail>;

/// <summary>Handler für <see cref="UpdateQuestionCommand"/>.</summary>
internal sealed class UpdateQuestionCommandHandler : ICommandHandler<UpdateQuestionCommand, QuestionDetail>
{
    private readonly IDialogAdminStore _store;

    /// <summary>Erstellt den Handler über den angegebenen <see cref="IDialogAdminStore"/>.</summary>
    /// <param name="store">Das schreibende Repository für den Konfigurationsgraphen.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> ist <see langword="null"/>.</exception>
    public UpdateQuestionCommandHandler(IDialogAdminStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    /// <exception cref="ConfigurationNotFoundException">
    /// Keine Frage mit der angegebenen Id im angegebenen Dialog existiert.
    /// </exception>
    /// <exception cref="InvalidOperationException">Im Dialog existiert bereits eine andere Frage mit diesem Schlüssel.</exception>
    public async ValueTask<QuestionDetail> Handle(UpdateQuestionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var question = await _store.GetQuestionAsync(command.QuestionId, cancellationToken);
        if (question is null || question.DialogId != command.DialogId)
        {
            throw ConfigurationNotFoundException.ForQuestion(command.QuestionId);
        }

        if (await _store.QuestionKeyExistsAsync(
                command.DialogId, command.Key, command.QuestionId, cancellationToken))
        {
            throw new InvalidOperationException(
                $"Im Dialog '{command.DialogId}' existiert bereits eine andere Frage mit dem Schlüssel '{command.Key}'.");
        }

        question.Key = command.Key;
        question.Text = command.Text;
        question.Type = command.Type;
        question.Order = command.Order;
        question.IsRequired = command.IsRequired;
        question.ValidationRules = command.ValidationRules;

        await _store.SaveChangesAsync(cancellationToken);

        return AdminProjection.ToDetail(question);
    }
}
