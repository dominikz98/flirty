using System.ComponentModel.DataAnnotations;
using Flirty.Domain;
using Flirty.Expressions;
using Flirty.Persistence;
using Mediator;

namespace Flirty.Runtime;

/// <summary>
/// Reicht die Antwort <see cref="Value"/> auf die aktuell offene Frage <see cref="QuestionId"/> der
/// laufenden Session <see cref="SessionId"/> ein: Die Antwort wird persistiert, anschließend werden die
/// ausgehenden Übergänge der Frage ausgewertet und die Session auf die nächste Frage weitergeschaltet
/// bzw. abgeschlossen, wenn kein Übergang mehr greift.
/// </summary>
/// <param name="SessionId">Der Primärschlüssel der laufenden <see cref="DialogSession"/>.</param>
/// <param name="QuestionId">
/// Die Id der zu beantwortenden Frage. Sie muss der aktuell offenen Frage der Session
/// (<see cref="DialogSession.CurrentQuestionId"/>) entsprechen; das Bearbeiten früherer Antworten ist
/// dem <c>EditAnswerCommand</c> (#28) vorbehalten.
/// </param>
/// <param name="Value">
/// Der abgegebene Antwortwert als roher JSON-Text (Format abhängig vom Fragetyp, z. B. der
/// <see cref="AnswerOption.Value"/> einer Auswahl).
/// </param>
public sealed record SubmitAnswerCommand(
    [property: Required] Guid SessionId,
    [property: Required] Guid QuestionId,
    [property: Required] string Value) : ICommand<SubmitAnswerResult>;

/// <summary>
/// Handler für <see cref="SubmitAnswerCommand"/>: validiert Session und Frage, persistiert die Antwort,
/// wertet die Übergänge (Branching) über den <see cref="IExpressionEvaluator"/> aus und schaltet die
/// Session weiter oder schließt sie ab.
/// </summary>
internal sealed class SubmitAnswerCommandHandler : ICommandHandler<SubmitAnswerCommand, SubmitAnswerResult>
{
    private readonly IDialogStore _store;
    private readonly IExpressionEvaluator _evaluator;

    /// <summary>
    /// Erstellt den Handler über den angegebenen <see cref="IDialogStore"/> und
    /// <see cref="IExpressionEvaluator"/>.
    /// </summary>
    /// <param name="store">Das Repository für Dialoge und Sessions.</param>
    /// <param name="evaluator">Die Engine zur Auswertung der Übergangs-Bedingungsausdrücke.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="store"/> oder <paramref name="evaluator"/> ist <see langword="null"/>.
    /// </exception>
    public SubmitAnswerCommandHandler(IDialogStore store, IExpressionEvaluator evaluator)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(evaluator);
        _store = store;
        _evaluator = evaluator;
    }

    /// <inheritdoc />
    /// <exception cref="SessionNotFoundException">
    /// Keine Session mit der angegebenen <see cref="SubmitAnswerCommand.SessionId"/> existiert.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Die Session ist nicht mehr offen (<see cref="SessionStatus.InProgress"/>), die angegebene Frage
    /// ist nicht die aktuell offene, die gepinnte Dialogversion fehlt, oder das Branching ist
    /// fehlkonfiguriert (kein passender Übergang und kein Default bzw. unbekannte Zielfrage).
    /// </exception>
    public async ValueTask<SubmitAnswerResult> Handle(
        SubmitAnswerCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var session = await _store.GetSessionAsync(command.SessionId, cancellationToken)
            ?? throw SessionNotFoundException.ForId(command.SessionId);

        if (session.Status != SessionStatus.InProgress)
        {
            throw new InvalidOperationException(
                $"Die Session '{session.Id}' ist nicht offen (Status: {session.Status}) und nimmt keine Antworten an.");
        }

        if (command.QuestionId != session.CurrentQuestionId)
        {
            throw new InvalidOperationException(
                $"Die Frage '{command.QuestionId}' ist nicht die aktuell offene Frage "
                + $"('{session.CurrentQuestionId}') der Session '{session.Id}'.");
        }

        // Die von der Session gepinnte Dialogversion laden (unabhängig vom Veröffentlichungsstatus).
        var dialog = await _store.GetDialogAsync(session.DialogId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Die von Session '{session.Id}' gepinnte Dialogversion '{session.DialogId}' existiert nicht.");

        if (dialog.Questions.All(question => question.Id != command.QuestionId))
        {
            throw new InvalidOperationException(
                $"Die Frage '{command.QuestionId}' gehört nicht zum Dialog '{dialog.Key}'.");
        }

        PersistAnswer(dialog, session, command);

        var target = new TransitionResolver(_evaluator).ResolveTransitionTarget(dialog, session, command.QuestionId);
        if (target is null)
        {
            Complete(session);
            await _store.SaveChangesAsync(cancellationToken);
            return new SubmitAnswerResult(session.Id, IsCompleted: true, NextQuestion: null);
        }

        session.CurrentQuestionId = target;
        await _store.SaveChangesAsync(cancellationToken);
        return new SubmitAnswerResult(
            session.Id, IsCompleted: false, QuestionProjection.ResolveQuestion(dialog, target));
    }

    /// <summary>
    /// Hängt die eingereichte Antwort als neuen <see cref="SessionAnswer"/> an die getrackte Session an.
    /// Der Guid-Schlüssel wird bewusst nicht vorbelegt (store-generiert beim Speichern); die
    /// <see cref="SessionAnswer.Sequence"/> setzt die Reihenfolge innerhalb der Session fort. Liegt die
    /// Frage in einem Schleifen-Bereich, werden zusätzlich <see cref="SessionAnswer.LoopInstanceId"/> und
    /// <see cref="SessionAnswer.IterationIndex"/> über den <see cref="LoopResolver"/> gesetzt (die
    /// Zuordnung rechnet auf dem Vor-Zustand und muss daher vor dem Anhängen erfolgen); außerhalb einer
    /// Schleife bleiben beide <see langword="null"/>.
    /// </summary>
    private static void PersistAnswer(Dialog dialog, DialogSession session, SubmitAnswerCommand command)
    {
        var nextSequence = session.Answers.Count == 0
            ? 0
            : session.Answers.Max(answer => answer.Sequence) + 1;

        var assignment = new LoopResolver(dialog).ResolveAssignment(session, command.QuestionId);

        session.Answers.Add(new SessionAnswer
        {
            SessionId = session.Id,
            QuestionId = command.QuestionId,
            Value = command.Value,
            AnsweredAt = DateTimeOffset.UtcNow,
            Sequence = nextSequence,
            LoopInstanceId = assignment.LoopInstanceId,
            IterationIndex = assignment.IterationIndex,
        });
    }

    /// <summary>Schließt die Session ab: Status, Abschlusszeitpunkt und Löschen der offenen Frage.</summary>
    private static void Complete(DialogSession session)
    {
        session.Status = SessionStatus.Completed;
        session.CompletedAt = DateTimeOffset.UtcNow;
        session.CurrentQuestionId = null;
    }
}
