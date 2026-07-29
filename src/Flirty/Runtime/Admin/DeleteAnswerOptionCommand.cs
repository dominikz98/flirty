using Flirty.Persistence;
using Mediator;

namespace Flirty.Runtime.Admin;

/// <summary>
/// Deletes the answer option <see cref="OptionId"/> in the question <see cref="QuestionId"/>
/// (dialog <see cref="DialogId"/>).
/// </summary>
/// <param name="DialogId">The id of the dialog the question belongs to.</param>
/// <param name="QuestionId">The id of the question the option belongs to.</param>
/// <param name="OptionId">The primary key of the option to delete.</param>
public sealed record DeleteAnswerOptionCommand(Guid DialogId, Guid QuestionId, Guid OptionId) : ICommand<Unit>;

/// <summary>Handler for <see cref="DeleteAnswerOptionCommand"/>.</summary>
internal sealed class DeleteAnswerOptionCommandHandler : ICommandHandler<DeleteAnswerOptionCommand, Unit>
{
    private readonly IDialogAdminStore _store;

    /// <summary>Creates the handler over the given <see cref="IDialogAdminStore"/>.</summary>
    /// <param name="store">The writing repository for the configuration graph.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is <see langword="null"/>.</exception>
    public DeleteAnswerOptionCommandHandler(IDialogAdminStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    /// <exception cref="ConfigurationNotFoundException">
    /// The question (in the dialog) or the option (in the question) does not exist.
    /// </exception>
    /// <exception cref="DialogPublishedException">The dialog is published; its graph is locked.</exception>
    public async ValueTask<Unit> Handle(DeleteAnswerOptionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // A published version is immutable (running sessions depend on it).
        await DialogEditGuard.EnsureEditableAsync(_store, command.DialogId, cancellationToken);

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
