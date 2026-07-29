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
/// Built-in outbound webhook handler (issue #33, since #42 also <see cref="TriggerDefinition"/>-driven):
/// receives the four in-process trigger notifications (<see cref="DialogStartedNotification"/>/
/// <see cref="AnswerSubmittedNotification"/>/<see cref="QuestionAnsweredNotification"/>/
/// <see cref="DialogCompletedNotification"/>) and delivers them as HTTP POST – via
/// <see cref="IHttpClientFactory"/> with the standard resilience pipeline (retry/timeout).
/// </summary>
/// <remarks>
/// <para>
/// The handler lives in the core because the martinothamar source generator discovers it there and
/// automatically registers it per implemented <see cref="INotificationHandler{TNotification}"/> (the same
/// scoped lifetime as the Mediator). The targets come from <b>two</b> sources that complement each other:
/// </para>
/// <list type="number">
/// <item><description>
/// the <see cref="FlirtyWebhookRegistration"/> registered in code (<c>o.AddWebhook(scope, url, expression?)</c>);
/// only registrations with <see cref="FlirtyWebhookRegistration.Scope"/> set are delivered.
/// </description></item>
/// <item><description>
/// the <see cref="TriggerDefinition"/> configured on the dialog with <see cref="TriggerKind.Webhook"/>
/// (designer, #42) – filtered via <see cref="IDialogStore.GetTriggersForSessionAsync"/> by the
/// <see cref="TriggerScope"/> and, for <see cref="TriggerScope.AfterQuestion"/>, additionally by the question.
/// Target URL and event name are stored as JSON in <see cref="TriggerDefinition.Config"/> (schema:
/// <see cref="TriggerConfig"/>). Definitions with <see cref="TriggerKind.InProcess"/> are deliberately
/// <b>not</b> delivered: there the host app reacts via its own
/// <see cref="INotificationHandler{TNotification}"/>.
/// </description></item>
/// </list>
/// <para>
/// If a target carries a condition, the handler loads session and dialog via the <see cref="IDialogStore"/>,
/// builds the <see cref="ExpressionContext"/> and evaluates it via <see cref="IExpressionEvaluator"/>.
/// Everything is <b>best-effort</b>: unreadable configuration, non-evaluable conditions and delivery errors
/// (status code ≥ 400 after exhausted retries or exceptions) are logged, but <b>not</b>
/// rethrown – a dead webhook or a typo in the designer must not break the triggering command
/// (start/submit/edit).
/// </para>
/// </remarks>
internal sealed class WebhookNotificationHandler :
    INotificationHandler<DialogStartedNotification>,
    INotificationHandler<AnswerSubmittedNotification>,
    INotificationHandler<QuestionAnsweredNotification>,
    INotificationHandler<DialogCompletedNotification>
{
    /// <summary>Name of the named client registered via <c>AddHttpClient</c> along with its resilience pipeline.</summary>
    internal const string HttpClientName = "Flirty.Webhooks";

    /// <summary>HTTP header that passes the triggering <see cref="TriggerScope"/> to the receiver.</summary>
    internal const string EventHeaderName = "X-Flirty-Event";

    /// <summary>
    /// HTTP header with the business event name from <see cref="TriggerConfig.Name"/> – only set
    /// if the trigger definition carries a name.
    /// </summary>
    internal const string TriggerHeaderName = "X-Flirty-Trigger";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IReadOnlyList<FlirtyWebhookRegistration> _registrations;
    private readonly IExpressionEvaluator _evaluator;
    private readonly IDialogStore _dialogStore;
    private readonly ILogger<WebhookNotificationHandler> _logger;

    /// <summary>Creates the handler with its dependencies.</summary>
    /// <param name="httpClientFactory">Factory for the resiliently configured webhook <c>HttpClient</c>.</param>
    /// <param name="registrations">The registered webhook targets (from <c>o.AddWebhook(...)</c>).</param>
    /// <param name="evaluator">Engine for evaluating optional condition expressions.</param>
    /// <param name="dialogStore">Store for reloading session/dialog for the expression evaluation.</param>
    /// <param name="logger">Logger for delivery errors (best-effort).</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
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

    /// <summary>Delivers webhooks of the scope <see cref="TriggerScope.OnDialogStarted"/>.</summary>
    /// <param name="notification">The start notification.</param>
    /// <param name="cancellationToken">Token to cancel the delivery.</param>
    /// <returns>A <see cref="ValueTask"/> that completes when the delivery is finished.</returns>
    public ValueTask Handle(DialogStartedNotification notification, CancellationToken cancellationToken)
        => DispatchAsync(
            TriggerScope.OnDialogStarted, notification.SessionId, notification.CurrentQuestionId, notification, cancellationToken);

    /// <summary>Delivers webhooks of the scope <see cref="TriggerScope.AfterAnswer"/>.</summary>
    /// <param name="notification">The answer notification.</param>
    /// <param name="cancellationToken">Token to cancel the delivery.</param>
    /// <returns>A <see cref="ValueTask"/> that completes when the delivery is finished.</returns>
    public ValueTask Handle(AnswerSubmittedNotification notification, CancellationToken cancellationToken)
        => DispatchAsync(
            TriggerScope.AfterAnswer, notification.SessionId, notification.QuestionId, notification, cancellationToken);

    /// <summary>Delivers webhooks of the scope <see cref="TriggerScope.AfterQuestion"/>.</summary>
    /// <param name="notification">The transition notification.</param>
    /// <param name="cancellationToken">Token to cancel the delivery.</param>
    /// <returns>A <see cref="ValueTask"/> that completes when the delivery is finished.</returns>
    public ValueTask Handle(QuestionAnsweredNotification notification, CancellationToken cancellationToken)
        => DispatchAsync(
            TriggerScope.AfterQuestion, notification.SessionId, notification.QuestionId, notification, cancellationToken);

    /// <summary>Delivers webhooks of the scope <see cref="TriggerScope.OnDialogCompleted"/>.</summary>
    /// <param name="notification">The completion notification.</param>
    /// <param name="cancellationToken">Token to cancel the delivery.</param>
    /// <returns>A <see cref="ValueTask"/> that completes when the delivery is finished.</returns>
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

        // Only reload session/dialog if at least one target carries a condition.
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
    /// Reads the webhook triggers configured on the session's dialog and maps them to delivery targets.
    /// Unusable definitions (no readable JSON, no target URL) are logged and
    /// skipped – the admin commands prevent them on save, but hand-written rows
    /// must still not break the triggering command.
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
            // In-process triggers are pure markers: the notification is published anyway, and it is
            // handled by a handler of the host app.
            if (trigger.Kind != TriggerKind.Webhook)
            {
                continue;
            }

            // The question reference applies only to AfterQuestion; an empty reference there means "any question".
            if (scope == TriggerScope.AfterQuestion
                && trigger.QuestionId is { } questionId
                && questionId != currentQuestionId)
            {
                continue;
            }

            if (!TriggerConfig.TryParse(trigger.Config, out var config, out var error))
            {
                _logger.LogError(
                    "Trigger {TriggerId} ({Scope}) has an unreadable configuration and is skipped: {Error}",
                    trigger.Id, scope, error);
                continue;
            }

            if (string.IsNullOrWhiteSpace(config.Url))
            {
                _logger.LogError(
                    "Trigger {TriggerId} ({Scope}) is configured as a webhook but has no target URL – skipped.",
                    trigger.Id, scope);
                continue;
            }

            targets.Add(new WebhookTarget(config.Url, trigger.Expression, config.Name));
        }

        return targets;
    }

    /// <summary>
    /// Evaluates the condition of a target. Errors (unknown identifier – e.g. an answer that does not yet
    /// exist at dialog start – or a non-boolean result) lead to skipping
    /// the target, not to aborting the triggering command.
    /// </summary>
    private bool ConditionHolds(WebhookTarget target, ExpressionContext? context)
    {
        if (context is null)
        {
            // Session/dialog not loadable – already logged in BuildContextAsync.
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
                "The condition '{Expression}' of the webhook to {Url} could not be evaluated – no delivery.",
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
                "Webhook: session {SessionId} for the expression evaluation not found – conditional webhooks are skipped.",
                sessionId);
            return null;
        }

        var dialog = await _dialogStore.GetDialogAsync(session.DialogId, cancellationToken).ConfigureAwait(false);
        if (dialog is null)
        {
            _logger.LogWarning(
                "Webhook: dialog {DialogId} for the expression evaluation not found – conditional webhooks are skipped.",
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
                    "Webhook to {Url} ({Scope}) ended after retries with HTTP {StatusCode}.",
                    target.Url, scope, (int)response.StatusCode);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Best-effort: a faulty webhook must not break the triggering command.
            _logger.LogError(exception, "Webhook to {Url} ({Scope}) failed.", target.Url, scope);
        }
    }

    /// <summary>
    /// A resolved delivery target – from a code registration (<see cref="FlirtyWebhookRegistration"/>)
    /// or from a <see cref="TriggerDefinition"/>. Unifies both sources so that condition and
    /// delivery exist only once.
    /// </summary>
    /// <param name="Url">The target URL of the HTTP POST.</param>
    /// <param name="Expression">Optional condition expression.</param>
    /// <param name="Name">Optional event name for the <see cref="TriggerHeaderName"/> header.</param>
    private sealed record WebhookTarget(string Url, string? Expression, string? Name);
}
