using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Flirty.Domain;
using Flirty.Runtime.Admin;

namespace Flirty.Designer.Models;

/// <summary>
/// Form model of the trigger editor (#42) – for creating a trigger in the dialog editor and for
/// its detail page. Deliberately mutable (settable properties), so that the Blazor <c>EditForm</c> can bind
/// directly to it.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="TriggerDefinition.Config"/> stored as JSON is mapped onto the individual fields
/// <see cref="Name"/> and <see cref="Url"/>. Authoritative here is the public core type
/// <see cref="TriggerConfig"/> – the schema is <b>not</b> duplicated here, but used directly as the
/// serialization type (pattern from <see cref="QuestionFormModel"/>).
/// </para>
/// <para>
/// If the stored JSON contains fields that <see cref="TriggerConfig"/> does not know (or if it is not
/// a valid JSON object at all), <see cref="From"/> switches to <see cref="UseRawJson"/>. Otherwise
/// saving would silently discard the foreign fields.
/// </para>
/// </remarks>
internal sealed class TriggerFormModel
{
    /// <summary>The JSON fields supported by <see cref="TriggerConfig"/> (case-insensitive as when reading).</summary>
    private static readonly HashSet<string> KnownConfigProperties =
        new(StringComparer.OrdinalIgnoreCase) { "name", "url" };

    /// <summary>The point in the dialog flow at which the trigger fires.</summary>
    public TriggerScope Scope { get; set; } = TriggerScope.OnDialogCompleted;

    /// <summary>
    /// The question that is listened to for <see cref="TriggerScope.AfterQuestion"/>. Deliberately
    /// <see cref="Guid"/>?, so that an <c>InputSelect</c> without a preselection can bind to it (pattern from
    /// <see cref="TransitionFormModel"/>).
    /// </summary>
    public Guid? QuestionId { get; set; }

    /// <summary>The channel over which the host application is notified.</summary>
    public TriggerKind Kind { get; set; } = TriggerKind.Webhook;

    /// <summary>The optional domain-level event name (header <c>X-Flirty-Trigger</c>).</summary>
    public string? Name { get; set; }

    /// <summary>The target URL of the webhook (required for <see cref="TriggerKind.Webhook"/>).</summary>
    public string? Url { get; set; }

    /// <summary>The condition expression; empty means "unconditionally firing".</summary>
    public string? Expression { get; set; }

    /// <summary>
    /// Indicates whether the configuration is edited as raw JSON. Set by <see cref="From"/>
    /// when the stored JSON is not losslessly mappable onto the individual fields.
    /// </summary>
    public bool UseRawJson { get; set; }

    /// <summary>The raw-edited configuration JSON; only relevant if <see cref="UseRawJson"/> is set.</summary>
    public string? RawJson { get; set; }

    /// <summary>Creates a form model from an existing trigger definition.</summary>
    /// <param name="trigger">The trigger view from the admin CRUD.</param>
    /// <returns>The populated form model.</returns>
    public static TriggerFormModel From(TriggerDetail trigger)
    {
        ArgumentNullException.ThrowIfNull(trigger);

        var model = new TriggerFormModel
        {
            Scope = trigger.Scope,
            QuestionId = trigger.QuestionId,
            Kind = trigger.Kind,
            Expression = trigger.Expression,
        };

        model.ReadConfig(trigger.Config);
        return model;
    }

    /// <summary>
    /// Builds the JSON for <see cref="TriggerDefinition.Config"/> from the input fields and checks it
    /// against the requirements of the chosen channel – with the same core type that the admin commands
    /// use. So a missing URL fails here with an understandable message instead of later as a
    /// silently undeliverable trigger.
    /// </summary>
    /// <param name="json">The produced JSON.</param>
    /// <param name="error">The error message, if the inputs are unusable.</param>
    /// <returns><see langword="true"/> if the configuration is valid.</returns>
    public bool TryBuildConfig(out string json, out string? error)
    {
        json = string.Empty;
        error = null;

        if (UseRawJson)
        {
            if (!TriggerConfig.TryParse(RawJson, out var raw, out error))
            {
                return false;
            }

            if (!raw.TryValidate(Kind, out error))
            {
                return false;
            }

            // Take over unchanged – foreign fields are thus preserved.
            json = string.IsNullOrWhiteSpace(RawJson) ? "{}" : RawJson;
            return true;
        }

        var config = new TriggerConfig
        {
            Name = string.IsNullOrWhiteSpace(Name) ? null : Name.Trim(),
            Url = string.IsNullOrWhiteSpace(Url) ? null : Url.Trim(),
        };

        if (!config.TryValidate(Kind, out error))
        {
            return false;
        }

        json = config.ToJson();
        return true;
    }

    /// <summary>
    /// Normalizes the expression for persistence: an empty/whitespace-only expression
    /// becomes <see langword="null"/> (unconditional), instead of landing in the column as an empty string.
    /// </summary>
    /// <returns>The expression to store or <see langword="null"/>.</returns>
    public string? NormalizedExpression()
        => string.IsNullOrWhiteSpace(Expression) ? null : Expression.Trim();

    /// <summary>
    /// Sets the question reference to match the point in time: only <see cref="TriggerScope.AfterQuestion"/>
    /// may carry one (the admin commands reject everything else).
    /// </summary>
    /// <returns>The question reference to store or <see langword="null"/>.</returns>
    public Guid? NormalizedQuestionId()
        => TriggerLabels.RequiresQuestion(Scope) ? QuestionId : null;

    /// <summary>
    /// Takes the stored configuration JSON into the individual fields – or falls back to
    /// raw editing if it is not losslessly mappable.
    /// </summary>
    /// <param name="config">The stored JSON.</param>
    private void ReadConfig(string? config)
    {
        if (string.IsNullOrWhiteSpace(config))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(config);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || document.RootElement.EnumerateObject().Any(property => !KnownConfigProperties.Contains(property.Name)))
            {
                UseRawJson = true;
                RawJson = config;
                return;
            }
        }
        catch (JsonException)
        {
            UseRawJson = true;
            RawJson = config;
            return;
        }

        // From here it is certain: a valid object, exclusively known fields.
        if (TriggerConfig.TryParse(config, out var parsed, out _))
        {
            Name = parsed.Name;
            Url = parsed.Url;
        }
    }
}
