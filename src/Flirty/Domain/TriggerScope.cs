namespace Flirty.Domain;

/// <summary>
/// Determines the point in the dialog flow at which a <see cref="TriggerDefinition"/>
/// is fired.
/// </summary>
public enum TriggerScope
{
    /// <summary>When a dialog starts (after the session has been created).</summary>
    OnDialogStarted = 0,

    /// <summary>After every submitted answer.</summary>
    AfterAnswer = 1,

    /// <summary>After a specific question (<see cref="TriggerDefinition.QuestionId"/>).</summary>
    AfterQuestion = 2,

    /// <summary>When the dialog completes.</summary>
    OnDialogCompleted = 3,
}
