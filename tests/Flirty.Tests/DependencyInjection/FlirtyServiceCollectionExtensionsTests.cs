using Flirty.Domain;
using Flirty.Expressions;
using Flirty.Persistence;
using Flirty.Placeholders;
using Flirty.Runtime;
using Flirty.Tests.Persistence;
using Flirty.Tests.Runtime;
using Flirty.Validation;
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

    // ---- Custom question types (#136) --------------------------------------------------------

    /// <summary>
    /// The lifetime change is <b>opt-in</b>: a host that declares no custom question type must keep the
    /// plain singleton. Asserted on the implementation type as well, because a refactor that swapped in
    /// a factory would keep the lifetime and still break the promise.
    /// </summary>
    [Fact]
    public void Without_a_custom_question_type_the_answer_validator_stays_a_singleton()
    {
        var services = new ServiceCollection();

        services.AddFlirty();

        var descriptor = Assert.Single(
            services, service => service.ServiceType == typeof(IAnswerValidator));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        Assert.Equal(typeof(AnswerValidator), descriptor.ImplementationType);
    }

    /// <summary>The registry is always resolvable, so a client can be told "none declared".</summary>
    [Fact]
    public void Without_a_custom_question_type_the_registry_resolves_empty()
    {
        using var provider = new ServiceCollection().AddLogging().AddFlirty().BuildServiceProvider();

        Assert.Empty(provider.GetRequiredService<FlirtyQuestionTypeRegistry>().Types);
    }

    [Fact]
    public void AddQuestionType_makes_the_answer_validator_a_scoped_decorator()
    {
        var services = new ServiceCollection();

        services.AddLogging().AddFlirty(options =>
            options.AddQuestionType<ProbeQuestionTypeValidator>("color", "Colour picker", "\"#ff0000\""));

        var descriptor = Assert.Single(
            services, service => service.ServiceType == typeof(IAnswerValidator));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);

        var validatorDescriptor = Assert.Single(
            services, service => service.ServiceType == typeof(ProbeQuestionTypeValidator));
        Assert.Equal(ServiceLifetime.Scoped, validatorDescriptor.Lifetime);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.IsType<CustomQuestionTypeAnswerValidator>(
            scope.ServiceProvider.GetRequiredService<IAnswerValidator>());
    }

    /// <summary>
    /// The whole point of the decorator taking the <see cref="IServiceProvider"/> rather than an
    /// <c>IServiceScopeFactory</c>: a host validator must see the <b>same</b> scoped instances the
    /// handler sees. A second scope would hand it a different <c>FlirtyDbContext</c>.
    /// </summary>
    [Fact]
    public void A_host_validator_is_resolved_from_the_request_scope()
    {
        var services = new ServiceCollection();
        services.AddLogging().AddScoped<ScopeProbe>();
        services.AddFlirty(options =>
            options.AddQuestionType<ProbeQuestionTypeValidator>("probe", "Probe"));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var question = new Question
        {
            Id = Guid.NewGuid(),
            DialogId = Guid.NewGuid(),
            Key = "q",
            Text = "Question?",
            Type = QuestionType.Json,
            CustomTypeKey = "probe",
        };

        scope.ServiceProvider.GetRequiredService<IAnswerValidator>().Validate(question, "{}");

        Assert.Same(
            scope.ServiceProvider.GetRequiredService<ScopeProbe>(),
            ProbeQuestionTypeValidator.LastSeenProbe);
    }

    /// <summary>Catches a singleton that starts depending on the now-scoped validator.</summary>
    [Fact]
    public void AddQuestionType_keeps_the_container_scope_valid()
    {
        var services = new ServiceCollection();
        services.AddLogging().AddScoped<ScopeProbe>();
        services.AddFlirty(options => options
            .UseSqlite("Data Source=:memory:")
            .AddQuestionType<ProbeQuestionTypeValidator>("probe", "Probe"));

        using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IAnswerValidator>());
    }

    /// <summary>
    /// A bad declaration must fail where it is written, not at the first submitted answer – by then the
    /// dialog may be published and unrepairable.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Color")]
    [InlineData("col_or")]
    [InlineData("col or")]
    [InlineData("cölor")]
    public void AddQuestionType_rejects_an_unusable_key(string key)
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new ServiceCollection().AddFlirty(options => options.AddQuestionType(key, "Display")));

        Assert.Equal("key", exception.ParamName);
    }

    [Fact]
    public void AddQuestionType_rejects_a_duplicate_key()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new ServiceCollection().AddFlirty(options => options
                .AddQuestionType("color", "Colour picker")
                .AddQuestionType("color", "Something else")));

        Assert.Equal("key", exception.ParamName);
    }

    /// <summary>A malformed sample would teach every client a malformed shape.</summary>
    [Fact]
    public void AddQuestionType_rejects_a_sample_that_is_not_json()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new ServiceCollection().AddFlirty(options =>
                options.AddQuestionType("color", "Colour picker", sample: "#ff0000")));

        Assert.Equal("sample", exception.ParamName);
    }

    [Fact]
    public void AddQuestionType_gathers_the_declarations_into_the_registry()
    {
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddFlirty(options => options
                .AddQuestionType("zip", "Postal code")
                .AddQuestionType<ProbeQuestionTypeValidator>("color", "Colour picker", "\"#ff0000\""))
            .BuildServiceProvider();

        var types = provider.GetRequiredService<FlirtyQuestionTypeRegistry>().Types;

        // Ordered by key, so a client sees a stable list.
        Assert.Collection(
            types,
            type =>
            {
                Assert.Equal("color", type.Key);
                Assert.Equal("Colour picker", type.DisplayName);
                Assert.Equal(typeof(ProbeQuestionTypeValidator), type.ValidatorType);
                Assert.Equal("\"#ff0000\"", type.SampleValue);
            },
            type =>
            {
                Assert.Equal("zip", type.Key);
                Assert.Null(type.ValidatorType);
                Assert.Null(type.SampleValue);
            });
    }

    // ---- Message placeholders (#140) ---------------------------------------------------------

    /// <summary>The registry is always resolvable, so a client can be told "none declared".</summary>
    [Fact]
    public void Without_a_placeholder_the_registry_resolves_empty()
    {
        using var provider = new ServiceCollection().AddLogging().AddFlirty().BuildServiceProvider();

        Assert.Empty(provider.GetRequiredService<FlirtyPlaceholderRegistry>().Placeholders);
    }

    /// <summary>
    /// The renderer is <see cref="ServiceLifetime.Scoped"/> from the start, and declaring a placeholder
    /// does <b>not</b> move it – the "no lifetime change" promise of gating by absence (unlike the
    /// question-type decorator, which does swap the validator lifetime).
    /// </summary>
    [Fact]
    public void The_placeholder_renderer_is_scoped_with_or_without_a_declaration()
    {
        var without = new ServiceCollection().AddLogging().AddFlirty();
        var with = new ServiceCollection().AddLogging().AddFlirty(
            options => options.AddPlaceholder<ProbePlaceholderFiller>("user-name", "User name"));

        foreach (var services in new[] { without, with })
        {
            var descriptor = Assert.Single(
                services, service => service.ServiceType == typeof(PlaceholderRenderer));
            Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        }
    }

    /// <summary>A declared filler is resolvable from the request scope (registered <see cref="ServiceLifetime.Scoped"/>).</summary>
    [Fact]
    public void AddPlaceholder_registers_the_filler_type_as_scoped()
    {
        var services = new ServiceCollection();

        services.AddLogging().AddFlirty(
            options => options.AddPlaceholder<ProbePlaceholderFiller>("user-name", "User name"));

        var descriptor = Assert.Single(
            services, service => service.ServiceType == typeof(ProbePlaceholderFiller));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void AddPlaceholder_gathers_the_declarations_into_the_registry()
    {
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddFlirty(options => options
                .AddPlaceholder("today", "Today's date", "2026-08-07")
                .AddPlaceholder<ProbePlaceholderFiller>("user-name", "User name"))
            .BuildServiceProvider();

        var placeholders = provider.GetRequiredService<FlirtyPlaceholderRegistry>().Placeholders;

        // Ordered by key, so a client sees a stable list.
        Assert.Collection(
            placeholders,
            placeholder =>
            {
                Assert.Equal("today", placeholder.Key);
                Assert.Null(placeholder.FillerType);
                Assert.Equal("2026-08-07", placeholder.Sample);
            },
            placeholder =>
            {
                Assert.Equal("user-name", placeholder.Key);
                Assert.Equal(typeof(ProbePlaceholderFiller), placeholder.FillerType);
            });
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("User-Name")]
    [InlineData("user_name")]
    [InlineData("user name")]
    [InlineData("übung")]
    public void AddPlaceholder_rejects_an_unusable_key(string key)
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new ServiceCollection().AddFlirty(options => options.AddPlaceholder(key, "Display")));

        Assert.Equal("key", exception.ParamName);
    }

    [Fact]
    public void AddPlaceholder_rejects_a_duplicate_key()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new ServiceCollection().AddFlirty(options => options
                .AddPlaceholder("user-name", "User name")
                .AddPlaceholder<ProbePlaceholderFiller>("user-name", "Again")));

        Assert.Equal("key", exception.ParamName);
    }

    /// <summary>
    /// Unlike a custom question-type sample, a placeholder sample is a plain value substituted into a
    /// message – not a JSON answer document – so any string is accepted.
    /// </summary>
    [Fact]
    public void AddPlaceholder_accepts_a_non_json_sample()
    {
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddFlirty(options => options.AddPlaceholder("user-name", "User name", "Alice"))
            .BuildServiceProvider();

        var placeholder = Assert.Single(provider.GetRequiredService<FlirtyPlaceholderRegistry>().Placeholders);
        Assert.Equal("Alice", placeholder.Sample);
    }

    /// <summary>Test double filler; proves the DI registration and nothing else.</summary>
    private sealed class ProbePlaceholderFiller : IPlaceholderFiller
    {
        public ValueTask<string?> FillAsync(PlaceholderContext context, CancellationToken cancellationToken)
            => new("x");
    }

    /// <summary>Scoped marker, used to prove which scope the host validator was resolved from.</summary>
    private sealed class ScopeProbe;

    /// <summary>Test double: records the scoped dependency it was constructed with.</summary>
    private sealed class ProbeQuestionTypeValidator : IQuestionTypeValidator
    {
        public ProbeQuestionTypeValidator(ScopeProbe probe) => LastSeenProbe = probe;

        public static ScopeProbe? LastSeenProbe { get; private set; }

        public AnswerValidationResult Validate(Question question, string value)
            => AnswerValidationResult.Valid;
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
