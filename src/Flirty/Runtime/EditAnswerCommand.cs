using System.ComponentModel.DataAnnotations;
using Flirty.Domain;
using Flirty.Expressions;
using Flirty.Persistence;
using Mediator;

namespace Flirty.Runtime;

/// <summary>
/// Editiert die bereits gegebene Antwort auf eine frühere Frage <see cref="QuestionId"/> der Session
/// <see cref="SessionId"/>: Der bestehende Antwortwert wird durch <see cref="Value"/> <b>überschrieben</b>,
/// alle <b>nachgelagerten</b> Antworten (die nach der editierten Frage gegebenen) werden verworfen
/// (invalidiert) und der Pfad wird ab der editierten Frage über die Übergänge (Branching) <b>neu
/// berechnet</b>. Eine bereits abgeschlossene Session wird dabei wieder geöffnet, sofern die
/// Neuberechnung auf eine nicht-terminale Folgefrage führt.
/// </summary>
/// <param name="SessionId">Der Primärschlüssel der <see cref="DialogSession"/>, deren Antwort editiert wird.</param>
/// <param name="QuestionId">
/// Die Id der Frage, deren Antwort überschrieben werden soll. Sie muss zum gepinnten Dialog gehören und
/// in dieser Session bereits beantwortet worden sein (im Gegensatz zu <see cref="SubmitAnswerCommand"/>
/// muss sie <b>nicht</b> die aktuell offene Frage sein).
/// </param>
/// <param name="Value">
/// Der neue Antwortwert als roher JSON-Text (Format abhängig vom Fragetyp, z. B. der
/// <see cref="AnswerOption.Value"/> einer Auswahl).
/// </param>
/// <param name="IterationIndex">
/// Optionaler nullbasierter Iterationsindex, um innerhalb einer Schleife gezielt die Antwort einer
/// bestimmten Iteration zu editieren (eine Frage kann je Iteration eine Antwort tragen). Bleibt er
/// <see langword="null"/>, wird – wie außerhalb von Schleifen – die früheste Antwort der Frage editiert
/// (Iteration 0 bei einer Loop-Frage).
/// </param>
public sealed record EditAnswerCommand(
    [property: Required] Guid SessionId,
    [property: Required] Guid QuestionId,
    [property: Required] string Value,
    int? IterationIndex = null) : ICommand<EditAnswerResult>, IAnswerCommand;

