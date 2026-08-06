using System.ComponentModel.DataAnnotations;
using Flirty.Domain;
using Flirty.Persistence;
using Mediator;

namespace Flirty.Runtime.Admin;

/// <summary>
/// Updates the question <see cref="QuestionId"/> in the dialog <see cref="DialogId"/> (in place). The
/// business key must remain unique within the dialog.
/// </summary>
/// <param name="DialogId">The id of the dialog the question belongs to.</param>
/// <param name="QuestionId">The primary key of the question to change.</param>
/// <param name="Key">The business, stable key of the question.</param>
/// <param name="Text">The displayed question text.</param>
/// <param name="Type">The answer type of the question.</param>
/// <param name="Order">The order index of the question within the dialog.</param>
/// <param name="IsRequired">Indicates whether an answer is required.</param>
/// <param name="ValidationRules">Optional validation rules as JSON.</param>
/// <param name="CustomTypeKey">
/// Optional key of a host-declared custom question type. Only allowed together with
/// <see cref="QuestionType.Json"/>; on any other type the command is rejected.
/// </param>
public sealed record UpdateQuestionCommand(
    Guid DialogId,
    Guid QuestionId,
    [property: Required] string Key,
    [property: Required] string Text,
    QuestionType Type,
    int Order,
    bool IsRequired,
    string? ValidationRules,
    string? CustomTypeKey = null) : ICommand<QuestionDetail>, IValidatableObject
{
    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        => QuestionValidation.Validate(Type, CustomTypeKey);
}

/// <summary>Handler for <see cref="UpdateQuestionCommand"/>.</summary>
internal sealed class UpdateQuestionCommandHandler : ICommandHandler<UpdateQuestionCommand, QuestionDetail>
{
    private readonly IDialogAdminStore _store;

    /// <summary>Creates the handler over the given <see cref="IDialogAdminStore"/>.</summary>
    /// <param name="store">The writing repository for the configuration graph.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is <see langword="null"/>.</exception>
    public UpdateQuestionCommandHandler(IDialogAdminStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    /// <exception cref="ConfigurationNotFoundException">
    /// No question with the given id exists in the given dialog.
    /// </exception>
    /// <exception cref="InvalidOperationException">Another question with this key already exists in the dialog.</exception>
    /// <exception cref="DialogPublishedException">The dialog is published; its graph is locked.</exception>
    public async ValueTask<QuestionDetail> Handle(UpdateQuestionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // A published version is immutable (running sessions depend on it).
        await DialogEditGuard.EnsureEditableAsync(_store, command.DialogId, cancellationToken);

        var question = await _store.GetQuestionAsync(command.QuestionId, cancellationToken);
        if (question is null || question.DialogId != command.DialogId)
        {
            throw ConfigurationNotFoundException.ForQuestion(command.QuestionId);
        }

        if (await _store.QuestionKeyExistsAsync(
                command.DialogId, command.Key, command.QuestionId, cancellationToken))
        {
            throw new InvalidOperationException(
                $"Another question with the key '{command.Key}' already exists in the dialog '{command.DialogId}'.");
        }

        question.Key = command.Key;
        question.Text = command.Text;
        question.Type = command.Type;
        question.Order = command.Order;
        question.IsRequired = command.IsRequired;
        question.ValidationRules = command.ValidationRules;
        question.CustomTypeKey = command.CustomTypeKey;

        await _store.SaveChangesAsync(cancellationToken);

        return AdminProjection.ToDetail(question);
    }
}
