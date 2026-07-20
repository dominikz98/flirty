using Flirty.Persistence;
using Mediator;

namespace Flirty.Runtime.Admin;

/// <summary>
/// Löscht die Frage <see cref="QuestionId"/> im Dialog <see cref="DialogId"/> samt ihren Optionen
/// (Datenbank-Cascade). Da <see cref="Flirty.Domain.Transition"/> die Fragen FK-los referenziert,
/// werden verweisende Übergänge (Ausgangs- oder Zielfrage) mit entfernt; zeigt die
/// Einstiegsfrage des Dialogs auf die gelöschte Frage, wird sie auf <see langword="null"/> gesetzt.
/// </summary>
/// <param name="DialogId">Die Id des Dialogs, zu dem die Frage gehört.</param>
/// <param name="QuestionId">Der Primärschlüssel der zu löschenden Frage.</param>
public sealed record DeleteQuestionCommand(Guid DialogId, Guid QuestionId) : ICommand<Unit>;

/// <summary>Handler für <see cref="DeleteQuestionCommand"/>.</summary>
internal sealed class DeleteQuestionCommandHandler : ICommandHandler<DeleteQuestionCommand, Unit>
{
    private readonly IDialogAdminStore _store;

    /// <summary>Erstellt den Handler über den angegebenen <see cref="IDialogAdminStore"/>.</summary>
    /// <param name="store">Das schreibende Repository für den Konfigurationsgraphen.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> ist <see langword="null"/>.</exception>
    public DeleteQuestionCommandHandler(IDialogAdminStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    /// <exception cref="ConfigurationNotFoundException">
    /// Keine Frage mit der angegebenen Id im angegebenen Dialog existiert.
    /// </exception>
    public async ValueTask<Unit> Handle(DeleteQuestionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var question = await _store.GetQuestionAsync(command.QuestionId, cancellationToken);
        if (question is null || question.DialogId != command.DialogId)
        {
            throw ConfigurationNotFoundException.ForQuestion(command.QuestionId);
        }

        // Verwaiste (FK-lose) Übergänge bereinigen, die diese Frage referenzieren.
        var referencingTransitions =
            await _store.GetTransitionsReferencingQuestionAsync(command.QuestionId, cancellationToken);
        if (referencingTransitions.Count > 0)
        {
            _store.RemoveRange(referencingTransitions);
        }

        // Einstiegsfrage zurücksetzen, falls sie auf die gelöschte Frage zeigt.
        var dialog = await _store.GetDialogAsync(command.DialogId, cancellationToken);
        if (dialog is not null && dialog.StartQuestionId == command.QuestionId)
        {
            dialog.StartQuestionId = null;
            dialog.UpdatedAt = DateTimeOffset.UtcNow;
        }

        _store.Remove(question);
        await _store.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
