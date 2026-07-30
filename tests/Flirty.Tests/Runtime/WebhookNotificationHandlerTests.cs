using System.Net;
using Flirty.Domain;
using Flirty.Expressions;
using Flirty.Persistence;
using Flirty.Runtime;
using Flirty.Tests.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Flirty.Tests.Runtime;

/// <summary>
/// Checks the built-in <see cref="WebhookNotificationHandler"/> (#33, since #42 also
/// <see cref="TriggerDefinition"/>-driven) in isolation: scope filtering, HTTP delivery
/// (method/URL/header/body), the triggers configured on the dialog incl. their condition as well as
/// best-effort error handling and the retry on transient failures over the standard resilience
/// pipeline. No mocking library is used, deliberately – instead a hand-written
/// <see cref="RecordingHttpMessageHandler"/> serves as the <c>HttpMessageHandler</c> spy.
/// </summary>
public sealed class WebhookNotificationHandlerTests
{
    private const string TargetUrl = "https://example.test/hook";

    /// <summary>A matching scope produces exactly one POST with the event header and a JSON body.</summary>
    [Fact]
    public async Task Delivers_one_POST_with_the_event_header_and_a_body_on_a_matching_scope()
    {
        var spy = new RecordingHttpMessageHandler();
        var handler = CreateHandler(
            spy, [new FlirtyWebhookRegistration("OnDialogCompleted", TargetUrl, TriggerScope.OnDialogCompleted)]);

        var notification = new DialogCompletedNotification(Guid.NewGuid(), "onboarding", []);
        await handler.Handle(notification, CancellationToken.None);

        var request = Assert.Single(spy.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(TargetUrl, request.Url?.ToString());
        Assert.Equal("OnDialogCompleted", request.Event);
        Assert.Contains("onboarding", request.Body);
    }

    /// <summary>Notifications without a matching scope registration trigger no delivery.</summary>
    [Fact]
    public async Task Delivers_nothing_when_no_scope_matches()
    {
        var spy = new RecordingHttpMessageHandler();
        var handler = CreateHandler(
            spy, [new FlirtyWebhookRegistration("OnDialogCompleted", TargetUrl, TriggerScope.OnDialogCompleted)]);

        var notification = new AnswerSubmittedNotification(Guid.NewGuid(), "onboarding", Guid.NewGuid(), "\"x\"", null, null);
        await handler.Handle(notification, CancellationToken.None);

        Assert.Empty(spy.Requests);
    }

    /// <summary>Registrations without a <c>Scope</c> (the old string overload) are not delivered.</summary>
    [Fact]
    public async Task Ignores_registrations_without_a_scope()
    {
        var spy = new RecordingHttpMessageHandler();
        var handler = CreateHandler(spy, [new FlirtyWebhookRegistration("order-created", TargetUrl)]);

        await handler.Handle(new DialogCompletedNotification(Guid.NewGuid(), "onboarding", []), CancellationToken.None);

        Assert.Empty(spy.Requests);
    }

    /// <summary>Several registrations of the same scope are all served.</summary>
    [Fact]
    public async Task Delivers_to_several_targets_of_the_same_scope()
    {
        var spy = new RecordingHttpMessageHandler();
        var handler = CreateHandler(spy,
        [
            new FlirtyWebhookRegistration("OnDialogCompleted", "https://example.test/a", TriggerScope.OnDialogCompleted),
            new FlirtyWebhookRegistration("OnDialogCompleted", "https://example.test/b", TriggerScope.OnDialogCompleted),
        ]);

        await handler.Handle(new DialogCompletedNotification(Guid.NewGuid(), "onboarding", []), CancellationToken.None);

        Assert.Equal(2, spy.Requests.Count);
        Assert.Contains(spy.Requests, request => request.Url?.ToString() == "https://example.test/a");
        Assert.Contains(spy.Requests, request => request.Url?.ToString() == "https://example.test/b");
    }

    /// <summary>An error status (after the retries are exhausted) is logged but not thrown.</summary>
    [Fact]
    public async Task An_error_status_is_swallowed()
    {
        var spy = new RecordingHttpMessageHandler(HttpStatusCode.InternalServerError);
        var handler = CreateHandler(
            spy, [new FlirtyWebhookRegistration("OnDialogCompleted", TargetUrl, TriggerScope.OnDialogCompleted)]);

        // No throw: the triggering command must not fail.
        await handler.Handle(new DialogCompletedNotification(Guid.NewGuid(), "onboarding", []), CancellationToken.None);

        Assert.Single(spy.Requests);
    }

    /// <summary>A transport exception is logged but not thrown.</summary>
    [Fact]
    public async Task An_exception_is_swallowed()
    {
        var spy = RecordingHttpMessageHandler.Throwing();
        var handler = CreateHandler(
            spy, [new FlirtyWebhookRegistration("OnDialogCompleted", TargetUrl, TriggerScope.OnDialogCompleted)]);

        var exception = await Record.ExceptionAsync(() =>
            handler.Handle(new DialogCompletedNotification(Guid.NewGuid(), "onboarding", []), CancellationToken.None).AsTask());

        Assert.Null(exception);
    }

    /// <summary>A transient failure (503) is retried over the resilience pipeline.</summary>
    [Fact]
    public async Task Retries_on_a_transient_failure()
    {
        var spy = new RecordingHttpMessageHandler(HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);
        var handler = CreateHandler(
            spy,
            [new FlirtyWebhookRegistration("OnDialogCompleted", TargetUrl, TriggerScope.OnDialogCompleted)],
            withResilience: true);

        await handler.Handle(new DialogCompletedNotification(Guid.NewGuid(), "onboarding", []), CancellationToken.None);

        // First attempt 503 -> retry -> second attempt 200: exactly two delivery attempts.
        Assert.Equal(2, spy.Requests.Count);
    }

    /// <summary>A webhook trigger configured on the dialog (#42) is delivered – with the event name in the header.</summary>
    [Fact]
    public async Task Delivers_the_webhook_triggers_configured_on_the_dialog()
    {
        var spy = new RecordingHttpMessageHandler();
        var handler = CreateHandler(
            spy,
            registrations: [],
            store: new StubDialogStore(
            [
                Trigger(TriggerScope.OnDialogCompleted, $"{{\"url\":\"{TargetUrl}\",\"name\":\"order-created\"}}"),
            ]));

        await handler.Handle(new DialogCompletedNotification(Guid.NewGuid(), "onboarding", []), CancellationToken.None);

        var request = Assert.Single(spy.Requests);
        Assert.Equal(TargetUrl, request.Url?.ToString());
        Assert.Equal("OnDialogCompleted", request.Event);
        Assert.Equal("order-created", request.Trigger);
    }

    /// <summary>In-process triggers are markers for the host app and are not delivered.</summary>
    [Fact]
    public async Task Ignores_in_process_triggers()
    {
        var spy = new RecordingHttpMessageHandler();
        var handler = CreateHandler(
            spy,
            registrations: [],
            store: new StubDialogStore(
            [
                Trigger(TriggerScope.OnDialogCompleted, $"{{\"url\":\"{TargetUrl}\"}}", kind: TriggerKind.InProcess),
            ]));

        await handler.Handle(new DialogCompletedNotification(Guid.NewGuid(), "onboarding", []), CancellationToken.None);

        Assert.Empty(spy.Requests);
    }

    /// <summary>For <c>AfterQuestion</c> the question reference counts: only the trigger of the answered question fires.</summary>
    [Fact]
    public async Task Filters_AfterQuestion_triggers_down_to_the_question()
    {
        var questionId = Guid.NewGuid();
        var spy = new RecordingHttpMessageHandler();
        var handler = CreateHandler(
            spy,
            registrations: [],
            store: new StubDialogStore(
            [
                Trigger(TriggerScope.AfterQuestion, "{\"url\":\"https://example.test/passt\"}", questionId: questionId),
                Trigger(TriggerScope.AfterQuestion, "{\"url\":\"https://example.test/andere\"}", questionId: Guid.NewGuid()),
                Trigger(TriggerScope.AfterQuestion, "{\"url\":\"https://example.test/alle\"}"),
            ]));

        await handler.Handle(
            new QuestionAnsweredNotification(Guid.NewGuid(), "onboarding", questionId, null, IsCompleted: true),
            CancellationToken.None);

        Assert.Equal(2, spy.Requests.Count);
        Assert.Contains(spy.Requests, request => request.Url?.ToString() == "https://example.test/passt");
        Assert.Contains(spy.Requests, request => request.Url?.ToString() == "https://example.test/alle");
    }

    /// <summary>Unreadable or incomplete configuration is skipped, not thrown.</summary>
    [Theory]
    [InlineData("kein json")]
    [InlineData("{\"name\":\"ohne-url\"}")]
    public async Task Skips_a_trigger_with_an_unusable_configuration(string config)
    {
        var spy = new RecordingHttpMessageHandler();
        var handler = CreateHandler(
            spy, registrations: [], store: new StubDialogStore([Trigger(TriggerScope.OnDialogCompleted, config)]));

        var exception = await Record.ExceptionAsync(() =>
            handler.Handle(new DialogCompletedNotification(Guid.NewGuid(), "onboarding", []), CancellationToken.None).AsTask());

        Assert.Null(exception);
        Assert.Empty(spy.Requests);
    }

    /// <summary>A trigger's condition decides the delivery (the real expression engine).</summary>
    [Theory]
    [InlineData("role == \"dev\"", 1)]
    [InlineData("role == \"pm\"", 0)]
    public async Task Evaluates_the_condition_of_a_trigger(string expression, int expectedRequests)
    {
        var spy = new RecordingHttpMessageHandler();
        var store = StoreWithSession(
            [Trigger(TriggerScope.OnDialogCompleted, $"{{\"url\":\"{TargetUrl}\"}}", expression: expression)],
            out var sessionId);

        var handler = CreateHandler(
            spy, registrations: [], store: store, evaluator: new DynamicExpressoExpressionEvaluator());

        await handler.Handle(new DialogCompletedNotification(sessionId, "branching", []), CancellationToken.None);

        Assert.Equal(expectedRequests, spy.Requests.Count);
    }

    /// <summary>
    /// A condition that cannot be evaluated (an unknown identifier – e.g. an answer that does not yet
    /// exist at dialog start) skips the target instead of breaking the triggering command.
    /// </summary>
    [Fact]
    public async Task Skips_a_trigger_with_a_condition_that_cannot_be_evaluated()
    {
        var spy = new RecordingHttpMessageHandler();
        var store = StoreWithSession(
            [Trigger(TriggerScope.OnDialogCompleted, $"{{\"url\":\"{TargetUrl}\"}}", expression: "gibtEsNicht == 1")],
            out var sessionId);

        var handler = CreateHandler(
            spy, registrations: [], store: store, evaluator: new DynamicExpressoExpressionEvaluator());

        var exception = await Record.ExceptionAsync(() =>
            handler.Handle(new DialogCompletedNotification(sessionId, "branching", []), CancellationToken.None).AsTask());

        Assert.Null(exception);
        Assert.Empty(spy.Requests);
    }

    /// <summary>The constructor rejects every <see langword="null"/> argument.</summary>
    [Fact]
    public void Constructor_throws_on_null_arguments()
    {
        var factory = CreateHttpClientFactory(new RecordingHttpMessageHandler(), withResilience: false);
        IReadOnlyList<FlirtyWebhookRegistration> registrations = [];
        var evaluator = new UnusedExpressionEvaluator();
        var store = new StubDialogStore();
        var logger = NullLogger<WebhookNotificationHandler>.Instance;

        Assert.Throws<ArgumentNullException>(() => new WebhookNotificationHandler(null!, registrations, evaluator, store, logger));
        Assert.Throws<ArgumentNullException>(() => new WebhookNotificationHandler(factory, null!, evaluator, store, logger));
        Assert.Throws<ArgumentNullException>(() => new WebhookNotificationHandler(factory, registrations, null!, store, logger));
        Assert.Throws<ArgumentNullException>(() => new WebhookNotificationHandler(factory, registrations, evaluator, null!, logger));
        Assert.Throws<ArgumentNullException>(() => new WebhookNotificationHandler(factory, registrations, evaluator, store, null!));
    }

    private static WebhookNotificationHandler CreateHandler(
        RecordingHttpMessageHandler spy,
        IReadOnlyList<FlirtyWebhookRegistration> registrations,
        bool withResilience = false,
        StubDialogStore? store = null,
        IExpressionEvaluator? evaluator = null)
        => new(
            CreateHttpClientFactory(spy, withResilience),
            registrations,
            evaluator ?? new UnusedExpressionEvaluator(),
            store ?? new StubDialogStore(),
            NullLogger<WebhookNotificationHandler>.Instance);

    /// <summary>Builds a trigger definition for the store stub.</summary>
    private static TriggerDefinition Trigger(
        TriggerScope scope,
        string config,
        TriggerKind kind = TriggerKind.Webhook,
        Guid? questionId = null,
        string? expression = null)
        => new()
        {
            Id = Guid.NewGuid(),
            DialogId = Guid.NewGuid(),
            Scope = scope,
            QuestionId = questionId,
            Kind = kind,
            Config = config,
            Expression = expression,
        };

    /// <summary>
    /// Builds a store stub with a real dialog incl. a session and one answer to <c>role</c>
    /// (<c>"dev"</c>) – the basis for the condition tests against the real expression engine.
    /// </summary>
    private static StubDialogStore StoreWithSession(IReadOnlyList<TriggerDefinition> triggers, out Guid sessionId)
    {
        var dialog = TestDialogFactory.BuildBranchingDialog(Guid.NewGuid(), out var ids);
        sessionId = Guid.NewGuid();

        var session = new DialogSession
        {
            Id = sessionId,
            DialogId = dialog.Id,
            DialogVersion = dialog.Version,
            ExternalUserKey = "kunde-1",
            Status = SessionStatus.Completed,
            StartedAt = TestDialogFactory.SampleTime,
            Answers =
            {
                new SessionAnswer
                {
                    Id = Guid.NewGuid(), SessionId = sessionId, QuestionId = ids.RoleQuestionId,
                    Value = "\"dev\"", AnsweredAt = TestDialogFactory.SampleTime, Sequence = 0,
                },
            },
        };

        return new StubDialogStore(triggers, dialog, session);
    }

    private static IHttpClientFactory CreateHttpClientFactory(HttpMessageHandler primary, bool withResilience)
    {
        var services = new ServiceCollection();
        var builder = services
            .AddHttpClient(WebhookNotificationHandler.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => primary);

        if (withResilience)
        {
            // Deterministic: a zero delay makes the exponential backoff immediate (0 * 2^n = 0).
            builder.AddStandardResilienceHandler(options => options.Retry.Delay = TimeSpan.Zero);
        }

        return services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();
    }

    /// <summary>Test double for <see cref="IExpressionEvaluator"/>; never called in tests without an expression.</summary>
    private sealed class UnusedExpressionEvaluator : IExpressionEvaluator
    {
        public bool Evaluate(string expression, ExpressionContext context) => throw new NotSupportedException();

        public ExpressionValidationResult Validate(string expression, ExpressionContext context)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Test double for <see cref="IDialogStore"/>: returns the configured triggers (filtered by scope
    /// – like the real query) plus optionally a session and a dialog for building the context.
    /// Everything else throws: the webhook handler must not touch it.
    /// </summary>
    private sealed class StubDialogStore : IDialogStore
    {
        private readonly IReadOnlyList<TriggerDefinition> _triggers;
        private readonly Dialog? _dialog;
        private readonly DialogSession? _session;

        public StubDialogStore(
            IReadOnlyList<TriggerDefinition>? triggers = null, Dialog? dialog = null, DialogSession? session = null)
        {
            _triggers = triggers ?? [];
            _dialog = dialog;
            _session = session;
        }

        public Task<IReadOnlyList<TriggerDefinition>> GetTriggersForSessionAsync(
            Guid sessionId, TriggerScope scope, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TriggerDefinition>>(
                [.. _triggers.Where(trigger => trigger.Scope == scope)]);

        public Task<Dialog?> GetDialogAsync(Guid dialogId, CancellationToken cancellationToken = default)
            => Task.FromResult(_dialog);

        public Task<DialogSession?> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
            => Task.FromResult(_session);

        public Task<Dialog?> GetPublishedDialogAsync(string key, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<DialogSession?> FindActiveSessionAsync(
            Guid dialogId, string externalUserKey, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public void AddSession(DialogSession session) => throw new NotSupportedException();

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
