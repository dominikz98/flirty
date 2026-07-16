namespace Flirty.Domain;

/// <summary>
/// Definiert einen Rückkanal in die Host-Anwendung, der zu einem bestimmten Zeitpunkt
/// (<see cref="Scope"/>) über einen Kanal (<see cref="Kind"/>) ausgelöst wird – als
/// In-Process-Notification oder ausgehender Webhook.
/// </summary>
public sealed class TriggerDefinition
{
    /// <summary>Eindeutiger Primärschlüssel der Trigger-Definition.</summary>
    public Guid Id { get; set; }

    /// <summary>Fremdschlüssel auf den zugehörigen <see cref="Dialog"/>.</summary>
    public Guid DialogId { get; set; }

    /// <summary>Der Zeitpunkt im Dialogablauf, zu dem der Trigger auslöst.</summary>
    public TriggerScope Scope { get; set; }

    /// <summary>
    /// Verweis auf die Frage (<see cref="Question.Id"/>) bei <see cref="TriggerScope.AfterQuestion"/>;
    /// andernfalls <see langword="null"/>.
    /// </summary>
    public Guid? QuestionId { get; set; }

    /// <summary>Der Kanal, über den der Trigger die Host-Anwendung benachrichtigt.</summary>
    public TriggerKind Kind { get; set; }

    /// <summary>
    /// Kanal-spezifische Konfiguration als JSON (z. B. Webhook-URL/Name oder
    /// Notification-Parameter).
    /// </summary>
    public required string Config { get; set; }

    /// <summary>
    /// Optionaler Bedingungsausdruck, der über <see cref="Flirty.Expressions.IExpressionEvaluator"/>
    /// ausgewertet wird und über das Auslösen entscheidet.
    /// </summary>
    public string? Expression { get; set; }

    /// <summary>Der Dialog, zu dem diese Trigger-Definition gehört.</summary>
    public Dialog Dialog { get; set; } = null!;
}
