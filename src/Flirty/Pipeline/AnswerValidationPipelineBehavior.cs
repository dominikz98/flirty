using Flirty.Domain;
using Flirty.Persistence;
using Flirty.Runtime;
using Flirty.Validation;
using Mediator;

namespace Flirty.Pipeline;

/// <summary>
/// Mediator pipeline behavior that, for answer-submitting runtime commands
/// (<see cref="SubmitAnswerCommand"/>, <see cref="EditAnswerCommand"/> – recognized by the marker
/// <see cref="IAnswerCommand"/>), resolves the affected question of the pinned dialog version and
/// validates the answer value <b>before</b> the handler against the domain rules via <see cref="IAnswerValidator"/>. A
/// violation is rejected with an <see cref="AnswerValidationException"/> before the answer is
/// persisted or the path is recomputed (issue #30).
/// </summary>
/// <remarks>
/// <para>
/// Deliberately <b>internal</b> and registered via <c>AddFlirty()</c> <b>closed</b> per command type
/// (not open-generic): the behavior needs the scoped <see cref="IDialogStore"/> (and thus
/// a registered <c>FlirtyDbContext</c>). An open-generic registration would construct it for every
/// message – even where no <c>FlirtyDbContext</c> is present – and break the
/// resolution. As a scoped registration it shares the same context with the handler:
/// <see cref="IDialogStore.GetSessionAsync"/> returns tracked, so the handler gets the same instance
/// (no second query).
/// </para>
/// <para>
/// If the question cannot be resolved (session, pinned dialog or question missing) or the
/// value is empty, the behavior skips the validation and only calls <c>next</c> – the canonical
/// errors (<see cref="SessionNotFoundException"/>, DataAnnotations validation,
/// <see cref="InvalidOperationException"/>) remain solely the concern of the handler or the
/// <c>ValidationPipelineBehavior</c>.
/// </para>
/// </remarks>
/// <typeparam name="TMessage">The message type (command, query or notification).</typeparam>
/// <typeparam name="TResponse">The response type expected by the message.</typeparam>
internal sealed class AnswerValidationPipelineBehavior<TMessage, TResponse>
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : notnull, IMessage
{
    private readonly IDialogStore _store;
    private readonly IAnswerValidator _validator;

    /// <summary>
    /// Creates the behavior with the given <see cref="IDialogStore"/> and
    /// <see cref="IAnswerValidator"/>.
    /// </summary>
    /// <param name="store">The repository for resolving session and pinned dialog version.</param>
    /// <param name="validator">The domain answer validator.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="store"/> or <paramref name="validator"/> is <see langword="null"/>.
    /// </exception>
    public AnswerValidationPipelineBehavior(IDialogStore store, IAnswerValidator validator)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(validator);
        _store = store;
        _validator = validator;
    }

    /// <inheritdoc />
    /// <exception cref="AnswerValidationException">
    /// The answer value is invalid for the type or the rules of the resolved question.
    /// </exception>
    public async ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        if (message is IAnswerCommand answer && !string.IsNullOrEmpty(answer.Value))
        {
            var question = await ResolveQuestionAsync(answer, cancellationToken);
            if (question is not null)
            {
                var result = _validator.Validate(question, answer.Value);
                if (!result.IsValid)
                {
                    throw AnswerValidationException.For(question.Id, result.Errors);
                }
            }
        }

        return await next(message, cancellationToken);
    }

    /// <summary>
    /// Resolves the question addressed by the command via session → pinned dialog, or returns
    /// <see langword="null"/> if one of them is missing (then the behavior does not validate and leaves
    /// the canonical error to the handler).
    /// </summary>
    private async ValueTask<Question?> ResolveQuestionAsync(
        IAnswerCommand answer, CancellationToken cancellationToken)
    {
        var session = await _store.GetSessionAsync(answer.SessionId, cancellationToken);
        if (session is null)
        {
            return null;
        }

        var dialog = await _store.GetDialogAsync(session.DialogId, cancellationToken);
        return dialog?.Questions.FirstOrDefault(question => question.Id == answer.QuestionId);
    }
}
