using System.ComponentModel.DataAnnotations;
using Flirty.Persistence;
using Mediator;

namespace Flirty.Runtime.Admin;

/// <summary>
/// Updates the metadata of the dialog <see cref="Id"/> (in place). Optionally sets the entry question
/// (<see cref="StartQuestionId"/>); if it is not <see langword="null"/>, it must reference a question
/// of <b>this</b> dialog.
/// </summary>
/// <param name="Id">The primary key of the dialog to change.</param>
/// <param name="Key">The business, stable key of the dialog (must stay unique).</param>
/// <param name="Name">The display name of the dialog.</param>
/// <param name="Description">The optional description of the dialog.</param>
/// <param name="StartQuestionId">Optional reference to the entry question of this dialog.</param>
public sealed record UpdateDialogCommand(
    Guid Id,
    [property: Required] string Key,
    [property: Required] string Name,
    string? Description,
    Guid? StartQuestionId) : ICommand<DialogSummary>;

/// <summary>Handler for <see cref="UpdateDialogCommand"/>.</summary>
internal sealed class UpdateDialogCommandHandler : ICommandHandler<UpdateDialogCommand, DialogSummary>
{
    private readonly IDialogAdminStore _store;

    /// <summary>Creates the handler over the given <see cref="IDialogAdminStore"/>.</summary>
    /// <param name="store">The writing repository for the configuration graph.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is <see langword="null"/>.</exception>
    public UpdateDialogCommandHandler(IDialogAdminStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    /// <exception cref="ConfigurationNotFoundException">No dialog with the given id exists.</exception>
    /// <exception cref="InvalidOperationException">
    /// The key collides with another dialog family, the key of a family with multiple versions is to be
    /// renamed, or the given entry question does not belong to this dialog.
    /// </exception>
    /// <exception cref="DialogPublishedException">
    /// The dialog is published and the <b>entry question</b> is to be changed (the rest of the metadata
    /// stays editable).
    /// </exception>
    public async ValueTask<DialogSummary> Handle(UpdateDialogCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var dialog = await _store.GetDialogAsync(command.Id, cancellationToken)
            ?? throw ConfigurationNotFoundException.ForDialog(command.Id);

        // The entry question is part of the graph – locked on a published version.
        // Name/description stay deliberately editable (purely descriptive, no effect on the flow).
        if (dialog.StartQuestionId != command.StartQuestionId)
        {
            DialogEditGuard.EnsureEditable(dialog);
        }

        // The key identifies the dialog family: all versions share it. It therefore only needs checking
        // when it actually changes – otherwise every version would collide with its siblings.
        if (!string.Equals(command.Key, dialog.Key, StringComparison.Ordinal))
        {
            if (await _store.DialogKeyExistsAsync(command.Key, cancellationToken: cancellationToken))
            {
                throw new InvalidOperationException(
                    $"A different dialog with the key '{command.Key}' already exists.");
            }

            if (await _store.DialogKeyExistsAsync(dialog.Key, dialog.Id, cancellationToken))
            {
                throw new InvalidOperationException(
                    $"The key '{dialog.Key}' has multiple versions. Renaming would tear the version "
                  + "series apart – rename all versions instead, or create a new dialog family.");
            }
        }

        if (command.StartQuestionId is Guid startQuestionId)
        {
            var startQuestion = await _store.GetQuestionAsync(startQuestionId, cancellationToken);
            if (startQuestion is null || startQuestion.DialogId != dialog.Id)
            {
                throw new InvalidOperationException(
                    $"The entry question '{startQuestionId}' does not belong to the dialog '{dialog.Id}'.");
            }
        }

        dialog.Key = command.Key;
        dialog.Name = command.Name;
        dialog.Description = command.Description;
        dialog.StartQuestionId = command.StartQuestionId;
        dialog.UpdatedAt = DateTimeOffset.UtcNow;

        await _store.SaveChangesAsync(cancellationToken);

        return AdminProjection.ToSummary(dialog);
    }
}
