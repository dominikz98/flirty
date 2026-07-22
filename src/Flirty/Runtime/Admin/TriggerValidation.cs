using System.ComponentModel.DataAnnotations;
using Flirty.Domain;

namespace Flirty.Runtime.Admin;

/// <summary>
/// Gemeinsame Querfeld-Prüfungen von <see cref="CreateTriggerCommand"/> und
/// <see cref="UpdateTriggerCommand"/>. Beide Commands rufen sie über <see cref="IValidatableObject"/>
/// auf; das <c>ValidationPipelineBehavior</c> führt sie damit vor dem Handler aus und meldet Verstöße
/// als <see cref="ValidationException"/> (in der WebAPI: HTTP 400).
/// </summary>
/// <remarks>
/// Bewusst hier und nicht im Handler: Die Regeln beschreiben die <b>Anfrage</b>, nicht den Zustand der
/// Datenbank. Der Frage-Verweis bleibt – wie bei <see cref="Transition"/> und
/// <see cref="LoopDefinition"/> – FK-los und wird <b>nicht</b> auf Existenz geprüft; geprüft wird nur,
/// ob er zum <see cref="TriggerScope"/> passt.
/// </remarks>
internal static class TriggerValidation
{
    /// <summary>
    /// Prüft, ob Zeitpunkt, Frage-Verweis, Kanal und Konfiguration zueinander passen.
    /// </summary>
    /// <param name="scope">Der Zeitpunkt, zu dem der Trigger auslösen soll.</param>
    /// <param name="questionId">Der Frage-Verweis (nur bei <see cref="TriggerScope.AfterQuestion"/> erlaubt).</param>
    /// <param name="kind">Der Kanal, über den ausgelöst wird.</param>
    /// <param name="config">Die kanal-spezifische Konfiguration als JSON.</param>
    /// <returns>Die gefundenen Verstöße (leer, wenn alles stimmig ist).</returns>
    public static IEnumerable<ValidationResult> Validate(
        TriggerScope scope, Guid? questionId, TriggerKind kind, string? config)
    {
        if (scope == TriggerScope.AfterQuestion && questionId is null)
        {
            yield return new ValidationResult(
                "Ein Trigger mit dem Zeitpunkt 'AfterQuestion' braucht eine Frage (QuestionId).",
                [nameof(TriggerDefinition.QuestionId)]);
        }

        if (scope != TriggerScope.AfterQuestion && questionId is not null)
        {
            yield return new ValidationResult(
                $"Der Zeitpunkt '{scope}' bezieht sich nicht auf eine einzelne Frage – QuestionId muss leer sein.",
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
