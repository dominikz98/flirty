using Flirty.Domain;

namespace Flirty.Designer.Models;

/// <summary>
/// English display texts for <see cref="TriggerScope"/> and <see cref="TriggerKind"/>. Centralized, so that
/// the trigger list (<c>DialogEditor</c>) and the trigger editor (<c>TriggerEditor</c>) use the same
/// labels (pattern: <see cref="QuestionTypeLabels"/>).
/// </summary>
internal static class TriggerLabels
{
    /// <summary>Returns the display text of the firing point in time.</summary>
    /// <param name="scope">The point in time in the dialog flow.</param>
    /// <returns>The English display text (including the technical name for recognition).</returns>
    public static string Describe(TriggerScope scope) => scope switch
    {
        TriggerScope.OnDialogStarted => "At dialog start (OnDialogStarted)",
        TriggerScope.AfterAnswer => "After every answer (AfterAnswer)",
        TriggerScope.AfterQuestion => "After a specific question (AfterQuestion)",
        TriggerScope.OnDialogCompleted => "On completion (OnDialogCompleted)",
        _ => scope.ToString(),
    };

    /// <summary>Returns the display text of the channel.</summary>
    /// <param name="kind">The channel over which notification is done.</param>
    /// <returns>The English display text (including the technical name for recognition).</returns>
    public static string Describe(TriggerKind kind) => kind switch
    {
        TriggerKind.Webhook => "Webhook (HTTP POST)",
        TriggerKind.InProcess => "In-process (host app handler)",
        _ => kind.ToString(),
    };

    /// <summary>
    /// Indicates whether the point in time needs a question reference. Only
    /// <see cref="TriggerScope.AfterQuestion"/> refers to a single question – the admin commands
    /// reject deviating combinations.
    /// </summary>
    /// <param name="scope">The point in time in the dialog flow.</param>
    /// <returns><see langword="true"/> for <see cref="TriggerScope.AfterQuestion"/>.</returns>
    public static bool RequiresQuestion(TriggerScope scope) => scope == TriggerScope.AfterQuestion;

    /// <summary>
    /// Indicates whether, at the time of evaluation, answers are already bound in the expression context.
    /// At dialog start that is <b>not</b> the case: a condition on a question key
    /// fails there at runtime (the trigger then does not fire, the error is only logged).
    /// </summary>
    /// <param name="scope">The point in time in the dialog flow.</param>
    /// <returns><see langword="false"/> for <see cref="TriggerScope.OnDialogStarted"/>.</returns>
    public static bool HasAnswers(TriggerScope scope) => scope != TriggerScope.OnDialogStarted;
}
