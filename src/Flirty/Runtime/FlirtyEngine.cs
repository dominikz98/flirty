using Mediator;

namespace Flirty.Runtime;

/// <summary>
/// Standardimplementierung von <see cref="IFlirtyEngine"/>: eine dünne Hülle über
/// <see cref="ISender"/>, die die Runtime-Commands durch die Mediator-Pipeline
/// (Logging/Validierung) sendet.
/// </summary>
internal sealed class FlirtyEngine : IFlirtyEngine
{
    private readonly ISender _sender;

    /// <summary>Erstellt die Facade über den angegebenen <see cref="ISender"/>.</summary>
    /// <param name="sender">Der Mediator-Sender zum Versenden der Commands.</param>
    /// <exception cref="ArgumentNullException"><paramref name="sender"/> ist <see langword="null"/>.</exception>
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
