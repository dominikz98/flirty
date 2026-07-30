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
/// Verifies the options build-out of <c>AddFlirty(Action&lt;FlirtyOptions&gt;)</c> from issue #34:
/// provider choice (<c>UseSqlite</c>/<c>UsePostgreSql</c>/<c>UseSqlServer</c>) incl. automatic
/// <see cref="FlirtyDbContext"/> registration with the correct <c>MigrationsAssembly</c>, a
/// swappable <see cref="IExpressionEvaluator"/> (<c>UseExpressionEvaluator&lt;T&gt;()</c>) and the
/// webhook stub registration (<c>AddWebhook</c>). Also contains a pure console setup without
/// ASP.NET that plays a dialog through end-to-end via the facade <see cref="IFlirtyEngine"/>.
/// </summary>
public sealed class FlirtyServiceCollectionExtensionsTests
{
    /// <summary><c>UseSqlite</c> registers a resolvable <see cref="FlirtyDbContext"/> configured with SQLite.</summary>
    [Fact]
    public void UseSqlite_registers_FlirtyDbContext_with_the_Sqlite_provider_and_migrations_assembly()
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

    /// <summary><c>UsePostgreSql</c> picks the Npgsql provider and the PostgreSQL migrations assembly.</summary>
    [Fact]
    public void UsePostgreSql_picks_the_Npgsql_provider_and_migrations_assembly()
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

    /// <summary><c>UseSqlServer</c> picks the SQL Server provider and the SQL Server migrations assembly.</summary>
    [Fact]
    public void UseSqlServer_picks_the_SqlServer_provider_and_migrations_assembly()
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

    /// <summary><c>UseExpressionEvaluator&lt;T&gt;()</c> replaces the default evaluator with the host's own implementation.</summary>
    [Fact]
    public void UseExpressionEvaluator_replaces_the_default()
    {
        using var provider = BuildProvider(options => options.UseExpressionEvaluator<FakeExpressionEvaluator>());

        var evaluator = provider.GetRequiredService<IExpressionEvaluator>();

        Assert.IsType<FakeExpressionEvaluator>(evaluator);
    }

    /// <summary>Without <c>UseExpressionEvaluator</c> the default <c>DynamicExpressoExpressionEvaluator</c> stays registered.</summary>
    [Fact]
    public void Without_UseExpressionEvaluator_the_default_stays()
    {
        using var provider = BuildProvider(_ => { });

        var evaluator = provider.GetRequiredService<IExpressionEvaluator>();

        Assert.IsType<DynamicExpressoExpressionEvaluator>(evaluator);
    }

    /// <summary><c>AddWebhook</c> exposes the collected registrations as an <see cref="IReadOnlyList{T}"/>.</summary>
    [Fact]
    public void AddWebhook_exposes_the_registrations()
    {
        using var provider = BuildProvider(options => options
            .AddWebhook("order-created", "https://example.test/order")
            .AddWebhook("dialog-completed", "https://example.test/done"));

        var webhooks = provider.GetRequiredService<IReadOnlyList<FlirtyWebhookRegistration>>();

        Assert.Equal(2, webhooks.Count);
        Assert.Contains(webhooks, hook => hook == new FlirtyWebhookRegistration("order-created", "https://example.test/order"));
        Assert.Contains(webhooks, hook => hook == new FlirtyWebhookRegistration("dialog-completed", "https://example.test/done"));
    }

    /// <summary>Without <c>AddWebhook</c> the webhook list is resolvable and empty.</summary>
    [Fact]
    public void Without_AddWebhook_the_webhook_list_is_empty()
    {
        using var provider = BuildProvider(_ => { });

        var webhooks = provider.GetRequiredService<IReadOnlyList<FlirtyWebhookRegistration>>();

        Assert.Empty(webhooks);
    }

