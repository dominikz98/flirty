using System.ComponentModel.DataAnnotations;
using Flirty.Domain;
using Flirty.Persistence;
using Mediator;

namespace Flirty.Runtime.Admin;

/// <summary>
/// Creates a new question in the dialog <see cref="DialogId"/>. The business key must be unique within
/// the dialog.
/// </summary>
/// <param name="DialogId">The id of the dialog the question belongs to.</param>
/// <param name="Key">The business, stable key of the question.</param>
/// <param name="Text">The displayed question text.</param>
/// <param name="Type">The answer type of the question.</param>
/// <param name="Order">The order index of the question within the dialog.</param>
/// <param name="IsRequired">Indicates whether an answer is required.</param>
/// <param name="ValidationRules">Optional validation rules as JSON.</param>
public sealed record CreateQuestionCommand(
    Guid DialogId,
    [property: Required] string Key,
    [property: Required] string Text,
    QuestionType Type,
    int Order,
    bool IsRequired,
    string? ValidationRules) : ICommand<QuestionDetail>;

/// <summary>Handler for <see cref="CreateQuestionCommand"/>.</summary>
internal sealed class CreateQuestionCommandHandler : ICommandHandler<CreateQuestionCommand, QuestionDetail>
{
    private readonly IDialogAdminStore _store;

    /// <summary>Creates the handler over the given <see cref="IDialogAdminStore"/>.</summary>
    /// <param name="store">The writing repository for the configuration graph.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is <see langword="null"/>.</exception>
    public CreateQuestionCommandHandler(IDialogAdminStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    /// <exception cref="ConfigurationNotFoundException">No dialog with the given id exists.</exception>
    /// <exception cref="InvalidOperationException">A question with this key already exists in the dialog.</exception>
    /// <exception cref="DialogPublishedException">The dialog is published; its graph is locked.</exception>
    public async ValueTask<QuestionDetail> Handle(CreateQuestionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var dialog = await _store.GetDialogAsync(command.DialogId, cancellationToken)
            ?? throw ConfigurationNotFoundException.ForDialog(command.DialogId);

        // A published version is immutable (running sessions depend on it).
        DialogEditGuard.EnsureEditable(dialog);

        if (await _store.QuestionKeyExistsAsync(command.DialogId, command.Key, cancellationToken: cancellationToken))
        {
            throw new InvalidOperationException(
                $"A question with the key '{command.Key}' already exists in the dialog '{command.DialogId}'.");
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
