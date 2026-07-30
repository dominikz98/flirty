using System.ComponentModel.DataAnnotations;
using Flirty.Domain;

namespace Flirty.Runtime.Admin;

/// <summary>
/// Shared cross-field checks of <see cref="CreateTriggerCommand"/> and
/// <see cref="UpdateTriggerCommand"/>. Both commands invoke them via <see cref="IValidatableObject"/>;
/// the <c>ValidationPipelineBehavior</c> thereby runs them before the handler and reports violations
/// as a <see cref="ValidationException"/> (in the WebAPI: HTTP 400).
/// </summary>
/// <remarks>
/// Deliberately here and not in the handler: the rules describe the <b>request</b>, not the state of the
/// database. The question reference stays – as with <see cref="Transition"/> and
/// <see cref="LoopDefinition"/> – FK-free and is <b>not</b> checked for existence; only whether
/// it matches the <see cref="TriggerScope"/> is checked.
/// </remarks>
internal static class TriggerValidation
{
    /// <summary>
    /// Checks whether point in time, question reference, channel and configuration match one another.
    /// </summary>
    /// <param name="scope">The point in time at which the trigger should fire.</param>
    /// <param name="questionId">The question reference (allowed only for <see cref="TriggerScope.AfterQuestion"/>).</param>
    /// <param name="kind">The channel over which it fires.</param>
    /// <param name="config">The channel-specific configuration as JSON.</param>
    /// <returns>The violations found (empty if everything is consistent).</returns>
    public static IEnumerable<ValidationResult> Validate(
        TriggerScope scope, Guid? questionId, TriggerKind kind, string? config)
    {
        if (scope == TriggerScope.AfterQuestion && questionId is null)
        {
            yield return new ValidationResult(
                "A trigger with the point in time 'AfterQuestion' needs a question (QuestionId).",
                [nameof(TriggerDefinition.QuestionId)]);
        }

        if (scope != TriggerScope.AfterQuestion && questionId is not null)
        {
            yield return new ValidationResult(
                $"The point in time '{scope}' does not refer to a single question – QuestionId must be empty.",
                [nameof(TriggerDefinition.QuestionId)]);
        }

        if (!TriggerConfig.TryParse(config, out var parsed, out var parseError))
        {
            yield return new ValidationResult(parseError, [nameof(TriggerDefinition.Config)]);
            yield break;
        }

        if (!parsed.TryValidate(kind, out var configError))
        {
            yield return new ValidationResult(configError, [nameof(TriggerDefinition.Config)]);
        }
    }
}
