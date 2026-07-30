using Mediator;

namespace Flirty.Runtime;

/// <summary>
/// Default implementation of <see cref="IFlirtyEngine"/>: a thin shell over
/// <see cref="ISender"/> that sends the runtime commands through the Mediator pipeline
/// (logging/validation).
/// </summary>
internal sealed class FlirtyEngine : IFlirtyEngine
{
    private readonly ISender _sender;

    /// <summary>Creates the facade over the given <see cref="ISender"/>.</summary>
    /// <param name="sender">The Mediator sender for dispatching the commands.</param>
    /// <exception cref="ArgumentNullException"><paramref name="sender"/> is <see langword="null"/>.</exception>
    public FlirtyEngine(ISender sender)
    {
        ArgumentNullException.ThrowIfNull(sender);
        _sender = sender;
    }

    /// <inheritdoc />
    public async Task<StartDialogResult> StartDialogAsync(
        string dialogKey, string externalUserKey, CancellationToken cancellationToken = default)
        => await _sender.Send(new StartDialogCommand(dialogKey, externalUserKey), cancellationToken);

    /// <inheritdoc />
    public async Task<StartDialogResult> StartDialogVersionAsync(
        Guid dialogId, string externalUserKey, CancellationToken cancellationToken = default)
        => await _sender.Send(new StartDialogVersionCommand(dialogId, externalUserKey), cancellationToken);

    /// <inheritdoc />
    public async Task<SubmitAnswerResult> SubmitAnswerAsync(
        Guid sessionId, Guid questionId, string value, CancellationToken cancellationToken = default)
        => await _sender.Send(new SubmitAnswerCommand(sessionId, questionId, value), cancellationToken);

    /// <inheritdoc />
    public async Task<ResumeDialogResult> ResumeDialogAsync(
        Guid sessionId, CancellationToken cancellationToken = default)
        => await _sender.Send(new ResumeDialogQuery(sessionId), cancellationToken);

    /// <inheritdoc />
    public async Task<EditAnswerResult> EditAnswerAsync(
        Guid sessionId, Guid questionId, string value, int? iterationIndex = null,
        CancellationToken cancellationToken = default)
        => await _sender.Send(
            new EditAnswerCommand(sessionId, questionId, value, iterationIndex), cancellationToken);
}
