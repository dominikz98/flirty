using System.ComponentModel.DataAnnotations;
using Flirty.Domain;
using Flirty.Persistence;
using Mediator;

namespace Flirty.Runtime.Admin;

/// <summary>
/// Creates a new answer option in the question <see cref="QuestionId"/> (dialog <see cref="DialogId"/>).
/// The business key must be unique within the question.
/// </summary>
/// <param name="DialogId">The id of the dialog the question belongs to.</param>
/// <param name="QuestionId">The id of the question the option belongs to.</param>
/// <param name="Key">The business, stable key of the option.</param>
/// <param name="Label">The displayed label text of the option.</param>
/// <param name="Value">The value of the option stored on selection.</param>
/// <param name="Order">The order index of the option within the question.</param>
public sealed record CreateAnswerOptionCommand(
    Guid DialogId,
    Guid QuestionId,
    [property: Required] string Key,
    [property: Required] string Label,
    [property: Required] string Value,
    int Order) : ICommand<AnswerOptionDetail>;

/// <summary>Handler for <see cref="CreateAnswerOptionCommand"/>.</summary>
internal sealed class CreateAnswerOptionCommandHandler
    : ICommandHandler<CreateAnswerOptionCommand, AnswerOptionDetail>
{
    private readonly IDialogAdminStore _store;

    /// <summary>Creates the handler over the given <see cref="IDialogAdminStore"/>.</summary>
    /// <param name="store">The writing repository for the configuration graph.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is <see langword="null"/>.</exception>
    public CreateAnswerOptionCommandHandler(IDialogAdminStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    /// <exception cref="ConfigurationNotFoundException">
    /// No question with the given id exists in the given dialog.
    /// </exception>
    /// <exception cref="InvalidOperationException">An option with this key already exists in the question.</exception>
    /// <exception cref="DialogPublishedException">The dialog is published; its graph is locked.</exception>
    public async ValueTask<AnswerOptionDetail> Handle(
        CreateAnswerOptionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // A published version is immutable (running sessions depend on it).
        await DialogEditGuard.EnsureEditableAsync(_store, command.DialogId, cancellationToken);

        var question = await _store.GetQuestionAsync(command.QuestionId, cancellationToken);
        if (question is null || question.DialogId != command.DialogId)
        {
            throw ConfigurationNotFoundException.ForQuestion(command.QuestionId);
        }

        if (question.Options.Any(option => option.Key == command.Key))
        {
            throw new InvalidOperationException(
                $"An option with the key '{command.Key}' already exists in the question '{command.QuestionId}'.");
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
