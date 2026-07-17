using Flirty.Domain;
using Flirty.Persistence;
using Flirty.Runtime;
using Flirty.Validation;
using Mediator;

namespace Flirty.Pipeline;

/// <summary>
/// Mediator-Pipeline-Behavior, das für antworteinreichende Runtime-Commands
/// (<see cref="SubmitAnswerCommand"/>, <see cref="EditAnswerCommand"/> – erkannt am Marker
/// <see cref="IAnswerCommand"/>) die betroffene Frage der gepinnten Dialogversion auflöst und den
/// Antwortwert <b>vor</b> dem Handler fachlich per <see cref="IAnswerValidator"/> validiert. Ein
/// Verstoß wird mit einer <see cref="AnswerValidationException"/> abgewiesen, bevor die Antwort
/// persistiert bzw. der Pfad neu berechnet wird (Issue #30).
/// </summary>
/// <remarks>
/// <para>
/// Bewusst <b>intern</b> und über <c>AddFlirty()</c> <b>geschlossen</b> je Command-Typ registriert
/// (nicht offen-generisch): Das Behavior benötigt den scoped <see cref="IDialogStore"/> (und damit
/// einen registrierten <c>FlirtyDbContext</c>). Eine offen-generische Registrierung würde es für jede
/// Nachricht konstruieren – auch dort, wo kein <c>FlirtyDbContext</c> vorhanden ist – und die
/// Auflösung brechen. Als scoped Registrierung teilt es sich mit dem Handler denselben Kontext:
/// <see cref="IDialogStore.GetSessionAsync"/> liefert getrackt, sodass der Handler dieselbe Instanz
/// erhält (kein zweiter Query).
/// </para>
/// <para>
/// Kann die Frage nicht aufgelöst werden (Session, gepinnter Dialog oder Frage fehlt) oder ist der
/// Wert leer, überspringt das Behavior die Validierung und ruft nur <c>next</c> – die kanonischen
/// Fehler (<see cref="SessionNotFoundException"/>, DataAnnotations-Validierung,
/// <see cref="InvalidOperationException"/>) bleiben allein Sache des Handlers bzw. des
/// <c>ValidationPipelineBehavior</c>.
/// </para>
/// </remarks>
/// <typeparam name="TMessage">Der Nachrichtentyp (Command, Query oder Notification).</typeparam>
/// <typeparam name="TResponse">Der von der Nachricht erwartete Antworttyp.</typeparam>
internal sealed class AnswerValidationPipelineBehavior<TMessage, TResponse>
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : notnull, IMessage
{
    private readonly IDialogStore _store;
    private readonly IAnswerValidator _validator;

    /// <summary>
    /// Erstellt das Behavior über den angegebenen <see cref="IDialogStore"/> und
    /// <see cref="IAnswerValidator"/>.
    /// </summary>
    /// <param name="store">Das Repository zum Auflösen von Session und gepinnter Dialogversion.</param>
    /// <param name="validator">Der fachliche Antwort-Validator.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="store"/> oder <paramref name="validator"/> ist <see langword="null"/>.
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
    /// Der Antwortwert ist für den Typ bzw. die Regeln der aufgelösten Frage ungültig.
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
    /// Löst die vom Command adressierte Frage über Session → gepinnten Dialog auf oder liefert
    /// <see langword="null"/>, wenn eines davon fehlt (dann validiert das Behavior nicht und überlässt
    /// den kanonischen Fehler dem Handler).
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
