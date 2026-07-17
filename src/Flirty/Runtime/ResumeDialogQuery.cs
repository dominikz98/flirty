using System.ComponentModel.DataAnnotations;
using Flirty.Domain;
using Flirty.Persistence;
using Mediator;

namespace Flirty.Runtime;

/// <summary>
/// Liest den aktuellen Zustand der Session <see cref="SessionId"/>: ihren Status, die aktuell offene
/// Frage (sofern die Session noch läuft) und die bisher gegebenen Antworten. Rein lesend – die Session
/// wird nicht verändert. Das <b>Resume-oder-Neu</b> einer Session je Anwender ist dagegen dem
/// <see cref="StartDialogCommand"/> vorbehalten.
/// </summary>
/// <param name="SessionId">Der Primärschlüssel der abzufragenden <see cref="DialogSession"/>.</param>
public sealed record ResumeDialogQuery(
    [property: Required] Guid SessionId) : IQuery<ResumeDialogResult>;

/// <summary>
/// Handler für <see cref="ResumeDialogQuery"/>: lädt die Session samt Antworten und die von ihr gepinnte
/// Dialogversion, projiziert die aktuell offene Frage und die bisherigen Antworten in navigationsfreie
/// Sichten und liefert den zusammengesetzten <see cref="ResumeDialogResult"/>.
/// </summary>
internal sealed class ResumeDialogQueryHandler : IQueryHandler<ResumeDialogQuery, ResumeDialogResult>
{
    private readonly IDialogStore _store;

    /// <summary>Erstellt den Handler über den angegebenen <see cref="IDialogStore"/>.</summary>
    /// <param name="store">Das Repository für Dialoge und Sessions.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> ist <see langword="null"/>.</exception>
    public ResumeDialogQueryHandler(IDialogStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    /// <exception cref="SessionNotFoundException">
    /// Keine Session mit der angegebenen <see cref="ResumeDialogQuery.SessionId"/> existiert.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Die von der Session gepinnte Dialogversion existiert nicht mehr, oder die aktuell offene Frage
    /// gehört nicht zum Dialog-Graphen (Fehlkonfiguration).
    /// </exception>
    public async ValueTask<ResumeDialogResult> Handle(
        ResumeDialogQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var session = await _store.GetSessionAsync(query.SessionId, cancellationToken)
            ?? throw SessionNotFoundException.ForId(query.SessionId);

        // Die von der Session gepinnte Dialogversion laden (unabhängig vom Veröffentlichungsstatus) –
        // sie liefert die fachlichen Frage-Schlüssel und den Graphen für die Frage-Projektion.
        var dialog = await _store.GetDialogAsync(session.DialogId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Die von Session '{session.Id}' gepinnte Dialogversion '{session.DialogId}' existiert nicht.");

        var answers = SessionAnswerProjection.Project(dialog, session);

        var currentQuestion = session.CurrentQuestionId is Guid questionId
            ? QuestionProjection.ResolveQuestion(dialog, questionId)
            : null;

        return new ResumeDialogResult(session.Id, session.Status, currentQuestion, answers);
    }
}
