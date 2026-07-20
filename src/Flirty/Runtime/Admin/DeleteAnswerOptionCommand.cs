using Flirty.Persistence;
using Mediator;

namespace Flirty.Runtime.Admin;

/// <summary>
/// Löscht die Antwortoption <see cref="OptionId"/> in der Frage <see cref="QuestionId"/>
/// (Dialog <see cref="DialogId"/>).
/// </summary>
/// <param name="DialogId">Die Id des Dialogs, zu dem die Frage gehört.</param>
/// <param name="QuestionId">Die Id der Frage, zu der die Option gehört.</param>
/// <param name="OptionId">Der Primärschlüssel der zu löschenden Option.</param>
public sealed record DeleteAnswerOptionCommand(Guid DialogId, Guid QuestionId, Guid OptionId) : ICommand<Unit>;

/// <summary>Handler für <see cref="DeleteAnswerOptionCommand"/>.</summary>
internal sealed class DeleteAnswerOptionCommandHandler : ICommandHandler<DeleteAnswerOptionCommand, Unit>
{
    private readonly IDialogAdminStore _store;

    /// <summary>Erstellt den Handler über den angegebenen <see cref="IDialogAdminStore"/>.</summary>
    /// <param name="store">Das schreibende Repository für den Konfigurationsgraphen.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> ist <see langword="null"/>.</exception>
    public DeleteAnswerOptionCommandHandler(IDialogAdminStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    /// <exception cref="ConfigurationNotFoundException">
    /// Die Frage (im Dialog) oder die Option (in der Frage) existiert nicht.
    /// </exception>
    public async ValueTask<Unit> Handle(DeleteAnswerOptionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var question = await _store.GetQuestionAsync(command.QuestionId, cancellationToken);
        if (question is null || question.DialogId != command.DialogId)
        {
            throw ConfigurationNotFoundException.ForQuestion(command.QuestionId);
        }

        var option = question.Options.FirstOrDefault(candidate => candidate.Id == command.OptionId)
            ?? throw ConfigurationNotFoundException.ForAnswerOption(command.OptionId);

        _store.Remove(option);
        await _store.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
