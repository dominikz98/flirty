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

        PersistAnswer(session, command);

        var target = ResolveTransitionTarget(dialog, session, command.QuestionId);
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
    /// <see cref="SessionAnswer.Sequence"/> setzt die Reihenfolge innerhalb der Session fort.
    /// </summary>
    private static void PersistAnswer(DialogSession session, SubmitAnswerCommand command)
    {
        var nextSequence = session.Answers.Count == 0
            ? 0
            : session.Answers.Max(answer => answer.Sequence) + 1;

        session.Answers.Add(new SessionAnswer
        {
            SessionId = session.Id,
            QuestionId = command.QuestionId,
            Value = command.Value,
            AnsweredAt = DateTimeOffset.UtcNow,
            Sequence = nextSequence,
        });
    }

    /// <summary>
    /// Wertet die ausgehenden Übergänge der beantworteten Frage aus und liefert die Ziel-Frage-Id des
    /// greifenden Übergangs. Zurückgegeben wird <see langword="null"/>, wenn die Frage <b>keine</b>
    /// ausgehenden Übergänge besitzt (regulärer Abschluss). Existieren Übergänge, greift aber weder ein
    /// bedingter Übergang noch ein Default, wird der Dialog als fehlkonfiguriert abgelehnt.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Übergänge sind vorhanden, aber keiner greift und es gibt keinen Default, oder die Zielfrage des
    /// greifenden Übergangs gehört nicht zum Dialog-Graphen.
    /// </exception>
    private Guid? ResolveTransitionTarget(Dialog dialog, DialogSession session, Guid questionId)
    {
        var outgoing = dialog.Transitions
            .Where(transition => transition.FromQuestionId == questionId)
            .OrderBy(transition => transition.Priority)
            .ToList();

        if (outgoing.Count == 0)
        {
            return null;
        }

        var context = BuildContext(dialog, session);
        var match = outgoing.FirstOrDefault(transition => !transition.IsDefault && ConditionHolds(transition, context))
            ?? outgoing.FirstOrDefault(transition => transition.IsDefault);

        if (match is null)
        {
            throw new InvalidOperationException(
                $"Für die Frage '{questionId}' im Dialog '{dialog.Key}' trifft kein Übergang zu und es "
                + "ist kein Default-Übergang konfiguriert.");
        }

        if (dialog.Questions.All(question => question.Id != match.TargetQuestionId))
        {
            throw new InvalidOperationException(
                $"Der Übergang '{match.Id}' im Dialog '{dialog.Key}' zeigt auf die unbekannte Zielfrage "
                + $"'{match.TargetQuestionId}'.");
        }

        return match.TargetQuestionId;
    }

    /// <summary>
    /// Prüft, ob der Übergang greift: Ein <see langword="null"/>er/leerer Ausdruck gilt als
    /// bedingungslos zutreffend (Kurzschluss liegt bei der Runtime); andernfalls entscheidet der
    /// <see cref="IExpressionEvaluator"/>.
    /// </summary>
    private bool ConditionHolds(Transition transition, ExpressionContext context)
        => string.IsNullOrWhiteSpace(transition.Expression)
            || _evaluator.Evaluate(transition.Expression, context);

    /// <summary>
    /// Baut den <see cref="ExpressionContext"/> aus den bisherigen Antworten der Session. Je Frage wird
    /// die zuletzt gegebene Antwort (höchste <see cref="SessionAnswer.Sequence"/>) auf den fachlichen
    /// <see cref="Question.Key"/> abgebildet. Loop-Collections und Iterationsindex bleiben in #26 leer
    /// bzw. <see langword="null"/> (Loop-Runtime folgt in #29).
    /// </summary>
    private static ExpressionContext BuildContext(Dialog dialog, DialogSession session)
    {
        var keyByQuestionId = dialog.Questions.ToDictionary(question => question.Id, question => question.Key);

        var answers = session.Answers
            .Where(answer => keyByQuestionId.ContainsKey(answer.QuestionId))
            .GroupBy(answer => answer.QuestionId)
            .ToDictionary(
                group => keyByQuestionId[group.Key],
                group => (string?)group.OrderByDescending(answer => answer.Sequence).First().Value);

        return new ExpressionContext(session, DateTimeOffset.UtcNow, answers);
    }

    /// <summary>Schließt die Session ab: Status, Abschlusszeitpunkt und Löschen der offenen Frage.</summary>
    private static void Complete(DialogSession session)
    {
        session.Status = SessionStatus.Completed;
        session.CompletedAt = DateTimeOffset.UtcNow;
        session.CurrentQuestionId = null;
    }
}
