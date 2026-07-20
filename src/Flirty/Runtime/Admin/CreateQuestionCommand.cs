using System.ComponentModel.DataAnnotations;
using Flirty.Domain;
using Flirty.Persistence;
using Mediator;

namespace Flirty.Runtime.Admin;

/// <summary>
/// Legt eine neue Frage im Dialog <see cref="DialogId"/> an. Der fachliche Schlüssel muss innerhalb
/// des Dialogs eindeutig sein.
/// </summary>
/// <param name="DialogId">Die Id des Dialogs, zu dem die Frage gehört.</param>
/// <param name="Key">Der fachliche, stabile Schlüssel der Frage.</param>
/// <param name="Text">Der angezeigte Fragetext.</param>
/// <param name="Type">Der Antworttyp der Frage.</param>
/// <param name="Order">Der Reihenfolge-Index der Frage innerhalb des Dialogs.</param>
/// <param name="IsRequired">Gibt an, ob eine Antwort erforderlich ist.</param>
/// <param name="ValidationRules">Optionale Validierungsregeln als JSON.</param>
public sealed record CreateQuestionCommand(
    Guid DialogId,
    [property: Required] string Key,
    [property: Required] string Text,
    QuestionType Type,
    int Order,
    bool IsRequired,
    string? ValidationRules) : ICommand<QuestionDetail>;

/// <summary>Handler für <see cref="CreateQuestionCommand"/>.</summary>
internal sealed class CreateQuestionCommandHandler : ICommandHandler<CreateQuestionCommand, QuestionDetail>
{
    private readonly IDialogAdminStore _store;

    /// <summary>Erstellt den Handler über den angegebenen <see cref="IDialogAdminStore"/>.</summary>
    /// <param name="store">Das schreibende Repository für den Konfigurationsgraphen.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> ist <see langword="null"/>.</exception>
    public CreateQuestionCommandHandler(IDialogAdminStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    /// <exception cref="ConfigurationNotFoundException">Kein Dialog mit der angegebenen Id existiert.</exception>
    /// <exception cref="InvalidOperationException">Im Dialog existiert bereits eine Frage mit diesem Schlüssel.</exception>
    public async ValueTask<QuestionDetail> Handle(CreateQuestionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        _ = await _store.GetDialogAsync(command.DialogId, cancellationToken)
            ?? throw ConfigurationNotFoundException.ForDialog(command.DialogId);

        if (await _store.QuestionKeyExistsAsync(command.DialogId, command.Key, cancellationToken: cancellationToken))
        {
            throw new InvalidOperationException(
                $"Im Dialog '{command.DialogId}' existiert bereits eine Frage mit dem Schlüssel '{command.Key}'.");
        }

        var question = new Question
        {
            Id = Guid.NewGuid(),
            DialogId = command.DialogId,
            Key = command.Key,
            Text = command.Text,
            Type = command.Type,
            Order = command.Order,
            IsRequired = command.IsRequired,
            ValidationRules = command.ValidationRules,
        };

        _store.Add(question);
        await _store.SaveChangesAsync(cancellationToken);

        return AdminProjection.ToDetail(question);
    }
}
