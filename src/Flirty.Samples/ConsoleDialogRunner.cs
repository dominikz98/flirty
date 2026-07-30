using Flirty.Runtime;
using Mediator;

namespace Flirty.Samples;

/// <summary>
/// Plays a dialog through completely via the facade <see cref="IFlirtyEngine"/>: starts the dialog,
/// presents each question, submits the answer supplied by the <see cref="IAnswerSource"/> and follows
/// the branching to completion. On completion the engine itself publishes the
/// <see cref="DialogCompletedNotification"/>, so that registered custom
/// <see cref="INotificationHandler{TNotification}"/> instances are notified automatically.
/// </summary>
/// <remarks>
/// Input/output is deliberately abstracted via <see cref="IAnswerSource"/> and a <see cref="TextWriter"/>,
/// so that the same flow runs interactively (console) as well as deterministically (test). The
/// engine-driven publishing of the trigger notifications (since EPIC 4) makes it unnecessary to resolve
/// and invoke the handlers manually in the runner – the host only registers its handler via DI.
/// </remarks>
public sealed class ConsoleDialogRunner
{
    private readonly IFlirtyEngine _engine;
    private readonly IAnswerSource _answers;
    private readonly TextWriter _output;

    /// <summary>
    /// Initializes the runner with the engine facade, the answer source and the output writer.
    /// </summary>
    /// <param name="engine">The dialog facade of the Flirty engine.</param>
    /// <param name="answers">The source of the answers (interactive or scripted).</param>
    /// <param name="output">The writer for the question/flow output.</param>
    public ConsoleDialogRunner(
        IFlirtyEngine engine,
        IAnswerSource answers,
        TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(answers);
        ArgumentNullException.ThrowIfNull(output);

        _engine = engine;
        _answers = answers;
        _output = output;
    }

    /// <summary>
    /// Starts the dialog with the given key and plays it through to completion.
    /// </summary>
    /// <param name="dialogKey">The business key of the dialog to start.</param>
    /// <param name="externalUserKey">The host app's business user key.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The result of the run (session, completion flag and posed questions in order).</returns>
    public async Task<DialogRunResult> RunAsync(
        string dialogKey, string externalUserKey, CancellationToken cancellationToken = default)
    {
        var start = await _engine.StartDialogAsync(dialogKey, externalUserKey, cancellationToken);
        var sessionId = start.SessionId;
        var current = start.CurrentQuestion;
        var askedQuestionKeys = new List<string>();
        var completed = false;

        while (true)
        {
            Present(current);
            askedQuestionKeys.Add(current.Key);

            var value = _answers.GetAnswer(current);
            var result = await _engine.SubmitAnswerAsync(sessionId, current.Id, value, cancellationToken);

            if (result.IsCompleted || result.NextQuestion is null)
            {
                completed = result.IsCompleted;
                break;
            }

            current = result.NextQuestion;
        }

        // Completion: on the last SubmitAnswer the engine has already published the
        // DialogCompletedNotification and thereby triggered the registered custom handlers – the runner
        // has nothing to do.
        return new DialogRunResult(sessionId, completed, askedQuestionKeys);
    }

    private void Present(QuestionView question)
    {
        _output.WriteLine(question.Text);
        foreach (var option in question.Options)
        {
            _output.WriteLine($"  [{option.Key}] {option.Label}");
        }
    }
}

/// <summary>
/// Result of a <see cref="ConsoleDialogRunner.RunAsync"/> run.
/// </summary>
/// <param name="SessionId">The primary key of the run session.</param>
/// <param name="Completed"><see langword="true"/> if the dialog was completed.</param>
/// <param name="AskedQuestionKeys">The keys of the posed questions in the order of the flow.</param>
public sealed record DialogRunResult(
    Guid SessionId,
    bool Completed,
    IReadOnlyList<string> AskedQuestionKeys);
