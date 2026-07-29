using Flirty.Domain;
using Flirty.Persistence;
using Mediator;

namespace Flirty.Runtime.Admin;

/// <summary>
/// Creates a <b>new version</b> of the dialog <see cref="SourceDialogId"/>: a full copy
/// of the configuration graph (questions including answer options, transitions, loop markers, triggers)
/// along with the stored canvas positions as a <b>draft</b> with the same <c>Key</c> identifier and the
/// next free version number.
/// </summary>
/// <remarks>
/// This is the intended way to evolve a published dialog: the published
/// version stays unchanged, so that running sessions run to completion stably via their pinned
/// <see cref="DialogSession.DialogVersion"/>. The copy receives throughout
/// <b>new Guids</b>; all question references (entry question, transitions, loop markers, triggers) are
/// rewritten to the copies. References to questions that do not (any longer) belong to the dialog –
/// the admin API deliberately does not check question references – are taken over unchanged; they are
/// already ineffective in the source and the designer flags them as orphaned. The only exception are the
/// <see cref="DialogLayout"/> rows: a position without an element is pure display data with no target and
/// is <b>discarded</b> on cloning instead of dragged along.
/// <para>
/// The copy is <b>not</b> published (<c>IsPublished = false</c>): two published
/// versions of the same key would not be unambiguous for <c>StartDialogCommand</c>. The release is
/// a separate step (<c>PublishDialogCommand</c>) – until then the runtime continues to start
/// the previous version.
/// </para>
/// </remarks>
/// <param name="SourceDialogId">The primary key of the dialog version that is copied.</param>
public sealed record CreateDialogVersionCommand(Guid SourceDialogId) : ICommand<DialogDetail>;

/// <summary>Handler for <see cref="CreateDialogVersionCommand"/>.</summary>
internal sealed class CreateDialogVersionCommandHandler
    : ICommandHandler<CreateDialogVersionCommand, DialogDetail>
{
    private readonly IDialogAdminStore _store;

    /// <summary>Creates the handler over the given <see cref="IDialogAdminStore"/>.</summary>
    /// <param name="store">The writing repository for the configuration graph.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is <see langword="null"/>.</exception>
    public CreateDialogVersionCommandHandler(IDialogAdminStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    /// <exception cref="ConfigurationNotFoundException">No dialog with the given id exists.</exception>
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

        // Old to new question id: every reference in the graph is rewritten through it.
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

        foreach (var layout in source.Layout)
        {
            if (!TryMapLayoutElement(questionIdMap, layout, out var elementId))
            {
                continue;
            }

            copy.Layout.Add(new DialogLayout
            {
                Id = Guid.NewGuid(),
                DialogId = copy.Id,
                ElementKind = layout.ElementKind,
                ElementId = elementId,
                X = layout.X,
                Y = layout.Y,
            });
        }

        _store.Add(copy);
        await _store.SaveChangesAsync(cancellationToken);

        return AdminProjection.ToDetail(copy);
    }

    /// <summary>
    /// Translates the element reference of a layout row onto the copy.
    /// </summary>
    /// <remarks>
    /// Deliberately <b>kind-aware</b> and with a return value instead of via <c>MapQuestion</c>: firstly, a
    /// future second <see cref="LayoutElementKind"/> stands out here as an unhandled branch, instead of
    /// silently running onto a question mapping. Secondly, a non-mappable row is
    /// <b>discarded</b> instead of taken over unchanged: a position without an associated element is pure
    /// display data with no target and would otherwise carry through every follow-up version.
    /// </remarks>
    /// <param name="map">The mapping of old to new question ids.</param>
    /// <param name="layout">The layout row to clone.</param>
    /// <param name="elementId">The translated reference if the mapping succeeds.</param>
    /// <returns><see langword="true"/> if the row can be cloned.</returns>
    private static bool TryMapLayoutElement(
        IReadOnlyDictionary<Guid, Guid> map, DialogLayout layout, out Guid elementId)
    {
        switch (layout.ElementKind)
        {
            case LayoutElementKind.Question:
                return map.TryGetValue(layout.ElementId, out elementId);

            default:
                elementId = default;
                return false;
        }
    }

    /// <summary>Rewrites a question reference onto the copy (unknown references stay unchanged).</summary>
    /// <param name="map">The mapping of old to new question ids.</param>
    /// <param name="questionId">The reference to translate.</param>
    /// <returns>The id of the copy, or the unchanged value.</returns>
    private static Guid MapQuestion(IReadOnlyDictionary<Guid, Guid> map, Guid questionId)
        => map.TryGetValue(questionId, out var mapped) ? mapped : questionId;

    /// <summary>Nullable variant of <see cref="MapQuestion(IReadOnlyDictionary{Guid, Guid}, Guid)"/>.</summary>
    /// <param name="map">The mapping of old to new question ids.</param>
    /// <param name="questionId">The reference to translate, or <see langword="null"/>.</param>
    /// <returns>The id of the copy, the unchanged value, or <see langword="null"/>.</returns>
    private static Guid? MapQuestion(IReadOnlyDictionary<Guid, Guid> map, Guid? questionId)
        => questionId is null ? null : MapQuestion(map, questionId.Value);
}
