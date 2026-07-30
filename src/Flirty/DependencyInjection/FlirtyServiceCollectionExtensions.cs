using Flirty.Expressions;
using Flirty.Hosting;
using Flirty.Persistence;
using Flirty.Pipeline;
using Flirty.Runtime;
using Flirty.Validation;
using Mediator;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for registering the Flirty engine in the
/// dependency injection container.
/// </summary>
public static class FlirtyServiceCollectionExtensions
{
    /// <summary>
    /// Registers the mediator (martinothamar, source generator) together with the
    /// base pipeline behaviors (<see cref="LoggingPipelineBehavior{TMessage, TResponse}"/> and
    /// <see cref="ValidationPipelineBehavior{TMessage, TResponse}"/>) in the given
    /// <see cref="IServiceCollection"/>.
    /// </summary>
    /// <remarks>
    /// Stub from issue #14: provides the minimal mediator wiring of the core.
    /// The options overload <see cref="AddFlirty(IServiceCollection, Action{FlirtyOptions})"/> (issue #20)
    /// builds on this and, on <c>o.ApplyMigrations()</c>, enables auto-migration. The overload
    /// additionally extends <see cref="FlirtyOptions"/> (since issue #34) additively with provider choice (incl.
    /// <see cref="FlirtyDbContext"/> registration), webhooks and an interchangeable expression evaluator.
    /// Open-generic pipeline behaviors are deliberately registered manually with the mediator.
    /// Since issue #21, <see cref="IDialogStore"/> (implementation
    /// <see cref="DialogStore"/>) is additionally registered as <see cref="ServiceLifetime.Scoped"/> – the same
    /// lifetime as the <see cref="FlirtyDbContext"/> the store requires. Since issue #36
    /// the writing <see cref="IDialogAdminStore"/> (implementation
    /// <see cref="DialogAdminStore"/>) is registered analogously as <see cref="ServiceLifetime.Scoped"/> – it is used by
    /// the admin CRUD handlers for the (tracked) configuration graph. The registration
    /// itself is inert; <see cref="IDialogStore"/> can only be resolved once a
    /// <see cref="FlirtyDbContext"/> (provider + <c>MigrationsAssembly</c>) is registered – conveniently
    /// via <c>o.UseSqlite/UsePostgreSql/UseSqlServer</c> (since #34) or externally via <c>AddDbContext</c>.
    /// Since issue #25, the runtime facade <see cref="IFlirtyEngine"/> (implementation
    /// <see cref="FlirtyEngine"/>) is additionally registered as <see cref="ServiceLifetime.Scoped"/> – the same
    /// lifetime as the mediator and <see cref="IDialogStore"/>, which it uses indirectly.
    /// Since issue #26, the <see cref="IExpressionEvaluator"/> (default
    /// <see cref="DynamicExpressoExpressionEvaluator"/>) is registered as <see cref="ServiceLifetime.Singleton"/> –
    /// the engine is stateless and is needed by the <see cref="SubmitAnswerCommandHandler"/>
    /// to evaluate the transitions (branching). The interchangeable overload
    /// <c>o.UseExpressionEvaluator&lt;T&gt;()</c> is available since #34.
    /// Since issue #30, the <see cref="IAnswerValidator"/> (default <see cref="AnswerValidator"/>) is registered as
    /// <see cref="ServiceLifetime.Singleton"/> (stateless) and the
    /// <c>AnswerValidationPipelineBehavior</c> per answer-submitting command
    /// (<see cref="SubmitAnswerCommand"/>/<see cref="EditAnswerCommand"/>) <b>closed</b> as
    /// <see cref="ServiceLifetime.Scoped"/> – so it validates the answer value (type + <c>ValidationRules</c>)
    /// before the handler, without being resolved for <c>FlirtyDbContext</c>-free messages.
    /// Since issue #33 the outbound webhook infrastructure is provided: a (by default empty)
    /// <see cref="IReadOnlyList{T}"/> of <see cref="FlirtyWebhookRegistration"/> (replaced by the options overload)
    /// and the resilient named <see cref="System.Net.Http.IHttpClientFactory"/> client that the
    /// <see cref="Flirty.Runtime.WebhookNotificationHandler"/> (automatically registered by the mediator) needs on
    /// every publish.
    /// </remarks>
    /// <param name="services">The service collection to extend.</param>
    /// <returns>The same <see cref="IServiceCollection"/>, to allow chaining calls.</returns>
    public static IServiceCollection AddFlirty(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);

