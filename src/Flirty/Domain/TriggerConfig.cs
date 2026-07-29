using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Flirty.Domain;

/// <summary>
/// Deserialized model of the channel-specific trigger configuration
/// (<see cref="TriggerDefinition.Config"/>, stored as JSON). All fields are optional; which
/// of them are <b>required</b> depends on the <see cref="TriggerKind"/> (see
/// <see cref="TryValidate"/>).
/// </summary>
/// <remarks>
/// <para>
/// The JSON uses camelCase field names (e.g. <c>{ "url": "https://host.example/hook",
/// "name": "order-created" }</c>); it is read case-insensitively. This type is the <b>single</b> source
/// of the schema: the admin commands validate with it, the built-in webhook handler reads with it and the
/// designer serializes against it - deliberately no duplicate per layer (like
/// <see cref="Flirty.Validation.ValidationRules"/> for the answer rules).
/// </para>
/// <para>
/// Unknown fields do <b>not</b> survive a read/write cycle: <see cref="ToJson"/> writes
/// exclusively the ones declared here. Whoever wants to preserve foreign fields (e.g. the raw-JSON mode of
/// the designer) passes the stored text through unchanged instead of routing it through this type.
/// </para>
/// </remarks>
public sealed record TriggerConfig
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Optional business event name of the trigger (e.g. <c>order-created</c>). For
    /// <see cref="TriggerKind.Webhook"/> it is delivered as the HTTP header <c>X-Flirty-Trigger</c>,
    /// for <see cref="TriggerKind.InProcess"/> it serves the host application as a label.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Target URL of the outgoing webhook. Required for <see cref="TriggerKind.Webhook"/> and an
    /// absolute <c>http</c>/<c>https</c> address; meaningless for <see cref="TriggerKind.InProcess"/>.
    /// </summary>
    public string? Url { get; init; }

    /// <summary>
    /// Reads the configuration from the stored JSON. An empty text is treated as an empty configuration
    /// (and only fails in <see cref="TryValidate"/> if the channel has required fields).
    /// </summary>
    /// <param name="json">The stored JSON text.</param>
    /// <param name="config">The read configuration on success, otherwise <see langword="null"/>.</param>
    /// <param name="error">The error message on failure, otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the JSON was readable.</returns>
    public static bool TryParse(
        string? json,
        [NotNullWhen(true)] out TriggerConfig? config,
        [NotNullWhen(false)] out string? error)
    {
        config = null;
        error = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            config = new TriggerConfig();
            return true;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "The trigger configuration must be a JSON object "
                    + "(e.g. {\"url\": \"https://host.example/hook\"}).";
                return false;
            }

            config = JsonSerializer.Deserialize<TriggerConfig>(json, ReadOptions) ?? new TriggerConfig();
            return true;
        }
        catch (JsonException exception)
        {
            error = $"The trigger configuration is not valid JSON: {exception.Message}";
            return false;
        }
    }

    /// <summary>Serializes the configuration into the stored JSON format (camelCase, without <c>null</c> fields).</summary>
    /// <returns>The JSON text for <see cref="TriggerDefinition.Config"/>.</returns>
    public string ToJson() => JsonSerializer.Serialize(this, WriteOptions);

    /// <summary>
    /// Checks the configuration against the requirements of the given channel. For
    /// <see cref="TriggerKind.Webhook"/> an absolute <c>http</c>/<c>https</c> URL must be set -
    /// otherwise the trigger could be saved and would silently never deliver at runtime.
    /// </summary>
    /// <param name="kind">The channel against whose requirements the check runs.</param>
    /// <param name="error">The error message on failure, otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the configuration fits the channel.</returns>
    public bool TryValidate(TriggerKind kind, [NotNullWhen(false)] out string? error)
    {
        error = null;

        if (kind != TriggerKind.Webhook)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(Url))
        {
            error = "A webhook trigger needs a target URL ('url' in the configuration).";
            return false;
        }

        if (!Uri.TryCreate(Url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            error = $"The target URL '{Url}' is not an absolute http or https address.";
            return false;
        }

        return true;
    }
}
