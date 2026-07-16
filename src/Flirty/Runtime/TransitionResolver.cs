using Flirty.Domain;
using Flirty.Expressions;

namespace Flirty.Runtime;

/// <summary>
/// Gemeinsamer Branching-Kernel der Dialog-Runtime: wertet ausgehend von einer beantworteten Frage die
/// konfigurierten <see cref="Transition"/>s einer gepinnten Dialogversion aus und liefert die nächste
/// Frage bzw. den Abschluss. Wird von <see cref="SubmitAnswerCommandHandler"/> (#26) und
/// <see cref="EditAnswerCommandHandler"/> (#28) geteilt, damit die Übergangs-Logik nur an <b>einer</b>
/// Stelle existiert.
/// </summary>
internal sealed class TransitionResolver
{
    private readonly IExpressionEvaluator _evaluator;

    /// <summary>Erstellt den Resolver über die angegebene Ausdrucks-Engine.</summary>
    /// <param name="evaluator">Die Engine zur Auswertung der Übergangs-Bedingungsausdrücke.</param>
    /// <exception cref="ArgumentNullException"><paramref name="evaluator"/> ist <see langword="null"/>.</exception>
    public TransitionResolver(IExpressionEvaluator evaluator)
    {
        ArgumentNullException.ThrowIfNull(evaluator);
        _evaluator = evaluator;
    }

    /// <summary>
    /// Wertet die ausgehenden Übergänge der Frage <paramref name="questionId"/> aus und liefert die
    /// Ziel-Frage-Id des greifenden Übergangs. Zurückgegeben wird <see langword="null"/>, wenn die Frage
    /// <b>keine</b> ausgehenden Übergänge besitzt (regulärer Abschluss). Existieren Übergänge, greift aber
    /// weder ein bedingter Übergang noch ein Default, wird der Dialog als fehlkonfiguriert abgelehnt.
    /// </summary>
    /// <param name="dialog">Die gepinnte Dialogversion samt Übergängen und Fragen.</param>
    /// <param name="session">Die laufende Session, deren Antworten den Ausdruckskontext speisen.</param>
    /// <param name="questionId">Die Id der beantworteten Frage, deren Übergänge ausgewertet werden.</param>
    /// <returns>Die Ziel-Frage-Id des greifenden Übergangs oder <see langword="null"/> bei Abschluss.</returns>
    /// <exception cref="InvalidOperationException">
    /// Übergänge sind vorhanden, aber keiner greift und es gibt keinen Default, oder die Zielfrage des
    /// greifenden Übergangs gehört nicht zum Dialog-Graphen.
    /// </exception>
    public Guid? ResolveTransitionTarget(Dialog dialog, DialogSession session, Guid questionId)
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
    /// <see cref="Question.Key"/> abgebildet. Loop-Collections und Iterationsindex bleiben leer bzw.
    /// <see langword="null"/> (Loop-Runtime folgt in #29).
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
}