    /// <summary>
    /// A pure console setup without ASP.NET: <c>AddLogging().AddFlirty(o =&gt; o.UseSqlite(...))</c> wires
    /// up the whole stack; a published dialog can be started and answered via the facade
    /// <see cref="IFlirtyEngine"/> (branching returns the next question).
    /// </summary>
    [Fact]
    public async Task Console_setup_without_AspNet_plays_the_dialog_through_the_facade()
    {
        // Shared-cache in-memory: as long as the keep-alive connection stays open, all
        // DI-created FlirtyDbContext instances share the same in-memory database.
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

    /// <summary><c>AddFlirtyHandler</c> registers the handler resolvably as <see cref="ServiceLifetime.Scoped"/> (the default).</summary>
    [Fact]
    public void AddFlirtyHandler_registers_the_handler_as_scoped_by_default()
    {
        var services = new ServiceCollection();

        services.AddFlirtyHandler<DialogCompletedNotification, NoopNotificationHandler>();

        var descriptor = Assert.Single(services);
        Assert.Equal(typeof(INotificationHandler<DialogCompletedNotification>), descriptor.ServiceType);
        Assert.Equal(typeof(NoopNotificationHandler), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    /// <summary>Several handlers per notification are preserved (proof against <c>TryAdd</c>/<c>Replace</c>) and all resolvable.</summary>
    [Fact]
    public void AddFlirtyHandler_allows_several_handlers_per_notification()
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

    /// <summary>The lifetime can be overridden via the parameter (e.g. <see cref="ServiceLifetime.Singleton"/>).</summary>
    [Fact]
    public void AddFlirtyHandler_takes_over_the_chosen_lifetime()
    {
        var services = new ServiceCollection();

        services.AddFlirtyHandler<DialogCompletedNotification, NoopNotificationHandler>(ServiceLifetime.Singleton);

        var descriptor = Assert.Single(services);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    /// <summary>The <see cref="TriggerScope"/> overload of <c>AddWebhook</c> stores scope and expression (#33).</summary>
    [Fact]
    public void AddWebhook_with_a_scope_stores_the_scope_and_the_expression()
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
    /// The built-in <see cref="WebhookNotificationHandler"/> is registered automatically by the mediator
    /// source generator, and the resilient named <c>HttpClient</c> is available (both part of
    /// <c>AddFlirty()</c> since #33).
    /// </summary>
    [Fact]
    public void WebhookNotificationHandler_and_HttpClientFactory_are_registered()
    {
        var services = new ServiceCollection();

        services.AddFlirty();

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(WebhookNotificationHandler));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IHttpClientFactory));
    }

    /// <summary>
    /// End-to-end (dispatch + webhook): a target registered via
    /// <c>o.AddWebhook(OnDialogCompleted, url)</c> receives exactly one POST with the event header and a
    /// JSON body when the dialog completes – triggered by the engine's notification publication.
    /// </summary>
    [Fact]
    public async Task Webhook_is_delivered_on_dialog_completion()
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

    /// <summary>A matching condition expression (<c>role == "dev"</c>) delivers the webhook.</summary>
    [Fact]
    public async Task Webhook_with_a_matching_condition_is_delivered()
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

    /// <summary>A non-matching condition expression (<c>role == "pm"</c>) suppresses the delivery.</summary>
    [Fact]
    public async Task Webhook_with_a_non_matching_condition_is_not_delivered()
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
    /// Builds a real DI container with SQLite in-memory (shared cache), the webhooks set via
    /// <paramref name="configureWebhooks"/> and a <see cref="RecordingHttpMessageHandler"/> injected into
    /// the webhook named client.
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
            // Replace the webhook client's primary handler with the spy (after AddFlirty; additive config).
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

    /// <summary>Test double for <see cref="IExpressionEvaluator"/>; resolved only to check the DI replacement.</summary>
    private sealed class FakeExpressionEvaluator : IExpressionEvaluator
    {
        public bool Evaluate(string expression, ExpressionContext context) => throw new NotSupportedException();

        public ExpressionValidationResult Validate(string expression, ExpressionContext context)
            => throw new NotSupportedException();
    }

    /// <summary>Test double handler; proves the DI registration and nothing else.</summary>
    private sealed class NoopNotificationHandler : INotificationHandler<DialogCompletedNotification>
    {
        public ValueTask Handle(DialogCompletedNotification notification, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;
    }

    /// <summary>Second test double handler for the multiple-registration case.</summary>
    private sealed class OtherNoopNotificationHandler : INotificationHandler<DialogCompletedNotification>
    {
        public ValueTask Handle(DialogCompletedNotification notification, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;
    }
}
