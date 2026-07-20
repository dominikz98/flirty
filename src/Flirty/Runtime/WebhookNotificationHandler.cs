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
/// Eingebauter Outbound-Webhook-Handler (Issue #33): empfängt die vier In-Process-Trigger-Notifications
/// (<see cref="DialogStartedNotification"/>/<see cref="AnswerSubmittedNotification"/>/
/// <see cref="QuestionAnsweredNotification"/>/<see cref="DialogCompletedNotification"/>) und liefert sie
/// als HTTP-POST an die per <c>o.AddWebhook(scope, url, expression?)</c> registrierten Ziele aus – über
/// <see cref="IHttpClientFactory"/> mit der Standard-Resilience-Pipeline (Retry/Timeout).
/// </summary>
/// <remarks>
/// Der Handler liegt im Core, weil ihn der martinothamar-Source-Generator dort entdeckt und automatisch
/// je implementiertem <see cref="INotificationHandler{TNotification}"/> registriert (dieselbe
/// Scoped-Lebensdauer wie der Mediator). Er filtert die bereitgestellten
/// <see cref="FlirtyWebhookRegistration"/> nach dem <see cref="TriggerScope"/> der jeweiligen Notification;
/// nur Registrierungen mit gesetztem <see cref="FlirtyWebhookRegistration.Scope"/> werden ausgeliefert.
/// Trägt eine Registrierung einen <see cref="FlirtyWebhookRegistration.Expression"/>, lädt der Handler
/// Session und Dialog über den <see cref="IDialogStore"/> nach, baut den
/// <see cref="ExpressionContext"/> und wertet die Bedingung via <see cref="IExpressionEvaluator"/> aus.
/// Die Auslieferung ist <b>best-effort</b>: Fehler (Statuscode ≥ 400 nach erschöpften Retries oder
/// Ausnahmen) werden protokolliert, aber <b>nicht</b> weitergeworfen, damit ein toter Webhook den
/// auslösenden Command nicht bricht.
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
        var matching = _registrations.Where(registration => registration.Scope == scope).ToList();
        if (matching.Count == 0)
        {
            return;
        }

        // Session/Dialog nur nachladen, wenn mindestens eine Registrierung eine Bedingung trägt.
        ExpressionContext? context = null;
        if (matching.Any(registration => !string.IsNullOrWhiteSpace(registration.Expression)))
        {
            context = await BuildContextAsync(sessionId, currentQuestionId, cancellationToken).ConfigureAwait(false);
        }

        string? body = null;
        foreach (var registration in matching)
        {
            if (!string.IsNullOrWhiteSpace(registration.Expression)
                && (context is null || !_evaluator.Evaluate(registration.Expression, context)))
            {
                continue;
            }

            body ??= JsonSerializer.Serialize(payload, SerializerOptions);
            await DeliverAsync(registration, scope, body, cancellationToken).ConfigureAwait(false);
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
        FlirtyWebhookRegistration registration, TriggerScope scope, string body, CancellationToken cancellationToken)
    {
        try
        {
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var request = new HttpRequestMessage(HttpMethod.Post, registration.Url) { Content = content };
            request.Headers.TryAddWithoutValidation(EventHeaderName, scope.ToString());

            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "Webhook an {Url} ({Scope}) endete nach Retries mit HTTP {StatusCode}.",
                    registration.Url, scope, (int)response.StatusCode);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Best-effort: ein fehlerhafter Webhook darf den auslösenden Command nicht brechen.
            _logger.LogError(exception, "Webhook an {Url} ({Scope}) fehlgeschlagen.", registration.Url, scope);
        }
    }
}
