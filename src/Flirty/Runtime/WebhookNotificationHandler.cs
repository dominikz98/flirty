using System.Text;
using System.Text.Json;
using Flirty.Domain;
using Flirty.Expressions;
using Flirty.Persistence;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Flirty.Runtime;

/// <summary>
/// Eingebauter Outbound-Webhook-Handler (Issue #33, seit #42 auch <see cref="TriggerDefinition"/>-getrieben):
/// empfängt die vier In-Process-Trigger-Notifications (<see cref="DialogStartedNotification"/>/
/// <see cref="AnswerSubmittedNotification"/>/<see cref="QuestionAnsweredNotification"/>/
/// <see cref="DialogCompletedNotification"/>) und liefert sie als HTTP-POST aus – über
/// <see cref="IHttpClientFactory"/> mit der Standard-Resilience-Pipeline (Retry/Timeout).
/// </summary>
/// <remarks>
/// <para>
/// Der Handler liegt im Core, weil ihn der martinothamar-Source-Generator dort entdeckt und automatisch
/// je implementiertem <see cref="INotificationHandler{TNotification}"/> registriert (dieselbe
/// Scoped-Lebensdauer wie der Mediator). Die Ziele kommen aus <b>zwei</b> Quellen, die sich ergänzen:
/// </para>
/// <list type="number">
/// <item><description>
/// den im Code registrierten <see cref="FlirtyWebhookRegistration"/> (<c>o.AddWebhook(scope, url, expression?)</c>);
/// nur Registrierungen mit gesetztem <see cref="FlirtyWebhookRegistration.Scope"/> werden ausgeliefert.
/// </description></item>
/// <item><description>
/// den am Dialog konfigurierten <see cref="TriggerDefinition"/> mit <see cref="TriggerKind.Webhook"/>
/// (Designer, #42) – gefiltert über <see cref="IDialogStore.GetTriggersForSessionAsync"/> nach dem
/// <see cref="TriggerScope"/> und bei <see cref="TriggerScope.AfterQuestion"/> zusätzlich nach der Frage.
/// Ziel-URL und Ereignisname stehen als JSON in <see cref="TriggerDefinition.Config"/> (Schema:
/// <see cref="TriggerConfig"/>). Definitionen mit <see cref="TriggerKind.InProcess"/> werden bewusst
/// <b>nicht</b> zugestellt: dort reagiert die Host-App über einen eigenen
/// <see cref="INotificationHandler{TNotification}"/>.
/// </description></item>
/// </list>
/// <para>
/// Trägt ein Ziel eine Bedingung, lädt der Handler Session und Dialog über den <see cref="IDialogStore"/>
/// nach, baut den <see cref="ExpressionContext"/> und wertet sie via <see cref="IExpressionEvaluator"/> aus.
/// Alles ist <b>best-effort</b>: unlesbare Konfiguration, nicht auswertbare Bedingungen und Zustellfehler
/// (Statuscode ≥ 400 nach erschöpften Retries oder Ausnahmen) werden protokolliert, aber <b>nicht</b>
/// weitergeworfen – ein toter Webhook oder ein Tippfehler im Designer darf den auslösenden Command
/// (Start/Submit/Edit) nicht brechen.
/// </para>
/// </remarks>
internal sealed class WebhookNotificationHandler :
    INotificationHandler<DialogStartedNotification>,
    INotificationHandler<AnswerSubmittedNotification>,
    INotificationHandler<QuestionAnsweredNotification>,
    INotificationHandler<DialogCompletedNotification>
{
    /// <summary>Name des über <c>AddHttpClient</c> registrierten Named-Clients samt Resilience-Pipeline.</summary>
    internal const string HttpClientName = "Flirty.Webhooks";

    /// <summary>HTTP-Header, der den auslösenden <see cref="TriggerScope"/> an den Empfänger mitgibt.</summary>
    internal const string EventHeaderName = "X-Flirty-Event";

    /// <summary>
    /// HTTP-Header mit dem fachlichen Ereignisnamen aus <see cref="TriggerConfig.Name"/> – nur gesetzt,
    /// wenn die Trigger-Definition einen Namen trägt.
    /// </summary>
    internal const string TriggerHeaderName = "X-Flirty-Trigger";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IReadOnlyList<FlirtyWebhookRegistration> _registrations;
    private readonly IExpressionEvaluator _evaluator;
    private readonly IDialogStore _dialogStore;
    private readonly ILogger<WebhookNotificationHandler> _logger;

    /// <summary>Erstellt den Handler mit seinen Abhängigkeiten.</summary>
    /// <param name="httpClientFactory">Factory für den resilient konfigurierten Webhook-<c>HttpClient</c>.</param>
    /// <param name="registrations">Die registrierten Webhook-Ziele (aus <c>o.AddWebhook(...)</c>).</param>
    /// <param name="evaluator">Engine zur Auswertung optionaler Bedingungsausdrücke.</param>
    /// <param name="dialogStore">Store zum Nachladen von Session/Dialog für die Ausdrucksauswertung.</param>
    /// <param name="logger">Logger für Zustellfehler (best-effort).</param>
    /// <exception cref="ArgumentNullException">Ein Argument ist <see langword="null"/>.</exception>
    public WebhookNotificationHandler(
        IHttpClientFactory httpClientFactory,
        IReadOnlyList<FlirtyWebhookRegistration> registrations,
        IExpressionEvaluator evaluator,
        IDialogStore dialogStore,
        ILogger<WebhookNotificationHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(registrations);
        ArgumentNullException.ThrowIfNull(evaluator);
        ArgumentNullException.ThrowIfNull(dialogStore);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClientFactory = httpClientFactory;
        _registrations = registrations;
        _evaluator = evaluator;
        _dialogStore = dialogStore;
        _logger = logger;
    }

    /// <summary>Liefert Webhooks des Scopes <see cref="TriggerScope.OnDialogStarted"/> aus.</summary>
    /// <param name="notification">Die Start-Notification.</param>
    /// <param name="cancellationToken">Token zum Abbrechen der Auslieferung.</param>
    /// <returns>Ein <see cref="ValueTask"/>, der mit Abschluss der Auslieferung abgeschlossen ist.</returns>
    public ValueTask Handle(DialogStartedNotification notification, CancellationToken cancellationToken)
        => DispatchAsync(
            TriggerScope.OnDialogStarted, notification.SessionId, notification.CurrentQuestionId, notification, cancellationToken);

    /// <summary>Liefert Webhooks des Scopes <see cref="TriggerScope.AfterAnswer"/> aus.</summary>
    /// <param name="notification">Die Antwort-Notification.</param>
    /// <param name="cancellationToken">Token zum Abbrechen der Auslieferung.</param>
    /// <returns>Ein <see cref="ValueTask"/>, der mit Abschluss der Auslieferung abgeschlossen ist.</returns>
    public ValueTask Handle(AnswerSubmittedNotification notification, CancellationToken cancellationToken)
        => DispatchAsync(
            TriggerScope.AfterAnswer, notification.SessionId, notification.QuestionId, notification, cancellationToken);

    /// <summary>Liefert Webhooks des Scopes <see cref="TriggerScope.AfterQuestion"/> aus.</summary>
    /// <param name="notification">Die Übergangs-Notification.</param>
    /// <param name="cancellationToken">Token zum Abbrechen der Auslieferung.</param>
    /// <returns>Ein <see cref="ValueTask"/>, der mit Abschluss der Auslieferung abgeschlossen ist.</returns>
    public ValueTask Handle(QuestionAnsweredNotification notification, CancellationToken cancellationToken)
        => DispatchAsync(
            TriggerScope.AfterQuestion, notification.SessionId, notification.QuestionId, notification, cancellationToken);

    /// <summary>Liefert Webhooks des Scopes <see cref="TriggerScope.OnDialogCompleted"/> aus.</summary>
    /// <param name="notification">Die Abschluss-Notification.</param>
    /// <param name="cancellationToken">Token zum Abbrechen der Auslieferung.</param>
    /// <returns>Ein <see cref="ValueTask"/>, der mit Abschluss der Auslieferung abgeschlossen ist.</returns>
    public ValueTask Handle(DialogCompletedNotification notification, CancellationToken cancellationToken)
        => DispatchAsync(
            TriggerScope.OnDialogCompleted, notification.SessionId, currentQuestionId: null, notification, cancellationToken);

    private async ValueTask DispatchAsync<TNotification>(
        TriggerScope scope,
        Guid sessionId,
        Guid? currentQuestionId,
        TNotification payload,
        CancellationToken cancellationToken)
        where TNotification : INotification
    {
        List<WebhookTarget> targets =
        [
            .. _registrations
                .Where(registration => registration.Scope == scope)
                .Select(registration => new WebhookTarget(registration.Url, registration.Expression, Name: null)),
            .. await LoadConfiguredTargetsAsync(scope, sessionId, currentQuestionId, cancellationToken)
                .ConfigureAwait(false),
        ];

        if (targets.Count == 0)
        {
            return;
        }

        // Session/Dialog nur nachladen, wenn mindestens ein Ziel eine Bedingung trägt.
        ExpressionContext? context = null;
        if (targets.Exists(target => !string.IsNullOrWhiteSpace(target.Expression)))
        {
            context = await BuildContextAsync(sessionId, currentQuestionId, cancellationToken).ConfigureAwait(false);
        }

        string? body = null;
        foreach (var target in targets)
        {
            if (!string.IsNullOrWhiteSpace(target.Expression) && !ConditionHolds(target, context))
            {
                continue;
            }

            body ??= JsonSerializer.Serialize(payload, SerializerOptions);
            await DeliverAsync(target, scope, body, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Liest die am Dialog konfigurierten Webhook-Trigger der Session und bildet sie auf Zustellziele ab.
    /// Unbrauchbare Definitionen (kein lesbares JSON, keine Ziel-URL) werden protokolliert und
    /// übersprungen – die Admin-Commands verhindern sie beim Speichern, von Hand geschriebene Zeilen
    /// dürfen den auslösenden Command aber trotzdem nicht brechen.
    /// </summary>
    private async ValueTask<IReadOnlyList<WebhookTarget>> LoadConfiguredTargetsAsync(
        TriggerScope scope, Guid sessionId, Guid? currentQuestionId, CancellationToken cancellationToken)
    {
        var triggers = await _dialogStore
            .GetTriggersForSessionAsync(sessionId, scope, cancellationToken)
            .ConfigureAwait(false);

        if (triggers.Count == 0)
        {
            return [];
        }

        var targets = new List<WebhookTarget>(triggers.Count);
        foreach (var trigger in triggers)
        {
            // In-Process-Trigger sind reine Marker: die Notification wird ohnehin publiziert, behandelt
            // wird sie von einem Handler der Host-App.
            if (trigger.Kind != TriggerKind.Webhook)
            {
                continue;
            }

            // Der Frage-Bezug gilt nur für AfterQuestion; ein leerer Verweis meint dort „jede Frage".
            if (scope == TriggerScope.AfterQuestion
                && trigger.QuestionId is { } questionId
                && questionId != currentQuestionId)
            {
                continue;
            }

            if (!TriggerConfig.TryParse(trigger.Config, out var config, out var error))
            {
                _logger.LogError(
                    "Trigger {TriggerId} ({Scope}) hat eine unlesbare Konfiguration und wird übersprungen: {Error}",
                    trigger.Id, scope, error);
                continue;
            }

            if (string.IsNullOrWhiteSpace(config.Url))
            {
                _logger.LogError(
                    "Trigger {TriggerId} ({Scope}) ist als Webhook konfiguriert, hat aber keine Ziel-URL – übersprungen.",
                    trigger.Id, scope);
                continue;
            }

            targets.Add(new WebhookTarget(config.Url, trigger.Expression, config.Name));
        }

        return targets;
    }

    /// <summary>
    /// Wertet die Bedingung eines Ziels aus. Fehler (unbekannter Bezeichner – z. B. eine Antwort, die es
    /// beim Dialogstart noch gar nicht gibt – oder ein nicht-boolesches Ergebnis) führen zum Überspringen
    /// des Ziels, nicht zum Abbruch des auslösenden Commands.
    /// </summary>
    private bool ConditionHolds(WebhookTarget target, ExpressionContext? context)
    {
        if (context is null)
        {
            // Session/Dialog nicht ladbar – bereits in BuildContextAsync protokolliert.
            return false;
        }

        try
        {
            return _evaluator.Evaluate(target.Expression!, context);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(
                exception,
                "Die Bedingung '{Expression}' des Webhooks an {Url} konnte nicht ausgewertet werden – keine Zustellung.",
                target.Expression, target.Url);
            return false;
        }
    }

    private async ValueTask<ExpressionContext?> BuildContextAsync(
        Guid sessionId, Guid? currentQuestionId, CancellationToken cancellationToken)
    {
        var session = await _dialogStore.GetSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            _logger.LogWarning(
                "Webhook: Session {SessionId} für die Ausdrucksauswertung nicht gefunden – bedingte Webhooks werden übersprungen.",
                sessionId);
            return null;
        }

        var dialog = await _dialogStore.GetDialogAsync(session.DialogId, cancellationToken).ConfigureAwait(false);
        if (dialog is null)
        {
            _logger.LogWarning(
                "Webhook: Dialog {DialogId} für die Ausdrucksauswertung nicht gefunden – bedingte Webhooks werden übersprungen.",
                session.DialogId);
            return null;
        }

        return SessionExpressionContextBuilder.Build(dialog, session, currentQuestionId);
    }

    private async ValueTask DeliverAsync(
        WebhookTarget target, TriggerScope scope, string body, CancellationToken cancellationToken)
    {
        try
        {
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var request = new HttpRequestMessage(HttpMethod.Post, target.Url) { Content = content };
            request.Headers.TryAddWithoutValidation(EventHeaderName, scope.ToString());

            if (!string.IsNullOrWhiteSpace(target.Name))
            {
                request.Headers.TryAddWithoutValidation(TriggerHeaderName, target.Name);
            }

            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "Webhook an {Url} ({Scope}) endete nach Retries mit HTTP {StatusCode}.",
                    target.Url, scope, (int)response.StatusCode);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Best-effort: ein fehlerhafter Webhook darf den auslösenden Command nicht brechen.
            _logger.LogError(exception, "Webhook an {Url} ({Scope}) fehlgeschlagen.", target.Url, scope);
        }
    }

    /// <summary>
    /// Ein aufgelöstes Zustellziel – aus einer Code-Registrierung (<see cref="FlirtyWebhookRegistration"/>)
    /// oder aus einer <see cref="TriggerDefinition"/>. Vereinheitlicht beide Quellen, damit Bedingung und
    /// Zustellung nur einmal existieren.
    /// </summary>
    /// <param name="Url">Die Ziel-URL des HTTP-POST.</param>
    /// <param name="Expression">Optionaler Bedingungsausdruck.</param>
    /// <param name="Name">Optionaler Ereignisname für den <see cref="TriggerHeaderName"/>-Header.</param>
    private sealed record WebhookTarget(string Url, string? Expression, string? Name);
}
