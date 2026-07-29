namespace Flirty.Domain;

/// <summary>
/// Defines a back channel into the host application that is fired at a specific point in time
/// (<see cref="Scope"/>) over a channel (<see cref="Kind"/>) - as an
/// in-process notification or an outgoing webhook.
/// </summary>
public sealed class TriggerDefinition
{
    /// <summary>Unique primary key of the trigger definition.</summary>
    public Guid Id { get; set; }

    /// <summary>Foreign key to the owning <see cref="Dialog"/>.</summary>
    public Guid DialogId { get; set; }

    /// <summary>The point in the dialog flow at which the trigger fires.</summary>
    public TriggerScope Scope { get; set; }

    /// <summary>
    /// Reference to the question (<see cref="Question.Id"/>) for <see cref="TriggerScope.AfterQuestion"/>;
    /// otherwise <see langword="null"/>.
    /// </summary>
    public Guid? QuestionId { get; set; }

    /// <summary>The channel over which the trigger notifies the host application.</summary>
    public TriggerKind Kind { get; set; }

    /// <summary>
    /// Channel-specific configuration as JSON (e.g. webhook URL/name or
    /// notification parameters).
    /// </summary>
    public required string Config { get; set; }

    /// <summary>
    /// Optional condition expression that is evaluated via <see cref="Flirty.Expressions.IExpressionEvaluator"/>
    /// and decides whether to fire.
    /// </summary>
    public string? Expression { get; set; }

    /// <summary>The dialog this trigger definition belongs to.</summary>
    public Dialog Dialog { get; set; } = null!;
}
