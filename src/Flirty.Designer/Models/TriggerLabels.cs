using Flirty.Domain;

namespace Flirty.Designer.Models;

/// <summary>
/// German display texts for <see cref="TriggerScope"/> and <see cref="TriggerKind"/>. Central, so that
/// the trigger list (<c>DialogEditor</c>) and trigger editor (<c>TriggerEditor</c>) use the same
/// designations (pattern: <see cref="QuestionTypeLabels"/>).
/// </summary>
internal static class TriggerLabels
{
    /// <summary>Returns the display text of the triggering point in time.</summary>
    /// <param name="scope">The point in time in the dialog flow.</param>
    /// <returns>The German display text (including the technical name for recognition).</returns>
    public static string Describe(TriggerScope scope) => scope switch
    {
        TriggerScope.OnDialogStarted => "Beim Dialogstart (OnDialogStarted)",
        TriggerScope.AfterAnswer => "Nach jeder Antwort (AfterAnswer)",
        TriggerScope.AfterQuestion => "Nach einer bestimmten Frage (AfterQuestion)",
        TriggerScope.OnDialogCompleted => "Beim Abschluss (OnDialogCompleted)",
        _ => scope.ToString(),
    };

    /// <summary>Returns the display text of the channel.</summary>
    /// <param name="kind">The channel over which notification is sent.</param>
    /// <returns>The German display text (including the technical name for recognition).</returns>
    public static string Describe(TriggerKind kind) => kind switch
    {
        TriggerKind.Webhook => "Webhook (HTTP POST)",
        TriggerKind.InProcess => "In-Process (Handler der Host-App)",
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
    /// Indicates whether answers are already bound in the expression context at the point in time of evaluation.
    /// At the dialog start this is <b>not</b> the case: a condition on a question key
    /// fails there at runtime (the trigger then does not fire, the error is only logged).
    /// </summary>
    /// <param name="scope">The point in time in the dialog flow.</param>
    /// <returns><see langword="false"/> for <see cref="TriggerScope.OnDialogStarted"/>.</returns>
    public static bool HasAnswers(TriggerScope scope) => scope != TriggerScope.OnDialogStarted;
}
