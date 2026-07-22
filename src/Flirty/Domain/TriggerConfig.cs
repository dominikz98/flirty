using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Flirty.Domain;

/// <summary>
/// Deserialisiertes Modell der kanal-spezifischen Trigger-Konfiguration
/// (<see cref="TriggerDefinition.Config"/>, als JSON gespeichert). Alle Felder sind optional; welche
/// davon <b>erforderlich</b> sind, hängt vom <see cref="TriggerKind"/> ab (siehe
/// <see cref="TryValidate"/>).
/// </summary>
/// <remarks>
/// <para>
/// Das JSON verwendet camelCase-Feldnamen (z. B. <c>{ "url": "https://host.example/hook",
/// "name": "order-created" }</c>); gelesen wird case-insensitiv. Dieser Typ ist die <b>eine</b> Quelle
/// des Schemas: die Admin-Commands validieren damit, der eingebaute Webhook-Handler liest damit und der
/// Designer serialisiert dagegen – bewusst kein Duplikat je Schicht (wie
/// <see cref="Flirty.Validation.ValidationRules"/> bei den Antwort-Regeln).
/// </para>
/// <para>
/// Unbekannte Felder überstehen einen Lese-/Schreibzyklus <b>nicht</b>: <see cref="ToJson"/> schreibt
/// ausschließlich die hier deklarierten. Wer fremde Felder erhalten will (z. B. der Roh-JSON-Modus des
/// Designers), gibt den gespeicherten Text unverändert weiter, statt ihn über diesen Typ zu führen.
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
    /// Optionaler fachlicher Ereignisname des Triggers (z. B. <c>order-created</c>). Bei
    /// <see cref="TriggerKind.Webhook"/> wird er als HTTP-Header <c>X-Flirty-Trigger</c> mitgeliefert,
    /// bei <see cref="TriggerKind.InProcess"/> dient er der Host-Anwendung als Bezeichnung.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Ziel-URL des ausgehenden Webhooks. Bei <see cref="TriggerKind.Webhook"/> erforderlich und eine
    /// absolute <c>http</c>-/<c>https</c>-Adresse; bei <see cref="TriggerKind.InProcess"/> ohne Bedeutung.
    /// </summary>
    public string? Url { get; init; }

    /// <summary>
    /// Liest die Konfiguration aus dem gespeicherten JSON. Ein leerer Text gilt als leere Konfiguration
    /// (und scheitert erst in <see cref="TryValidate"/>, sofern der Kanal Pflichtfelder hat).
    /// </summary>
    /// <param name="json">Der gespeicherte JSON-Text.</param>
    /// <param name="config">Die gelesene Konfiguration bei Erfolg, sonst <see langword="null"/>.</param>
    /// <param name="error">Die deutsche Fehlermeldung bei Misserfolg, sonst <see langword="null"/>.</param>
    /// <returns><see langword="true"/>, wenn das JSON lesbar war.</returns>
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
                error = "Die Trigger-Konfiguration muss ein JSON-Objekt sein "
                    + "(z. B. {\"url\": \"https://host.example/hook\"}).";
                return false;
            }

            config = JsonSerializer.Deserialize<TriggerConfig>(json, ReadOptions) ?? new TriggerConfig();
            return true;
        }
        catch (JsonException exception)
        {
            error = $"Die Trigger-Konfiguration ist kein gültiges JSON: {exception.Message}";
            return false;
        }
    }

    /// <summary>Serialisiert die Konfiguration in das gespeicherte JSON-Format (camelCase, ohne <c>null</c>-Felder).</summary>
    /// <returns>Der JSON-Text für <see cref="TriggerDefinition.Config"/>.</returns>
    public string ToJson() => JsonSerializer.Serialize(this, WriteOptions);

    /// <summary>
    /// Prüft die Konfiguration gegen die Anforderungen des angegebenen Kanals. Für
    /// <see cref="TriggerKind.Webhook"/> muss eine absolute <c>http</c>-/<c>https</c>-URL gesetzt sein –
    /// sonst könnte der Trigger gespeichert werden und würde zur Laufzeit still nie zustellen.
    /// </summary>
    /// <param name="kind">Der Kanal, gegen dessen Anforderungen geprüft wird.</param>
    /// <param name="error">Die deutsche Fehlermeldung bei Misserfolg, sonst <see langword="null"/>.</param>
    /// <returns><see langword="true"/>, wenn die Konfiguration zum Kanal passt.</returns>
    public bool TryValidate(TriggerKind kind, [NotNullWhen(false)] out string? error)
    {
        error = null;

        if (kind != TriggerKind.Webhook)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(Url))
        {
            error = "Ein Webhook-Trigger braucht eine Ziel-URL ('url' in der Konfiguration).";
            return false;
        }

        if (!Uri.TryCreate(Url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            error = $"Die Ziel-URL '{Url}' ist keine absolute http- oder https-Adresse.";
            return false;
        }

        return true;
    }
}