        services.AddSingleton(typeof(IPipelineBehavior<,>), typeof(LoggingPipelineBehavior<,>));
        services.AddSingleton(typeof(IPipelineBehavior<,>), typeof(ValidationPipelineBehavior<,>));

        services.AddScoped<IDialogStore, DialogStore>();
        services.AddScoped<IDialogAdminStore, DialogAdminStore>();
        services.AddScoped<IFlirtyEngine, FlirtyEngine>();

        services.AddSingleton<IExpressionEvaluator, DynamicExpressoExpressionEvaluator>();

        // Issue #30: domain answer validation. The validator is stateless (singleton); the
        // behavior is deliberately registered CLOSED per answer-submitting command (not
        // open-generic), because it needs the scoped IDialogStore and would otherwise also be constructed for messages
        // without a registered FlirtyDbContext. Scoped, so that it shares the context with
        // the handler. Registered after the base behaviors -> runs directly before the handler.
        services.AddSingleton<IAnswerValidator, AnswerValidator>();
        services.AddScoped<
            IPipelineBehavior<SubmitAnswerCommand, SubmitAnswerResult>,
            AnswerValidationPipelineBehavior<SubmitAnswerCommand, SubmitAnswerResult>>();
        services.AddScoped<
            IPipelineBehavior<EditAnswerCommand, EditAnswerResult>,
            AnswerValidationPipelineBehavior<EditAnswerCommand, EditAnswerResult>>();

        // Issue #33: outbound webhook delivery. The WebhookNotificationHandler lives in the core and is
        // AUTOMATICALLY registered per notification by the mediator source generator (rule 1, docs/MEDIATOR.md).
        // It is therefore constructed on every publish and needs its dependencies always resolvable:
        //   * the (by default empty) registry of targets – the options overload replaces it with the
        //     registrations gathered via o.AddWebhook(...),
        //   * the resiliently (retry/timeout via standard resilience) configured named HttpClient.
        services.AddSingleton<IReadOnlyList<FlirtyWebhookRegistration>>(Array.Empty<FlirtyWebhookRegistration>());
        services.AddHttpClient(WebhookNotificationHandler.HttpClientName).AddStandardResilienceHandler();

