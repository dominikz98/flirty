using Flirty.Domain;

namespace Flirty.Designer.Models;

/*
 * The payloads with which the inspector panels of the canvas (#103) report their inputs upward.
 *
 * Why dedicated types and not the form models: QuestionFormModel, TransitionFormModel,
 * LoopFormModel and TriggerFormModel are `internal`, and Razor generates components as `public` – an
 * internal type on a [Parameter] is CS0053 and, under TreatWarningsAsErrors, a build error. The
 * models therefore remain PRIVATE state of the respective panel (there `internal` may stand, because it
 * does not appear in the parameter list), and only the result crosses the component boundary.
 *
 * The side effect is the clearer responsibility: panel = form including pre-checks, page = commands.
 * This gives exactly one place for the gesture lock and the error path.
 */

/// <summary>The changed header fields of a question.</summary>
/// <param name="QuestionId">The edited question.</param>
/// <param name="Key">The domain-level key.</param>
/// <param name="Text">The question text.</param>
/// <param name="Type">The answer type.</param>
/// <param name="IsRequired">Whether an answer is required.</param>
public sealed record QuestionEdit(
    Guid QuestionId,
    string Key,
    string Text,
    QuestionType Type,
    bool IsRequired);

/// <summary>The changed fields of a transition.</summary>
/// <param name="TransitionId">The edited transition.</param>
/// <param name="TargetQuestionId">The target question.</param>
/// <param name="Expression">The condition; <see langword="null"/> means "unconditional".</param>
/// <param name="IsDefault">Whether the transition applies when no condition matches.</param>
public sealed record TransitionEdit(
    Guid TransitionId,
    Guid TargetQuestionId,
    string? Expression,
    bool IsDefault);

/// <summary>A reordering of the evaluation order within a source question.</summary>
/// <param name="FromQuestionId">The source question whose transitions are reordered.</param>
/// <param name="From">The current position.</param>
/// <param name="To">The target position.</param>
public sealed record TransitionMove(Guid FromQuestionId, int From, int To);

/// <summary>A new loop marker, derived from a back-jump.</summary>
/// <param name="CollectionKey">The key under which the iterations are collected.</param>
/// <param name="EntryQuestionId">The entry question (the target of the back-jump).</param>
/// <param name="BreakingQuestionId">The breaking question (the source question of the back-jump).</param>
public sealed record LoopDraft(string CollectionKey, Guid EntryQuestionId, Guid BreakingQuestionId);

/// <summary>The new position of a node after the drag was finished in the browser (#104).</summary>
/// <remarks>
/// On the editor page (#102) the JS module reports the drag directly to the page; the run view binds
/// the module in the canvas component instead, and it passes the drag on as one piece.
/// </remarks>
/// <param name="QuestionId">The moved question.</param>
/// <param name="X">The new horizontal canvas coordinate in px.</param>
/// <param name="Y">The new vertical canvas coordinate in px.</param>
public sealed record NodeMove(Guid QuestionId, int X, int Y);

/// <summary>A new trigger.</summary>
/// <param name="Scope">The point in time.</param>
/// <param name="QuestionId">The reference question for <see cref="TriggerScope.AfterQuestion"/>, otherwise <see langword="null"/>.</param>
/// <param name="Kind">The channel.</param>
/// <param name="Config">
/// The configuration as JSON – built in the panel via <c>TriggerFormModel.TryBuildConfig</c>, so that the
/// cross-field rules from #42 (a webhook needs an absolute URL) take effect before the command.
/// </param>
public sealed record TriggerDraft(TriggerScope Scope, Guid? QuestionId, TriggerKind Kind, string Config);
