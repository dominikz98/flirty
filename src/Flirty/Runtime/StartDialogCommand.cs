using System.ComponentModel.DataAnnotations;
using Flirty.Domain;
using Flirty.Persistence;
using Mediator;

namespace Flirty.Runtime;

/// <summary>
/// Startet den veröffentlichten Dialog mit dem fachlichen Schlüssel <see cref="DialogKey"/> für den
/// Anwender <see cref="ExternalUserKey"/>. Existiert für diesen Anwender bereits eine laufende
/// (<see cref="SessionStatus.InProgress"/>) Session der aktuell veröffentlichten Dialogversion, wird
/// diese fortgesetzt (Resume) statt eine neue anzulegen.
/// </summary>
/// <param name="DialogKey">Der fachliche, stabile Schlüssel des zu startenden Dialogs.</param>
/// <param name="ExternalUserKey">Der fachliche Anwenderschlüssel der Host-App (z. B. Benutzer-Id).</param>
public sealed record StartDialogCommand(
    [property: Required] string DialogKey,
    [property: Required] string ExternalUserKey) : ICommand<StartDialogResult>;

/// <summary>
/// Handler für <see cref="StartDialogCommand"/>: löst den veröffentlichten Dialog auf, entscheidet
/// zwischen Resume und Neu-Start und liefert die aktuell offene Frage.
/// </summary>
internal sealed class StartDialogCommandHandler : ICommandHandler<StartDialogCommand, StartDialogResult>
{
    private readonly IDialogStore _store;
    private readonly IPublisher _publisher;

    /// <summary>
    /// Erstellt den Handler über den angegebenen <see cref="IDialogStore"/> und <see cref="IPublisher"/>.
    /// </summary>
    /// <param name="store">Das Repository für Dialoge und Sessions.</param>
    /// <param name="publisher">Der Mediator-Publisher für die In-Process-Trigger-Notifications.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="store"/> oder <paramref name="publisher"/> ist <see langword="null"/>.
    /// </exception>
    public StartDialogCommandHandler(IDialogStore store, IPublisher publisher)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(publisher);
        _store = store;
        _publisher = publisher;
    }

    /// <inheritdoc />
    /// <exception cref="DialogNotFoundException">
    /// Kein veröffentlichter Dialog mit dem angegebenen Schlüssel existiert.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Der veröffentlichte Dialog besitzt keine Einstiegsfrage bzw. die aktuelle Frage kann nicht
    /// aufgelöst werden (Fehlkonfiguration).
    /// </exception>
    public async ValueTask<StartDialogResult> Handle(
        StartDialogCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var dialog = await _store.GetPublishedDialogAsync(command.DialogKey, cancellationToken)
            ?? throw DialogNotFoundException.ForKey(command.DialogKey);

        // Resume: FindActiveSessionAsync filtert auf dialog.Id (= die gerade veröffentlichte Version),
        // eine gefundene Session ist damit genau auf diesen Dialog-Graphen gepinnt.
        var existing = await _store.FindActiveSessionAsync(
            dialog.Id, command.ExternalUserKey, cancellationToken);
        if (existing is not null)
        {
            return new StartDialogResult(
                existing.Id, IsResumed: true,
                QuestionProjection.ResolveQuestion(dialog, existing.CurrentQuestionId));
        }

        if (dialog.StartQuestionId is null)
        {
            throw new InvalidOperationException(
                $"Der veröffentlichte Dialog '{dialog.Key}' besitzt keine Einstiegsfrage (StartQuestionId).");
        }

        var session = new DialogSession
        {
            Id = Guid.NewGuid(),
            DialogId = dialog.Id,
            DialogVersion = dialog.Version,
            ExternalUserKey = command.ExternalUserKey,
            Status = SessionStatus.InProgress,
            CurrentQuestionId = dialog.StartQuestionId,
            StartedAt = DateTimeOffset.UtcNow,
        };

        _store.AddSession(session);
        await _store.SaveChangesAsync(cancellationToken);

        // In-Process-Trigger (EPIC 4): nur der echte Neu-Start meldet DialogStarted; ein Resume nicht.
        await _publisher.Publish(
            new DialogStartedNotification(
                session.Id,
                dialog.Id,
                dialog.Key,
                command.ExternalUserKey,
                session.CurrentQuestionId,
                session.StartedAt),
            cancellationToken);

        return new StartDialogResult(
            session.Id, IsResumed: false,
            QuestionProjection.ResolveQuestion(dialog, session.CurrentQuestionId));
    }
}