        return services;
    }

    /// <summary>
    /// Registers the Flirty engine like <see cref="AddFlirty(IServiceCollection)"/> and additionally evaluates
    /// the <see cref="FlirtyOptions"/> set via <paramref name="configure"/>.
    /// </summary>
    /// <remarks>
    /// Evaluates the options set via <paramref name="configure"/> and performs the following
    /// registrations additively:
    /// <list type="bullet">
    /// <item>Provider choice (<c>o.UseSqlite/UsePostgreSql/UseSqlServer</c>, since #34): registers the
    /// <see cref="FlirtyDbContext"/> with provider and matching <c>MigrationsAssembly</c>.</item>
    /// <item>Interchangeable evaluator (<c>o.UseExpressionEvaluator&lt;T&gt;()</c>, since #34): replaces the
    /// default singleton registration of the <see cref="IExpressionEvaluator"/>.</item>
    /// <item>Webhooks (<c>o.AddWebhook(...)</c>, registration since #34): replaces the empty default registry
    /// with the gathered <see cref="FlirtyWebhookRegistration"/>. Since #33 the built-in
    /// <see cref="Flirty.Runtime.WebhookNotificationHandler"/> actively delivers these targets (HTTP + retry/timeout).</item>
    /// <item>Auto-migration (<see cref="FlirtyOptions.ApplyMigrations"/>, issue #20): registers the
    /// <see cref="FlirtyMigrationHostedService"/>. Requires a registered <see cref="FlirtyDbContext"/>
    /// – either via the provider choice or externally via <c>AddDbContext</c>.</item>
    /// </list>
    /// </remarks>
    /// <param name="services">The service collection to extend.</param>
    /// <param name="configure">Delegate for configuring the <see cref="FlirtyOptions"/>.</param>
    /// <returns>The same <see cref="IServiceCollection"/>, to allow chaining calls.</returns>
    public static IServiceCollection AddFlirty(this IServiceCollection services, Action<FlirtyOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddFlirty();

        var options = new FlirtyOptions();
        configure(options);

        // Provider choice (#34): registers the FlirtyDbContext together with provider and MigrationsAssembly.
        // The default lifetime of AddDbContext is Scoped – the same lifetime as IDialogStore/IFlirtyEngine.
        if (options.ConfigureDbContext is not null)
        {
            services.AddDbContext<FlirtyDbContext>(options.ConfigureDbContext);
        }

        // Interchangeable evaluator (#34): replaces the default singleton registration set in AddFlirty().
        if (options.ExpressionEvaluatorType is not null)
        {
            services.Replace(ServiceDescriptor.Singleton(typeof(IExpressionEvaluator), options.ExpressionEvaluatorType));
        }

        // Webhooks (#34 registration, #33 delivery): replace the (empty) default registry set in AddFlirty()
        // with the targets actually gathered via o.AddWebhook(...). The built-in
        // WebhookNotificationHandler (auto-registered) consumes exactly this list.
        services.Replace(ServiceDescriptor.Singleton<IReadOnlyList<FlirtyWebhookRegistration>>(options.Webhooks.AsReadOnly()));

        if (options.MigrationsEnabled)
        {
            services.AddHostedService<FlirtyMigrationHostedService>();
        }

        return services;
    }

    /// <summary>
    /// Registers a custom <see cref="INotificationHandler{TNotification}"/> as an in-process callback channel
    /// that handles trigger notifications published by the engine (e.g. <see cref="DialogCompletedNotification"/>).
    /// Convenience wrapper of the raw DI line
    /// <c>services.Add[Scoped]&lt;INotificationHandler&lt;T&gt;, THandler&gt;()</c>.
    /// </summary>
    /// <remarks>
    /// Issue #32: convenient "plugging in" of custom handlers (console scenario). Since #31 the engine calls all handlers
    /// registered via DI synchronously in the same scope when publishing (see
    /// <see href="../../../docs/TRIGGERS.md">TRIGGERS.md</see>). Deliberately registered via the <see cref="IServiceCollection"/>
    /// <c>Add</c> (and not <c>TryAdd</c>/<c>Replace</c>), because <b>multiple</b> handlers per notification are
    /// allowed and all should be called. The default lifetime
    /// <see cref="ServiceLifetime.Scoped"/> matches the mediator wiring
    /// (<c>AddMediator(o =&gt; o.ServiceLifetime = ServiceLifetime.Scoped)</c>) in <see cref="AddFlirty(IServiceCollection)"/>;
    /// via <paramref name="lifetime"/> a stateless handler, for example, can also be registered as
    /// <see cref="ServiceLifetime.Singleton"/>. The helper does not require a prior
    /// <see cref="AddFlirty(IServiceCollection)"/>, but is typically chained with it.
    /// </remarks>
    /// <typeparam name="TNotification">The notification contract the handler reacts to.</typeparam>
    /// <typeparam name="THandler">The handler type to register.</typeparam>
    /// <param name="services">The service collection to extend.</param>
    /// <param name="lifetime">The lifetime of the handler; default <see cref="ServiceLifetime.Scoped"/>.</param>
    /// <returns>The same <see cref="IServiceCollection"/>, to allow chaining calls.</returns>
    /// <example>
    /// <code>
    /// services
    ///     .AddFlirty(o =&gt; o.UseSqlite(connectionString))
    ///     .AddFlirtyHandler&lt;DialogCompletedNotification, OnDialogCompleted&gt;();
    /// </code>
    /// </example>
    public static IServiceCollection AddFlirtyHandler<TNotification, THandler>(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TNotification : INotification
        where THandler : class, INotificationHandler<TNotification>
    {
        ArgumentNullException.ThrowIfNull(services);

        services.Add(new ServiceDescriptor(
            typeof(INotificationHandler<TNotification>), typeof(THandler), lifetime));

        return services;
    }
}
