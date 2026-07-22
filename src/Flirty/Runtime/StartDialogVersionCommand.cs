using System.ComponentModel.DataAnnotations;
using Flirty.Domain;
using Flirty.Persistence;
using Mediator;

namespace Flirty.Runtime;

/// <summary>
/// Startet eine Session gegen die <b>konkrete Dialogversion</b> <see cref="DialogId"/> für den Anwender
/// <see cref="ExternalUserKey"/> – <b>unabhängig vom Veröffentlichungsstatus</b>. Gedacht für Vorschau-
/// und Testszenarien (Designer-Test-Runner, Issue #43), in denen ein Entwurf durchgespielt werden soll,
/// bevor er veröffentlicht wird.
/// </summary>
/// <remarks>
/// <para>
/// Gegenstück zu <see cref="StartDialogCommand"/>, der bewusst nur die <b>veröffentlichte</b> Version
/// eines fachlichen Schlüssels startet. Der Rest der Runtime ist von der Unterscheidung nicht betroffen:
/// Die Session pinnt die <see cref="DialogSession.DialogId"/>, und Resume/Submit/Edit laden ihre
/// Dialogversion über <see cref="IDialogStore.GetDialogAsync"/> – ebenfalls unabhängig vom
/// Veröffentlichungsstatus.
/// </para>
/// <para>
/// Existiert für diesen Anwender bereits eine laufende (<see cref="SessionStatus.InProgress"/>) Session
/// <b>dieser</b> Dialogversion, wird sie fortgesetzt (Resume) statt eine neue anzulegen – identisch zu
/// <see cref="StartDialogCommand"/>.
/// </para>
/// </remarks>
/// <param name="DialogId">Der Primärschlüssel der zu startenden Dialogversion.</param>
/// <param name="ExternalUserKey">Der fachliche Anwenderschlüssel der Host-App (z. B. Benutzer-Id).</param>
public sealed record StartDialogVersionCommand(
    [property: Required] Guid DialogId,
    [property: Required] string ExternalUserKey) : ICommand<StartDialogResult>;

/// <summary>
/// Handler für <see cref="StartDialogVersionCommand"/>: lädt die angegebene Dialogversion, entscheidet
/// zwischen Resume und Neu-Start und liefert die aktuell offene Frage.
/// </summary>
internal sealed class StartDialogVersionCommandHandler
    : ICommandHandler<StartDialogVersionCommand, StartDialogResult>
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
    public StartDialogVersionCommandHandler(IDialogStore store, IPublisher publisher)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(publisher);
        _store = store;
        _publisher = publisher;
    }

    /// <inheritdoc />
    /// <exception cref="ConfigurationNotFoundException">
    /// Keine Dialogversion mit der angegebenen Id existiert.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Der Dialog besitzt keine Einstiegsfrage bzw. die aktuelle Frage kann nicht aufgelöst werden
    /// (Fehlkonfiguration).
    /// </exception>
    public async ValueTask<StartDialogResult> Handle(
        StartDialogVersionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Bewusst GetDialogAsync (nicht GetPublishedDialogAsync): der Veröffentlichungsstatus wird hier
        // ignoriert, genau darin unterscheidet sich der Command von StartDialogCommand.
        var dialog = await _store.GetDialogAsync(command.DialogId, cancellationToken)
            ?? throw ConfigurationNotFoundException.ForDialog(command.DialogId);

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
                $"Der Dialog '{dialog.Key}' besitzt keine Einstiegsfrage (StartQuestionId).");
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

        // In-Process-Trigger (EPIC 4): wie bei StartDialogCommand meldet nur der echte Neu-Start
        // DialogStarted; ein Resume nicht.
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
