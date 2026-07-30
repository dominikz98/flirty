using System.ComponentModel.DataAnnotations;
using Flirty.Domain;
using Flirty.Persistence;
using Mediator;

namespace Flirty.Runtime.Admin;

/// <summary>
/// Creates a new (unpublished) dialog with the business key <see cref="Key"/>.
/// Version is set to <c>1</c> and <c>IsPublished</c> to <see langword="false"/>; the
/// entry question (<c>StartQuestionId</c>) stays open at first and is set via
/// <see cref="UpdateDialogCommand"/> as soon as questions exist.
/// </summary>
/// <param name="Key">The business, stable key of the dialog (must be unique).</param>
/// <param name="Name">The display name of the dialog.</param>
/// <param name="Description">The optional description of the dialog.</param>
public sealed record CreateDialogCommand(
    [property: Required] string Key,
    [property: Required] string Name,
    string? Description) : ICommand<DialogSummary>;

/// <summary>Handler for <see cref="CreateDialogCommand"/>.</summary>
internal sealed class CreateDialogCommandHandler : ICommandHandler<CreateDialogCommand, DialogSummary>
{
    private readonly IDialogAdminStore _store;

    /// <summary>Creates the handler over the given <see cref="IDialogAdminStore"/>.</summary>
    /// <param name="store">The writing repository for the configuration graph.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is <see langword="null"/>.</exception>
    public CreateDialogCommandHandler(IDialogAdminStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">A dialog with this key already exists.</exception>
    public async ValueTask<DialogSummary> Handle(CreateDialogCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await _store.DialogKeyExistsAsync(command.Key, cancellationToken: cancellationToken))
        {
            throw new InvalidOperationException(
                $"A dialog with the key '{command.Key}' already exists.");
        }

        var now = DateTimeOffset.UtcNow;
        var dialog = new Dialog
        {
            Id = Guid.NewGuid(),
            Key = command.Key,
            Name = command.Name,
            Description = command.Description,
            Version = 1,
            IsPublished = false,
            StartQuestionId = null,
            CreatedAt = now,
            UpdatedAt = now,
        };

        _store.Add(dialog);
        await _store.SaveChangesAsync(cancellationToken);

        return AdminProjection.ToSummary(dialog);
    }
}
