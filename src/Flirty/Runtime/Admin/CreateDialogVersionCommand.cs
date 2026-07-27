using Flirty.Domain;
using Flirty.Persistence;
using Mediator;

namespace Flirty.Runtime.Admin;

/// <summary>
/// Legt eine <b>neue Version</b> des Dialogs <see cref="SourceDialogId"/> an: eine vollständige Kopie
/// des Konfigurationsgraphen (Fragen inklusive Antwortoptionen, Übergänge, Schleifen-Marker, Trigger)
/// als <b>Entwurf</b> mit derselben <c>Key</c>-Kennung und der nächsten freien Versionsnummer.
/// </summary>
/// <remarks>
/// Das ist der vorgesehene Weg, einen veröffentlichten Dialog weiterzuentwickeln: Die veröffentlichte
/// Version bleibt unverändert, sodass laufende Sessions über ihre gepinnte
/// <see cref="DialogSession.DialogVersion"/> stabil zu Ende laufen. Die Kopie erhält durchgängig
/// <b>neue Guids</b>; alle Frage-Verweise (Einstiegsfrage, Übergänge, Schleifen-Marker, Trigger) werden
/// dabei auf die Kopien umgeschrieben. Verweise auf Fragen, die nicht (mehr) zum Dialog gehören –
/// die Admin-API prüft Frage-Verweise bewusst nicht –, werden unverändert übernommen; sie sind in der
/// Quelle schon unwirksam und der Designer weist sie als verwaist aus.
/// <para>
/// Veröffentlicht wird die Kopie <b>nicht</b> (<c>IsPublished = false</c>): Zwei veröffentlichte
/// Versionen desselben Schlüssels wären für <c>StartDialogCommand</c> nicht eindeutig. Die Freigabe ist
/// ein eigener Schritt (<c>PublishDialogCommand</c>) – bis dahin startet die Laufzeit weiterhin die
/// bisherige Version.
/// </para>
/// </remarks>
/// <param name="SourceDialogId">Der Primärschlüssel der Dialogversion, die kopiert wird.</param>
public sealed record CreateDialogVersionCommand(Guid SourceDialogId) : ICommand<DialogDetail>;

/// <summary>Handler für <see cref="CreateDialogVersionCommand"/>.</summary>
internal sealed class CreateDialogVersionCommandHandler
    : ICommandHandler<CreateDialogVersionCommand, DialogDetail>
{
    private readonly IDialogAdminStore _store;

    /// <summary>Erstellt den Handler über den angegebenen <see cref="IDialogAdminStore"/>.</summary>
    /// <param name="store">Das schreibende Repository für den Konfigurationsgraphen.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> ist <see langword="null"/>.</exception>
    public CreateDialogVersionCommandHandler(IDialogAdminStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    /// <exception cref="ConfigurationNotFoundException">Kein Dialog mit der angegebenen Id existiert.</exception>
    public async ValueTask<DialogDetail> Handle(
        CreateDialogVersionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var source = await _store.GetDialogGraphAsync(command.SourceDialogId, cancellationToken)
            ?? throw ConfigurationNotFoundException.ForDialog(command.SourceDialogId);

        var nextVersion = await _store.GetMaxDialogVersionAsync(source.Key, cancellationToken) + 1;
        var now = DateTimeOffset.UtcNow;

        var copy = new Dialog
        {
            Id = Guid.NewGuid(),
            Key = source.Key,
            Name = source.Name,
            Description = source.Description,
            Version = nextVersion,
            IsPublished = false,
            CreatedAt = now,
            UpdatedAt = now,
        };

        // Alte auf neue Frage-Id: jeder Verweis im Graphen wird darüber umgeschrieben.
        var questionIdMap = new Dictionary<Guid, Guid>();

        foreach (var question in source.Questions)
        {
            var questionCopy = new Question
            {
                Id = Guid.NewGuid(),
                DialogId = copy.Id,
                Key = question.Key,
                Text = question.Text,
                Type = question.Type,
                Order = question.Order,
                IsRequired = question.IsRequired,
                ValidationRules = question.ValidationRules,
            };

            questionIdMap[question.Id] = questionCopy.Id;

            foreach (var option in question.Options)
            {
                questionCopy.Options.Add(new AnswerOption
                {
                    Id = Guid.NewGuid(),
                    QuestionId = questionCopy.Id,
                    Key = option.Key,
                    Label = option.Label,
                    Value = option.Value,
                    Order = option.Order,
                });
            }

            copy.Questions.Add(questionCopy);
        }

        copy.StartQuestionId = MapQuestion(questionIdMap, source.StartQuestionId);

        foreach (var transition in source.Transitions)
        {
            copy.Transitions.Add(new Transition
            {
                Id = Guid.NewGuid(),
                DialogId = copy.Id,
                FromQuestionId = MapQuestion(questionIdMap, transition.FromQuestionId),
                TargetQuestionId = MapQuestion(questionIdMap, transition.TargetQuestionId),
                Expression = transition.Expression,
                Priority = transition.Priority,
                IsDefault = transition.IsDefault,
            });
        }

        foreach (var loop in source.Loops)
        {
            copy.Loops.Add(new LoopDefinition
            {
                Id = Guid.NewGuid(),
                DialogId = copy.Id,
                CollectionKey = loop.CollectionKey,
                EntryQuestionId = MapQuestion(questionIdMap, loop.EntryQuestionId),
                BreakingQuestionId = MapQuestion(questionIdMap, loop.BreakingQuestionId),
            });
        }

        foreach (var trigger in source.Triggers)
        {
            copy.Triggers.Add(new TriggerDefinition
            {
                Id = Guid.NewGuid(),
                DialogId = copy.Id,
                Scope = trigger.Scope,
                QuestionId = MapQuestion(questionIdMap, trigger.QuestionId),
                Kind = trigger.Kind,
                Config = trigger.Config,
                Expression = trigger.Expression,
            });
        }

        _store.Add(copy);
        await _store.SaveChangesAsync(cancellationToken);

        return AdminProjection.ToDetail(copy);
    }

    /// <summary>Schreibt einen Frage-Verweis auf die Kopie um (unbekannte Verweise bleiben unverändert).</summary>
    /// <param name="map">Die Abbildung alter auf neue Frage-Ids.</param>
    /// <param name="questionId">Der zu übersetzende Verweis.</param>
    /// <returns>Die Id der Kopie oder der unveränderte Wert.</returns>
    private static Guid MapQuestion(IReadOnlyDictionary<Guid, Guid> map, Guid questionId)
        => map.TryGetValue(questionId, out var mapped) ? mapped : questionId;

    /// <summary>Nullbare Variante von <see cref="MapQuestion(IReadOnlyDictionary{Guid, Guid}, Guid)"/>.</summary>
    /// <param name="map">Die Abbildung alter auf neue Frage-Ids.</param>
    /// <param name="questionId">Der zu übersetzende Verweis oder <see langword="null"/>.</param>
    /// <returns>Die Id der Kopie, der unveränderte Wert oder <see langword="null"/>.</returns>
    private static Guid? MapQuestion(IReadOnlyDictionary<Guid, Guid> map, Guid? questionId)
        => questionId is null ? null : MapQuestion(map, questionId.Value);
}
