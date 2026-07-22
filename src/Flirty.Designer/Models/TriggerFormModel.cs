using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Flirty.Domain;
using Flirty.Runtime.Admin;

namespace Flirty.Designer.Models;

/// <summary>
/// Formular-Modell des Trigger-Editors (#42) – für das Anlegen eines Triggers im Dialog-Editor und für
/// seine Detailseite. Bewusst veränderbar (settable Properties), damit die Blazor-<c>EditForm</c> direkt
/// daran binden kann.
/// </summary>
/// <remarks>
/// <para>
/// Die als JSON gespeicherte <see cref="TriggerDefinition.Config"/> wird auf die Einzelfelder
/// <see cref="Name"/> und <see cref="Url"/> abgebildet. Maßgeblich ist dabei der öffentliche Core-Typ
/// <see cref="TriggerConfig"/> – das Schema wird hier <b>nicht</b> dupliziert, sondern direkt als
/// Serialisierungstyp benutzt (Muster aus <see cref="QuestionFormModel"/>).
/// </para>
/// <para>
/// Enthält das gespeicherte JSON Felder, die <see cref="TriggerConfig"/> nicht kennt (oder ist es gar
/// kein gültiges JSON-Objekt), schaltet <see cref="From"/> auf <see cref="UseRawJson"/> um. Sonst würde
/// das Speichern die fremden Felder stillschweigend verwerfen.
/// </para>
/// </remarks>
internal sealed class TriggerFormModel
{
    /// <summary>Die von <see cref="TriggerConfig"/> unterstützten JSON-Felder (case-insensitiv wie beim Lesen).</summary>
    private static readonly HashSet<string> KnownConfigProperties =
        new(StringComparer.OrdinalIgnoreCase) { "name", "url" };

    /// <summary>Der Zeitpunkt im Dialogablauf, zu dem der Trigger auslöst.</summary>
    public TriggerScope Scope { get; set; } = TriggerScope.OnDialogCompleted;

    /// <summary>
    /// Die Frage, auf die bei <see cref="TriggerScope.AfterQuestion"/> gehört wird. Bewusst
    /// <see cref="Guid"/>?, damit ein <c>InputSelect</c> ohne Vorauswahl daran binden kann (Muster aus
    /// <see cref="TransitionFormModel"/>).
    /// </summary>
    public Guid? QuestionId { get; set; }

    /// <summary>Der Kanal, über den die Host-Anwendung benachrichtigt wird.</summary>
    public TriggerKind Kind { get; set; } = TriggerKind.Webhook;

    /// <summary>Der optionale fachliche Ereignisname (Header <c>X-Flirty-Trigger</c>).</summary>
    public string? Name { get; set; }

    /// <summary>Die Ziel-URL des Webhooks (bei <see cref="TriggerKind.Webhook"/> Pflicht).</summary>
    public string? Url { get; set; }

    /// <summary>Der Bedingungsausdruck; leer bedeutet „bedingungslos auslösend".</summary>
    public string? Expression { get; set; }

    /// <summary>
    /// Gibt an, ob die Konfiguration als Roh-JSON bearbeitet wird. Wird von <see cref="From"/> gesetzt,
    /// wenn das gespeicherte JSON nicht verlustfrei auf die Einzelfelder abbildbar ist.
    /// </summary>
    public bool UseRawJson { get; set; }

    /// <summary>Das roh bearbeitete Konfigurations-JSON; nur relevant, wenn <see cref="UseRawJson"/> gesetzt ist.</summary>
    public string? RawJson { get; set; }

    /// <summary>Erzeugt ein Formular-Modell aus einer bestehenden Trigger-Definition.</summary>
    /// <param name="trigger">Die Trigger-Sicht aus dem Admin-CRUD.</param>
    /// <returns>Das befüllte Formular-Modell.</returns>
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
    /// Baut aus den Eingabefeldern das JSON für <see cref="TriggerDefinition.Config"/> und prüft es
    /// gegen die Anforderungen des gewählten Kanals – mit demselben Core-Typ, den die Admin-Commands
    /// verwenden. So scheitert eine fehlende URL hier mit einer verständlichen Meldung statt später als
    /// still nicht zustellender Trigger.
    /// </summary>
    /// <param name="json">Das erzeugte JSON.</param>
    /// <param name="error">Die deutsche Fehlermeldung, falls die Eingaben unbrauchbar sind.</param>
    /// <returns><see langword="true"/>, wenn die Konfiguration gültig ist.</returns>
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

            // Unverändert übernehmen – fremde Felder bleiben so erhalten.
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
    /// Normalisiert den Ausdruck für die Persistenz: Ein leerer/nur aus Leerraum bestehender Ausdruck
    /// wird zu <see langword="null"/> (bedingungslos), statt als leere Zeichenkette in der Spalte zu landen.
    /// </summary>
    /// <returns>Der zu speichernde Ausdruck oder <see langword="null"/>.</returns>
    public string? NormalizedExpression()
        => string.IsNullOrWhiteSpace(Expression) ? null : Expression.Trim();

    /// <summary>
    /// Setzt den Frage-Verweis passend zum Zeitpunkt: nur <see cref="TriggerScope.AfterQuestion"/> darf
    /// einen tragen (die Admin-Commands weisen alles andere zurück).
    /// </summary>
    /// <returns>Der zu speichernde Frage-Verweis oder <see langword="null"/>.</returns>
    public Guid? NormalizedQuestionId()
        => TriggerLabels.RequiresQuestion(Scope) ? QuestionId : null;

    /// <summary>
    /// Übernimmt das gespeicherte Konfigurations-JSON in die Einzelfelder – oder fällt auf die
    /// Roh-Bearbeitung zurück, wenn es nicht verlustfrei abbildbar ist.
    /// </summary>
    /// <param name="config">Das gespeicherte JSON.</param>
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

        // Ab hier steht fest: gültiges Objekt, ausschließlich bekannte Felder.
        if (TriggerConfig.TryParse(config, out var parsed, out _))
        {
            Name = parsed.Name;
            Url = parsed.Url;
        }
    }
}
