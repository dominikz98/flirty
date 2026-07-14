namespace Flirty.Domain;

/// <summary>
/// Bestimmt den Zeitpunkt im Dialogablauf, zu dem eine <see cref="TriggerDefinition"/>
/// ausgelöst wird.
/// </summary>
public enum TriggerScope
{
    /// <summary>Beim Start eines Dialogs (nach Anlage der Session).</summary>
    OnDialogStarted = 0,

    /// <summary>Nach jeder abgegebenen Antwort.</summary>
    AfterAnswer = 1,

    /// <summary>Nach einer bestimmten Frage (<see cref="TriggerDefinition.QuestionId"/>).</summary>
    AfterQuestion = 2,

    /// <summary>Beim Abschluss des Dialogs.</summary>
    OnDialogCompleted = 3,
}
