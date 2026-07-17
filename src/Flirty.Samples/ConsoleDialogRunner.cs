using Flirty.Runtime;
using Mediator;

namespace Flirty.Samples;

/// <summary>
/// Spielt einen Dialog vollständig über die Facade <see cref="IFlirtyEngine"/> durch: startet den
/// Dialog, präsentiert jede Frage, reicht die von der <see cref="IAnswerSource"/> gelieferte Antwort
/// ein und folgt dem Branching bis zum Abschluss. Beim Abschluss publiziert die Engine selbst die
/// <see cref="DialogCompletedNotification"/>, sodass registrierte eigene
/// <see cref="INotificationHandler{TNotification}"/> automatisch benachrichtigt werden.
/// </summary>
/// <remarks>
/// Die Ein-/Ausgabe ist bewusst über <see cref="IAnswerSource"/> und einen <see cref="TextWriter"/>
/// abstrahiert, damit derselbe Ablauf interaktiv (Konsole) wie auch deterministisch (Test) läuft. Das
/// engine-getriebene Publizieren der Trigger-Notifications (seit EPIC 4) macht ein manuelles Auflösen und
/// Aufrufen der Handler im Runner überflüssig – der Host registriert seinen Handler nur noch per DI.
/// </remarks>
public sealed class ConsoleDialogRunner
{
    private readonly IFlirtyEngine _engine;
    private readonly IAnswerSource _answers;
    private readonly TextWriter _output;

    /// <summary>
    /// Initialisiert den Runner mit der Engine-Facade, der Antwortquelle und dem Ausgabe-Writer.
    /// </summary>
    /// <param name="engine">Die Dialog-Facade der Flirty-Engine.</param>
    /// <param name="answers">Die Quelle der Antworten (interaktiv oder skriptgesteuert).</param>
    /// <param name="output">Der Writer für die Frage-/Ablauf-Ausgabe.</param>
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
    /// Startet den Dialog mit dem angegebenen Schlüssel und spielt ihn bis zum Abschluss durch.
    /// </summary>
    /// <param name="dialogKey">Der fachliche Schlüssel des zu startenden Dialogs.</param>
    /// <param name="externalUserKey">Der fachliche Anwenderschlüssel der Host-App.</param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns>Das Ergebnis des Durchlaufs (Session, Abschluss-Flag und gestellte Fragen in Reihenfolge).</returns>
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

        // Abschluss: Die Engine hat beim letzten SubmitAnswer die DialogCompletedNotification bereits
        // publiziert und damit die registrierten eigenen Handler ausgelöst – der Runner muss nichts tun.
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
/// Ergebnis eines <see cref="ConsoleDialogRunner.RunAsync"/>-Durchlaufs.
/// </summary>
/// <param name="SessionId">Der Primärschlüssel der durchlaufenen Session.</param>
/// <param name="Completed"><see langword="true"/>, wenn der Dialog abgeschlossen wurde.</param>
/// <param name="AskedQuestionKeys">Die Schlüssel der gestellten Fragen in Reihenfolge des Ablaufs.</param>
public sealed record DialogRunResult(
    Guid SessionId,
    bool Completed,
    IReadOnlyList<string> AskedQuestionKeys);
