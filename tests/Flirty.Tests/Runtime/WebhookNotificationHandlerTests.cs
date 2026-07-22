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
/// Prüft den eingebauten <see cref="WebhookNotificationHandler"/> (#33, seit #42 auch
/// <see cref="TriggerDefinition"/>-getrieben) isoliert: Scope-Filterung, HTTP-Zustellung
/// (Methode/URL/Header/Body), die am Dialog konfigurierten Trigger samt Bedingung sowie
/// Best-effort-Fehlerbehandlung und das Wiederholen bei transienten Fehlern über die
/// Standard-Resilience-Pipeline. Es wird bewusst keine Mock-Bibliothek genutzt, sondern ein
/// handgeschriebener <see cref="RecordingHttpMessageHandler"/> als <c>HttpMessageHandler</c>-Spy.
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

    /// <summary>Ein am Dialog konfigurierter Webhook-Trigger (#42) wird ausgeliefert – mit Ereignisname im Header.</summary>
    [Fact]
    public async Task Liefert_am_Dialog_konfigurierte_Webhook_Trigger_aus()
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

    /// <summary>In-Process-Trigger sind Marker für die Host-App und werden nicht zugestellt.</summary>
    [Fact]
    public async Task Ignoriert_InProcess_Trigger()
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

    /// <summary>Bei <c>AfterQuestion</c> zählt der Frage-Bezug: nur der Trigger der beantworteten Frage feuert.</summary>
    [Fact]
    public async Task Filtert_AfterQuestion_Trigger_auf_die_Frage()
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

    /// <summary>Unlesbare oder unvollständige Konfiguration wird übersprungen, nicht geworfen.</summary>
    [Theory]
    [InlineData("kein json")]
    [InlineData("{\"name\":\"ohne-url\"}")]
    public async Task Ueberspringt_Trigger_mit_unbrauchbarer_Konfiguration(string config)
    {
        var spy = new RecordingHttpMessageHandler();
        var handler = CreateHandler(
            spy, registrations: [], store: new StubDialogStore([Trigger(TriggerScope.OnDialogCompleted, config)]));

        var exception = await Record.ExceptionAsync(() =>
            handler.Handle(new DialogCompletedNotification(Guid.NewGuid(), "onboarding", []), CancellationToken.None).AsTask());

        Assert.Null(exception);
        Assert.Empty(spy.Requests);
    }

    /// <summary>Die Bedingung eines Triggers entscheidet über die Zustellung (echte Ausdrucks-Engine).</summary>
    [Theory]
    [InlineData("role == \"dev\"", 1)]
    [InlineData("role == \"pm\"", 0)]
    public async Task Wertet_die_Bedingung_eines_Triggers_aus(string expression, int expectedRequests)
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
    /// Eine nicht auswertbare Bedingung (unbekannter Bezeichner – z. B. eine Antwort, die es beim
    /// Dialogstart noch nicht gibt) überspringt das Ziel, statt den auslösenden Command zu brechen.
    /// </summary>
    [Fact]
    public async Task Ueberspringt_Trigger_mit_nicht_auswertbarer_Bedingung()
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

    /// <summary>Der Konstruktor lehnt jedes <see langword="null"/>-Argument ab.</summary>
    [Fact]
    public void Konstruktor_wirft_bei_null_Argumenten()
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

    /// <summary>Baut eine Trigger-Definition für die Store-Attrappe.</summary>
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
    /// Baut eine Store-Attrappe mit einem echten Dialog samt Session und einer Antwort auf <c>role</c>
    /// (<c>"dev"</c>) – Grundlage für die Bedingungs-Tests gegen die echte Ausdrucks-Engine.
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

    /// <summary>
    /// Test-Doppel für <see cref="IDialogStore"/>: liefert die konfigurierten Trigger (nach Scope
    /// gefiltert – wie die echte Abfrage) sowie optional Session und Dialog für die Kontext-Bildung.
    /// Alles Übrige wirft: der Webhook-Handler darf es nicht anfassen.
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