/// <summary>
/// Handler für <see cref="EditAnswerCommand"/>: überschreibt die bestehende Antwort, invalidiert die
/// nachgelagerten Antworten und berechnet den Pfad ab der editierten Frage über den
/// <see cref="TransitionResolver"/> neu (Weiterschalten, Abschluss oder Wieder-Öffnen der Session).
/// </summary>
internal sealed class EditAnswerCommandHandler : ICommandHandler<EditAnswerCommand, EditAnswerResult>
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
    public EditAnswerCommandHandler(IDialogStore store, IExpressionEvaluator evaluator)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(evaluator);
        _store = store;
        _evaluator = evaluator;
    }

    /// <inheritdoc />
    /// <exception cref="SessionNotFoundException">
    /// Keine Session mit der angegebenen <see cref="EditAnswerCommand.SessionId"/> existiert.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Die Session ist abgebrochen (<see cref="SessionStatus.Abandoned"/>), die gepinnte Dialogversion
    /// fehlt, die Frage gehört nicht zum Dialog, die Frage (bzw. die angegebene Iteration) wurde in dieser
    /// Session noch nicht beantwortet, oder das Branching ist fehlkonfiguriert (kein passender Übergang und
    /// kein Default bzw. unbekannte Zielfrage).
    /// </exception>
    public async ValueTask<EditAnswerResult> Handle(
        EditAnswerCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var session = await _store.GetSessionAsync(command.SessionId, cancellationToken)
            ?? throw SessionNotFoundException.ForId(command.SessionId);

        // Editieren ist für laufende und abgeschlossene Sessions erlaubt (nachträgliche Korrektur),
        // aber nicht für abgebrochene.
        if (session.Status == SessionStatus.Abandoned)
        {
            throw new InvalidOperationException(
                $"Die Session '{session.Id}' ist abgebrochen (Status: {session.Status}) und kann nicht editiert werden.");
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

        // Die zu editierende Antwort auf die Frage suchen; ohne bestehende Antwort gibt es nichts zu
        // editieren. Innerhalb einer Schleife wählt der optionale IterationIndex gezielt die Iteration,
        // sonst greift wie bisher die früheste Antwort der Frage.
        var candidates = session.Answers.Where(answer => answer.QuestionId == command.QuestionId);
        var target = (command.IterationIndex is int iteration
                ? candidates.FirstOrDefault(answer => answer.IterationIndex == iteration)
                : candidates.OrderBy(answer => answer.Sequence).FirstOrDefault())
            ?? throw new InvalidOperationException(
                $"Die Frage '{command.QuestionId}' wurde in Session '{session.Id}'"
                + (command.IterationIndex is int it ? $" in Iteration {it}" : string.Empty)
                + " noch nicht beantwortet und kann daher nicht editiert werden.");

        // Antwort überschreiben (Sequence bleibt erhalten, Zeitpunkt spiegelt die Editierung wider).
        target.Value = command.Value;
        target.AnsweredAt = DateTimeOffset.UtcNow;

        var invalidatedCount = InvalidateDownstream(session, target.Sequence);

        var next = new TransitionResolver(_evaluator).ResolveTransitionTarget(dialog, session, command.QuestionId);
        if (next is null)
        {
            Complete(session);
            await _store.SaveChangesAsync(cancellationToken);
            return new EditAnswerResult(session.Id, IsCompleted: true, NextQuestion: null, invalidatedCount);
        }

        Reopen(session, next.Value);
        await _store.SaveChangesAsync(cancellationToken);
        return new EditAnswerResult(
            session.Id, IsCompleted: false, QuestionProjection.ResolveQuestion(dialog, next.Value), invalidatedCount);
    }

    /// <summary>
    /// Verwirft alle nachgelagerten Antworten der Session – jene mit einer <see cref="SessionAnswer.Sequence"/>
    /// oberhalb der editierten Antwort – aus dem getrackten Antwort-Graphen. Das Entfernen aus der
    /// Collection löscht die Zeilen beim <see cref="IDialogStore.SaveChangesAsync"/> (Cascade-/Orphan-Delete)
    /// und hält zugleich den In-Memory-Kontext für die anschließende Pfad-Neuberechnung konsistent.
    /// </summary>
    /// <param name="session">Die getrackte Session.</param>
    /// <param name="editedSequence">Die <see cref="SessionAnswer.Sequence"/> der editierten Antwort.</param>
    /// <returns>Die Anzahl der verworfenen nachgelagerten Antworten.</returns>
    private static int InvalidateDownstream(DialogSession session, int editedSequence)
    {
        var downstream = session.Answers
            .Where(answer => answer.Sequence > editedSequence)
            .ToList();

        foreach (var answer in downstream)
        {
            session.Answers.Remove(answer);
        }

        return downstream.Count;
    }

    /// <summary>
    /// Öffnet die Session (wieder) für die neu berechnete Folgefrage: setzt sie auf
    /// <see cref="SessionStatus.InProgress"/>, löscht einen etwaigen Abschlusszeitpunkt und richtet die
    /// aktuell offene Frage neu aus. Wirkt bei einer laufenden Session als reines Umsetzen der Frage und
    /// öffnet eine zuvor abgeschlossene Session erneut.
    /// </summary>
    private static void Reopen(DialogSession session, Guid currentQuestionId)
    {
        session.Status = SessionStatus.InProgress;
        session.CompletedAt = null;
        session.CurrentQuestionId = currentQuestionId;
    }

    /// <summary>Schließt die Session ab: Status, Abschlusszeitpunkt und Löschen der offenen Frage.</summary>
    private static void Complete(DialogSession session)
    {
        session.Status = SessionStatus.Completed;
        session.CompletedAt = DateTimeOffset.UtcNow;
        session.CurrentQuestionId = null;
    }
}
