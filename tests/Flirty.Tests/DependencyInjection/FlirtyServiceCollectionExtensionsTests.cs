using Flirty.Domain;
using Flirty.Expressions;
using Flirty.Persistence;
using Flirty.Runtime;
using Flirty.Tests.Persistence;
using Flirty.Tests.Runtime;
using Mediator;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Flirty.Tests.DependencyInjection;

/// <summary>
/// Verifiziert den Options-Ausbau von <c>AddFlirty(Action&lt;FlirtyOptions&gt;)</c> aus Issue #34:
/// Provider-Wahl (<c>UseSqlite</c>/<c>UsePostgreSql</c>/<c>UseSqlServer</c>) inkl. automatischer
/// <see cref="FlirtyDbContext"/>-Registrierung mit korrekter <c>MigrationsAssembly</c>, austauschbarer
/// <see cref="IExpressionEvaluator"/> (<c>UseExpressionEvaluator&lt;T&gt;()</c>) und die Webhook-Stub-
/// Registrierung (<c>AddWebhook</c>). Enthält zusätzlich ein reines Console-Setup ohne ASP.NET, das
/// einen Dialog end-to-end über die Facade <see cref="IFlirtyEngine"/> durchspielt.
/// </summary>
public sealed class FlirtyServiceCollectionExtensionsTests
{
    /// <summary><c>UseSqlite</c> registriert einen auflösbaren, mit SQLite konfigurierten <see cref="FlirtyDbContext"/>.</summary>
    [Fact]
    public void UseSqlite_registriert_FlirtyDbContext_mit_Sqlite_Provider_und_MigrationsAssembly()
    {
        using var provider = BuildProvider(options => options.UseSqlite("Data Source=:memory:"));
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FlirtyDbContext>();

        Assert.Equal("Microsoft.EntityFrameworkCore.Sqlite", context.Database.ProviderName);
        Assert.Contains(
            context.Database.GetMigrations(),
            migration => migration.EndsWith("InitialCreate", StringComparison.Ordinal));
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IDialogStore>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IFlirtyEngine>());
    }

    /// <summary><c>UsePostgreSql</c> wählt den Npgsql-Provider und die PostgreSQL-Migrations-Assembly.</summary>
    [Fact]
    public void UsePostgreSql_waehlt_den_Npgsql_Provider_und_MigrationsAssembly()
    {
        using var provider = BuildProvider(
            options => options.UsePostgreSql("Host=localhost;Database=flirty;Username=u;Password=p"));
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FlirtyDbContext>();

        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", context.Database.ProviderName);
        Assert.Contains(
            context.Database.GetMigrations(),
            migration => migration.EndsWith("InitialCreate", StringComparison.Ordinal));
    }

    /// <summary><c>UseSqlServer</c> wählt den SQL-Server-Provider und die SQL-Server-Migrations-Assembly.</summary>
    [Fact]
    public void UseSqlServer_waehlt_den_SqlServer_Provider_und_MigrationsAssembly()
    {
        using var provider = BuildProvider(
            options => options.UseSqlServer("Server=localhost;Database=flirty;Trusted_Connection=True;"));
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FlirtyDbContext>();

        Assert.Equal("Microsoft.EntityFrameworkCore.SqlServer", context.Database.ProviderName);
        Assert.Contains(
            context.Database.GetMigrations(),
            migration => migration.EndsWith("InitialCreate", StringComparison.Ordinal));
    }

    /// <summary><c>UseExpressionEvaluator&lt;T&gt;()</c> ersetzt den Default-Evaluator durch die eigene Implementierung.</summary>
    [Fact]
    public void UseExpressionEvaluator_ersetzt_den_Default()
    {
        using var provider = BuildProvider(options => options.UseExpressionEvaluator<FakeExpressionEvaluator>());

        var evaluator = provider.GetRequiredService<IExpressionEvaluator>();

        Assert.IsType<FakeExpressionEvaluator>(evaluator);
    }

    /// <summary>Ohne <c>UseExpressionEvaluator</c> bleibt der Default <c>DynamicExpressoExpressionEvaluator</c> registriert.</summary>
    [Fact]
    public void Ohne_UseExpressionEvaluator_bleibt_der_Default()
    {
        using var provider = BuildProvider(_ => { });

        var evaluator = provider.GetRequiredService<IExpressionEvaluator>();

        Assert.IsType<DynamicExpressoExpressionEvaluator>(evaluator);
    }

    /// <summary><c>AddWebhook</c> stellt die gesammelten Registrierungen als <see cref="IReadOnlyList{T}"/> bereit.</summary>
    [Fact]
    public void AddWebhook_stellt_die_Registrierungen_bereit()
    {
        using var provider = BuildProvider(options => options
            .AddWebhook("order-created", "https://example.test/order")
            .AddWebhook("dialog-completed", "https://example.test/done"));

        var webhooks = provider.GetRequiredService<IReadOnlyList<FlirtyWebhookRegistration>>();

        Assert.Equal(2, webhooks.Count);
        Assert.Contains(webhooks, hook => hook == new FlirtyWebhookRegistration("order-created", "https://example.test/order"));
        Assert.Contains(webhooks, hook => hook == new FlirtyWebhookRegistration("dialog-completed", "https://example.test/done"));
    }

    /// <summary>Ohne <c>AddWebhook</c> ist die Webhook-Liste auflösbar und leer.</summary>
    [Fact]
    public void Ohne_AddWebhook_ist_die_Webhook_Liste_leer()
    {
        using var provider = BuildProvider(_ => { });

        var webhooks = provider.GetRequiredService<IReadOnlyList<FlirtyWebhookRegistration>>();

        Assert.Empty(webhooks);
    }

    /// <summary>
    /// Reines Console-Setup ohne ASP.NET: <c>AddLogging().AddFlirty(o =&gt; o.UseSqlite(...))</c> verdrahtet den
    /// gesamten Stack; ein veröffentlichter Dialog lässt sich über die Facade <see cref="IFlirtyEngine"/>
    /// starten und beantworten (Branching liefert die nächste Frage).
    /// </summary>
    [Fact]
    public async Task Console_Setup_ohne_AspNet_spielt_Dialog_ueber_die_Facade_durch()
    {
        // Shared-Cache-in-memory: solange die keep-alive-Verbindung offen ist, teilen sich alle
        // DI-erzeugten FlirtyDbContext-Instanzen dieselbe in-memory-Datenbank.
        const string connectionString = "Data Source=FlirtyDiConsoleTest;Mode=Memory;Cache=Shared";
        using var keepAlive = new SqliteConnection(connectionString);
        keepAlive.Open();

        using var provider = new ServiceCollection()
            .AddLogging()
            .AddFlirty(options => options.UseSqlite(connectionString))
            .BuildServiceProvider();

        var dialogId = Guid.NewGuid();
        BranchingDialogIds ids;
        using (var seedScope = provider.CreateScope())
        {
            var context = seedScope.ServiceProvider.GetRequiredService<FlirtyDbContext>();
            context.Database.EnsureCreated();
            context.Dialogs.Add(TestDialogFactory.BuildBranchingDialog(dialogId, out ids));
            context.SaveChanges();
        }

        using var scope = provider.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<IFlirtyEngine>();

        var start = await engine.StartDialogAsync("branching", "user-1");
        var next = await engine.SubmitAnswerAsync(start.SessionId, start.CurrentQuestion.Id, "\"dev\"");

        Assert.False(next.IsCompleted);
        Assert.NotNull(next.NextQuestion);
        Assert.Equal(ids.DevQuestionId, next.NextQuestion.Id);
    }

    /// <summary><c>AddFlirtyHandler</c> registriert den Handler auflösbar als <see cref="ServiceLifetime.Scoped"/> (Default).</summary>
    [Fact]
    public void AddFlirtyHandler_registriert_Handler_als_Scoped_Default()
    {
        var services = new ServiceCollection();

        services.AddFlirtyHandler<DialogCompletedNotification, NoopNotificationHandler>();

        var descriptor = Assert.Single(services);
        Assert.Equal(typeof(INotificationHandler<DialogCompletedNotification>), descriptor.ServiceType);
        Assert.Equal(typeof(NoopNotificationHandler), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    /// <summary>Mehrere Handler je Notification bleiben erhalten (Beleg gegen <c>TryAdd</c>/<c>Replace</c>) und sind alle auflösbar.</summary>
    [Fact]
    public void AddFlirtyHandler_erlaubt_mehrere_Handler_je_Notification()
    {
        using var provider = new ServiceCollection()
            .AddFlirtyHandler<DialogCompletedNotification, NoopNotificationHandler>()
            .AddFlirtyHandler<DialogCompletedNotification, OtherNoopNotificationHandler>()
            .BuildServiceProvider();

        var handlers = provider.GetServices<INotificationHandler<DialogCompletedNotification>>().ToList();

        Assert.Equal(2, handlers.Count);
        Assert.Contains(handlers, handler => handler is NoopNotificationHandler);
        Assert.Contains(handlers, handler => handler is OtherNoopNotificationHandler);
    }

    /// <summary>Die Lebensdauer lässt sich über den Parameter überschreiben (z. B. <see cref="ServiceLifetime.Singleton"/>).</summary>
    [Fact]
    public void AddFlirtyHandler_uebernimmt_die_gewaehlte_Lifetime()
    {
        var services = new ServiceCollection();

        services.AddFlirtyHandler<DialogCompletedNotification, NoopNotificationHandler>(ServiceLifetime.Singleton);

        var descriptor = Assert.Single(services);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    /// <summary>Der <see cref="TriggerScope"/>-Overload von <c>AddWebhook</c> speichert Scope und Ausdruck (#33).</summary>
    [Fact]
    public void AddWebhook_mit_Scope_speichert_Scope_und_Expression()
    {
        using var provider = BuildProvider(options => options
            .AddWebhook(TriggerScope.OnDialogCompleted, "https://example.test/done", "role == \"dev\""));

        var webhook = Assert.Single(provider.GetRequiredService<IReadOnlyList<FlirtyWebhookRegistration>>());
        Assert.Equal(TriggerScope.OnDialogCompleted, webhook.Scope);
        Assert.Equal("https://example.test/done", webhook.Url);
        Assert.Equal("role == \"dev\"", webhook.Expression);
        Assert.Equal("OnDialogCompleted", webhook.EventName);
    }

    /// <summary>
    /// Der eingebaute <see cref="WebhookNotificationHandler"/> wird vom Mediator-Source-Generator automatisch
    /// registriert, und der resiliente Named-<c>HttpClient</c> steht bereit (beides seit #33 Teil von
    /// <c>AddFlirty()</c>).
    /// </summary>
    [Fact]
    public void WebhookNotificationHandler_und_HttpClientFactory_werden_registriert()
    {
        var services = new ServiceCollection();

        services.AddFlirty();

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(WebhookNotificationHandler));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IHttpClientFactory));
    }

    /// <summary>
    /// Ende-zu-Ende (Dispatch + Webhook): ein via <c>o.AddWebhook(OnDialogCompleted, url)</c> registriertes Ziel
    /// erhält beim Dialog-Abschluss genau einen POST mit Event-Header und JSON-Body – ausgelöst durch die
    /// Notification-Publikation der Engine.
    /// </summary>
    [Fact]
    public async Task Webhook_wird_bei_Dialog_Abschluss_ausgeliefert()
    {
        var (provider, spy, keepAlive) = BuildWebhookProvider(
            options => options.AddWebhook(TriggerScope.OnDialogCompleted, "https://example.test/done"));

        using (keepAlive)
        using (provider)
        {
            await RunBranchingToCompletionAsync(provider);

            var request = Assert.Single(spy.Requests);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://example.test/done", request.Url?.ToString());
            Assert.Equal("OnDialogCompleted", request.Event);
            Assert.Contains("branching", request.Body);
        }
    }

    /// <summary>Ein zutreffender Bedingungsausdruck (<c>role == "dev"</c>) liefert den Webhook aus.</summary>
    [Fact]
    public async Task Webhook_mit_zutreffender_Bedingung_wird_ausgeliefert()
    {
        var (provider, spy, keepAlive) = BuildWebhookProvider(
            options => options.AddWebhook(TriggerScope.OnDialogCompleted, "https://example.test/done", "role == \"dev\""));

        using (keepAlive)
        using (provider)
        {
            await RunBranchingToCompletionAsync(provider);

            Assert.Single(spy.Requests);
        }
    }

    /// <summary>Ein nicht zutreffender Bedingungsausdruck (<c>role == "pm"</c>) unterdrückt die Auslieferung.</summary>
    [Fact]
    public async Task Webhook_mit_nicht_zutreffender_Bedingung_wird_nicht_ausgeliefert()
    {
        var (provider, spy, keepAlive) = BuildWebhookProvider(
            options => options.AddWebhook(TriggerScope.OnDialogCompleted, "https://example.test/done", "role == \"pm\""));

        using (keepAlive)
        using (provider)
        {
            await RunBranchingToCompletionAsync(provider);

            Assert.Empty(spy.Requests);
        }
    }

    private static ServiceProvider BuildProvider(Action<FlirtyOptions> configure)
        => new ServiceCollection()
            .AddLogging()
            .AddFlirty(configure)
            .BuildServiceProvider();

    /// <summary>
    /// Baut einen echten DI-Container mit SQLite in-memory (shared cache), den über <paramref name="configureWebhooks"/>
    /// gesetzten Webhooks und einem in den Webhook-Named-Client eingeschleusten <see cref="RecordingHttpMessageHandler"/>.
    /// </summary>
    private static (ServiceProvider Provider, RecordingHttpMessageHandler Spy, SqliteConnection KeepAlive) BuildWebhookProvider(
        Action<FlirtyOptions> configureWebhooks)
    {
        var connectionString = $"Data Source=FlirtyWebhookTest-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        var keepAlive = new SqliteConnection(connectionString);
        keepAlive.Open();

        var spy = new RecordingHttpMessageHandler();

        var provider = new ServiceCollection()
            .AddLogging()
            .AddFlirty(options =>
            {
                options.UseSqlite(connectionString);
                configureWebhooks(options);
            })
            // Den Primary-Handler des Webhook-Clients durch den Spy ersetzen (nach AddFlirty; additive Config).
            .AddHttpClient(WebhookNotificationHandler.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => spy)
            .Services
            .BuildServiceProvider();

        return (provider, spy, keepAlive);
    }

    private static async Task RunBranchingToCompletionAsync(ServiceProvider provider)
    {
        var dialogId = Guid.NewGuid();
        using (var seedScope = provider.CreateScope())
        {
            var context = seedScope.ServiceProvider.GetRequiredService<FlirtyDbContext>();
            context.Database.EnsureCreated();
            context.Dialogs.Add(TestDialogFactory.BuildBranchingDialog(dialogId, out _));
            context.SaveChanges();
        }

        using var scope = provider.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<IFlirtyEngine>();

        var start = await engine.StartDialogAsync("branching", "user-1");
        var afterRole = await engine.SubmitAnswerAsync(start.SessionId, start.CurrentQuestion.Id, "\"dev\"");
        await engine.SubmitAnswerAsync(start.SessionId, afterRole.NextQuestion!.Id, "\"C#\"");
    }

    /// <summary>Test-Doppel für <see cref="IExpressionEvaluator"/>; wird nur zur Prüfung der DI-Ersetzung aufgelöst.</summary>
    private sealed class FakeExpressionEvaluator : IExpressionEvaluator
    {
        public bool Evaluate(string expression, ExpressionContext context) => throw new NotSupportedException();

        public ExpressionValidationResult Validate(string expression, ExpressionContext context)
            => throw new NotSupportedException();
    }

    /// <summary>Test-Doppel-Handler; belegt nur die DI-Registrierung, nichts weiter.</summary>
    private sealed class NoopNotificationHandler : INotificationHandler<DialogCompletedNotification>
    {
        public ValueTask Handle(DialogCompletedNotification notification, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;
    }

    /// <summary>Zweiter Test-Doppel-Handler für die Mehrfach-Registrierung.</summary>
    private sealed class OtherNoopNotificationHandler : INotificationHandler<DialogCompletedNotification>
    {
        public ValueTask Handle(DialogCompletedNotification notification, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;
    }
}
