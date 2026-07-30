using System.ComponentModel.DataAnnotations;
using Flirty.Persistence;
using Mediator;

namespace Flirty.Runtime.Admin;

/// <summary>
/// Updates the answer option <see cref="OptionId"/> in the question <see cref="QuestionId"/>
/// (dialog <see cref="DialogId"/>). The business key must remain unique within the question.
/// </summary>
/// <param name="DialogId">The id of the dialog the question belongs to.</param>
/// <param name="QuestionId">The id of the question the option belongs to.</param>
/// <param name="OptionId">The primary key of the option to change.</param>
/// <param name="Key">The business, stable key of the option.</param>
/// <param name="Label">The displayed label text of the option.</param>
/// <param name="Value">The value of the option stored on selection.</param>
/// <param name="Order">The order index of the option within the question.</param>
public sealed record UpdateAnswerOptionCommand(
    Guid DialogId,
    Guid QuestionId,
    Guid OptionId,
    [property: Required] string Key,
    [property: Required] string Label,
    [property: Required] string Value,
    int Order) : ICommand<AnswerOptionDetail>;

/// <summary>Handler for <see cref="UpdateAnswerOptionCommand"/>.</summary>
internal sealed class UpdateAnswerOptionCommandHandler
    : ICommandHandler<UpdateAnswerOptionCommand, AnswerOptionDetail>
{
    private readonly IDialogAdminStore _store;

    /// <summary>Creates the handler over the given <see cref="IDialogAdminStore"/>.</summary>
    /// <param name="store">The writing repository for the configuration graph.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is <see langword="null"/>.</exception>
    public UpdateAnswerOptionCommandHandler(IDialogAdminStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    /// <exception cref="ConfigurationNotFoundException">
    /// The question (in the dialog) or the option (in the question) does not exist.
    /// </exception>
    /// <exception cref="InvalidOperationException">Another option with this key already exists in the question.</exception>
    /// <exception cref="DialogPublishedException">The dialog is published; its graph is locked.</exception>
    public async ValueTask<AnswerOptionDetail> Handle(
        UpdateAnswerOptionCommand command, CancellationToken cancellationToken)
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

        if (question.Options.Any(candidate => candidate.Id != command.OptionId && candidate.Key == command.Key))
        {
            throw new InvalidOperationException(
                $"Another option with the key '{command.Key}' already exists in the question '{command.QuestionId}'.");
        }

        option.Key = command.Key;
        option.Label = command.Label;
        option.Value = command.Value;
        option.Order = command.Order;

        await _store.SaveChangesAsync(cancellationToken);

        return AdminProjection.ToDetail(option);
    }
}
