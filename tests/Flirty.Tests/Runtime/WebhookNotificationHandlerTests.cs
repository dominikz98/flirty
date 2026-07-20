using System.Net;
using Flirty.Domain;
using Flirty.Expressions;
using Flirty.Persistence;
using Flirty.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Flirty.Tests.Runtime;

/// <summary>
/// Prüft den eingebauten <see cref="WebhookNotificationHandler"/> (#33) isoliert: Scope-Filterung,
/// HTTP-Zustellung (Methode/URL/Header/Body), Best-effort-Fehlerbehandlung und das Wiederholen bei
/// transienten Fehlern über die Standard-Resilience-Pipeline. Es wird bewusst keine Mock-Bibliothek
/// genutzt, sondern ein handgeschriebener <see cref="RecordingHttpMessageHandler"/> als
/// <c>HttpMessageHandler</c>-Spy.
/// </summary>
public sealed class WebhookNotificationHandlerTests
{
    private const string TargetUrl = "https://example.test/hook";

    /// <summary>Ein passender Scope liefert genau einen POST mit Event-Header und JSON-Body.</summary>
    [Fact]
    public async Task Liefert_bei_passendem_Scope_einen_POST_mit_Event_Header_und_Body()
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

    /// <summary>Notifications ohne passende Scope-Registrierung lösen keine Zustellung aus.</summary>
    [Fact]
    public async Task Liefert_nichts_wenn_kein_Scope_passt()
    {
        var spy = new RecordingHttpMessageHandler();
        var handler = CreateHandler(
            spy, [new FlirtyWebhookRegistration("OnDialogCompleted", TargetUrl, TriggerScope.OnDialogCompleted)]);

        var notification = new AnswerSubmittedNotification(Guid.NewGuid(), "onboarding", Guid.NewGuid(), "\"x\"", null, null);
        await handler.Handle(notification, CancellationToken.None);

        Assert.Empty(spy.Requests);
    }

    /// <summary>Registrierungen ohne <c>Scope</c> (alter String-Overload) werden nicht ausgeliefert.</summary>
    [Fact]
    public async Task Ignoriert_Registrierungen_ohne_Scope()
    {
        var spy = new RecordingHttpMessageHandler();
        var handler = CreateHandler(spy, [new FlirtyWebhookRegistration("order-created", TargetUrl)]);

        await handler.Handle(new DialogCompletedNotification(Guid.NewGuid(), "onboarding", []), CancellationToken.None);

        Assert.Empty(spy.Requests);
    }

    /// <summary>Mehrere Registrierungen desselben Scopes werden alle bedient.</summary>
    [Fact]
    public async Task Liefert_an_mehrere_Ziele_desselben_Scopes()
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

    /// <summary>Ein Fehlerstatus (nach erschöpften Retries) wird geloggt, aber nicht geworfen.</summary>
    [Fact]
    public async Task Fehlerstatus_wird_geschluckt()
    {
        var spy = new RecordingHttpMessageHandler(HttpStatusCode.InternalServerError);
        var handler = CreateHandler(
            spy, [new FlirtyWebhookRegistration("OnDialogCompleted", TargetUrl, TriggerScope.OnDialogCompleted)]);

        // Kein Wurf: der auslösende Command darf nicht scheitern.
        await handler.Handle(new DialogCompletedNotification(Guid.NewGuid(), "onboarding", []), CancellationToken.None);

        Assert.Single(spy.Requests);
    }

    /// <summary>Eine Transport-Ausnahme wird geloggt, aber nicht geworfen.</summary>
    [Fact]
    public async Task Ausnahme_wird_geschluckt()
    {
        var spy = RecordingHttpMessageHandler.Throwing();
        var handler = CreateHandler(
            spy, [new FlirtyWebhookRegistration("OnDialogCompleted", TargetUrl, TriggerScope.OnDialogCompleted)]);

        var exception = await Record.ExceptionAsync(() =>
            handler.Handle(new DialogCompletedNotification(Guid.NewGuid(), "onboarding", []), CancellationToken.None).AsTask());

        Assert.Null(exception);
    }

    /// <summary>Ein transienter Fehler (503) wird über die Resilience-Pipeline wiederholt.</summary>
    [Fact]
    public async Task Wiederholt_bei_transientem_Fehler()
    {
        var spy = new RecordingHttpMessageHandler(HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);
        var handler = CreateHandler(
            spy,
            [new FlirtyWebhookRegistration("OnDialogCompleted", TargetUrl, TriggerScope.OnDialogCompleted)],
            withResilience: true);

        await handler.Handle(new DialogCompletedNotification(Guid.NewGuid(), "onboarding", []), CancellationToken.None);

        // Erster Versuch 503 -> Retry -> zweiter Versuch 200: genau zwei Zustellversuche.
        Assert.Equal(2, spy.Requests.Count);
    }

    /// <summary>Der Konstruktor lehnt jedes <see langword="null"/>-Argument ab.</summary>
    [Fact]
    public void Konstruktor_wirft_bei_null_Argumenten()
    {
        var factory = CreateHttpClientFactory(new RecordingHttpMessageHandler(), withResilience: false);
        IReadOnlyList<FlirtyWebhookRegistration> registrations = [];
        var evaluator = new UnusedExpressionEvaluator();
        var store = new UnusedDialogStore();
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
        bool withResilience = false)
        => new(
            CreateHttpClientFactory(spy, withResilience),
            registrations,
            new UnusedExpressionEvaluator(),
            new UnusedDialogStore(),
            NullLogger<WebhookNotificationHandler>.Instance);

    private static IHttpClientFactory CreateHttpClientFactory(HttpMessageHandler primary, bool withResilience)
    {
        var services = new ServiceCollection();
        var builder = services
            .AddHttpClient(WebhookNotificationHandler.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => primary);

        if (withResilience)
        {
            // Deterministisch: Zero-Delay macht das exponentielle Backoff sofort (0 * 2^n = 0).
            builder.AddStandardResilienceHandler(options => options.Retry.Delay = TimeSpan.Zero);
        }

        return services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();
    }

    /// <summary>Test-Doppel für <see cref="IExpressionEvaluator"/>; wird bei ausdruckslosen Tests nie aufgerufen.</summary>
    private sealed class UnusedExpressionEvaluator : IExpressionEvaluator
    {
        public bool Evaluate(string expression, ExpressionContext context) => throw new NotSupportedException();

        public ExpressionValidationResult Validate(string expression, ExpressionContext context)
            => throw new NotSupportedException();
    }

    /// <summary>Test-Doppel für <see cref="IDialogStore"/>; wird bei ausdruckslosen Tests nie aufgerufen.</summary>
    private sealed class UnusedDialogStore : IDialogStore
    {
        public Task<Dialog?> GetPublishedDialogAsync(string key, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Dialog?> GetDialogAsync(Guid dialogId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<DialogSession?> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<DialogSession?> FindActiveSessionAsync(
            Guid dialogId, string externalUserKey, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public void AddSession(DialogSession session) => throw new NotSupportedException();

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
