using Flirty.Domain;
using Flirty.Expressions;
using Flirty.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Configuration object for <see cref="FlirtyServiceCollectionExtensions.AddFlirty(IServiceCollection, System.Action{FlirtyOptions})"/>.
/// </summary>
/// <remarks>
/// Deliberately minimal in issue #20 (<see cref="ApplyMigrations"/>). Issue #34 extends the same class
/// <b>additively</b> and without breaking the existing surface with: the provider choice
/// (<see cref="UseSqlite(string)"/>/<see cref="UsePostgreSql(string)"/>/<see cref="UseSqlServer(string)"/>
/// incl. automatic <see cref="Flirty.Persistence.FlirtyDbContext"/> registration with the correct
/// <c>MigrationsAssembly</c>), an interchangeable expression evaluator
/// (<see cref="UseExpressionEvaluator{TEvaluator}"/>) and the registration of outbound webhooks
/// (<see cref="AddWebhook(string, string)"/>). All setters only collect configuration state; the
/// actual registrations are performed by the <c>AddFlirty(Action&lt;FlirtyOptions&gt;)</c> overload after
/// evaluating this object.
/// </remarks>
public sealed class FlirtyOptions
{
    /// <summary>
    /// Indicates whether the host should migrate automatically on start. Set via
    /// <see cref="ApplyMigrations"/> and evaluated by
    /// <see cref="FlirtyServiceCollectionExtensions.AddFlirty(IServiceCollection, System.Action{FlirtyOptions})"/>.
    /// </summary>
    internal bool MigrationsEnabled { get; private set; }

    /// <summary>
    /// Configuration of the <see cref="Flirty.Persistence.FlirtyDbContext"/> (provider + connection +
    /// <c>MigrationsAssembly</c>), set by one of the <c>Use*</c> provider methods. <c>null</c>
    /// as long as no provider has been chosen (then the context must come externally via <c>AddDbContext</c>).
    /// </summary>
    internal Action<DbContextOptionsBuilder>? ConfigureDbContext { get; private set; }

    /// <summary>
    /// Type of a custom <see cref="IExpressionEvaluator"/> that replaces the default registration.
    /// <c>null</c> as long as <see cref="UseExpressionEvaluator{TEvaluator}"/> has not been called.
    /// </summary>
    internal Type? ExpressionEvaluatorType { get; private set; }

    /// <summary>The outbound webhooks gathered via <see cref="AddWebhook(string, string)"/>.</summary>
    internal List<FlirtyWebhookRegistration> Webhooks { get; } = [];

    /// <summary>
    /// Enables auto-migration: registers the
    /// <see cref="Flirty.Hosting.FlirtyMigrationHostedService"/>, which on host start applies all pending
    /// EF Core migrations to the registered <see cref="Flirty.Persistence.FlirtyDbContext"/>.
    /// </summary>
    /// <returns>The same <see cref="FlirtyOptions"/> instance, to allow chaining calls.</returns>
    public FlirtyOptions ApplyMigrations()
    {
        MigrationsEnabled = true;
        return this;
    }

    /// <summary>
    /// Chooses SQLite as the database provider and registers the <see cref="Flirty.Persistence.FlirtyDbContext"/>
    /// with the migrations assembly <c>Flirty.Migrations.Sqlite</c>.
    /// </summary>
    /// <param name="connectionString">The SQLite connection string (e.g. <c>Data Source=flirty.db</c>).</param>
    /// <returns>The same <see cref="FlirtyOptions"/> instance, to allow chaining calls.</returns>
    /// <remarks>Another call to a <c>Use*</c> provider method overrides the previous choice.</remarks>
    public FlirtyOptions UseSqlite(string connectionString)
        => UseProvider(FlirtyDatabaseProvider.Sqlite, connectionString);

    /// <summary>
    /// Chooses PostgreSQL as the database provider and registers the <see cref="Flirty.Persistence.FlirtyDbContext"/>
    /// with the migrations assembly <c>Flirty.Migrations.PostgreSql</c>.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <returns>The same <see cref="FlirtyOptions"/> instance, to allow chaining calls.</returns>
    /// <remarks>Another call to a <c>Use*</c> provider method overrides the previous choice.</remarks>
    public FlirtyOptions UsePostgreSql(string connectionString)
        => UseProvider(FlirtyDatabaseProvider.PostgreSql, connectionString);

    /// <summary>
    /// Chooses SQL Server as the database provider and registers the <see cref="Flirty.Persistence.FlirtyDbContext"/>
    /// with the migrations assembly <c>Flirty.Migrations.SqlServer</c>.
    /// </summary>
    /// <param name="connectionString">The SQL Server connection string.</param>
    /// <returns>The same <see cref="FlirtyOptions"/> instance, to allow chaining calls.</returns>
    /// <remarks>Another call to a <c>Use*</c> provider method overrides the previous choice.</remarks>
    public FlirtyOptions UseSqlServer(string connectionString)
        => UseProvider(FlirtyDatabaseProvider.SqlServer, connectionString);

    /// <summary>
    /// Chooses the database provider based on the given <see cref="FlirtyDatabaseProvider"/> value and
    /// registers the <see cref="Flirty.Persistence.FlirtyDbContext"/> with the
    /// <c>MigrationsAssembly</c> matching the provider. The type-specific <c>Use*</c> methods delegate to this
    /// method.
    /// </summary>
    /// <param name="provider">The database provider to use.</param>
    /// <param name="connectionString">The connection string for the chosen provider.</param>
    /// <returns>The same <see cref="FlirtyOptions"/> instance, to allow chaining calls.</returns>
    /// <remarks>
    /// Since issue #37: allows the provider choice as a <b>value</b> and shares the same mapping with the
    /// designer's runtime profile choice
    /// (<see cref="Microsoft.EntityFrameworkCore.FlirtyDatabaseProviderExtensions"/>). Another call to
    /// a <c>Use*</c> provider method overrides the previous choice.
    /// </remarks>
    public FlirtyOptions UseProvider(FlirtyDatabaseProvider provider, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        ConfigureDbContext = options => options.UseFlirtyProvider(provider, connectionString);
        return this;
    }

    /// <summary>
    /// Replaces the default <see cref="IExpressionEvaluator"/> (<c>DynamicExpressoExpressionEvaluator</c>)
    /// with a custom implementation. The type is registered as a <see cref="ServiceLifetime.Singleton"/>
    /// (like the default; the engine is stateless).
    /// </summary>
    /// <typeparam name="TEvaluator">The evaluator type to register.</typeparam>
    /// <returns>The same <see cref="FlirtyOptions"/> instance, to allow chaining calls.</returns>
    public FlirtyOptions UseExpressionEvaluator<TEvaluator>()
        where TEvaluator : class, IExpressionEvaluator
    {
        ExpressionEvaluatorType = typeof(TEvaluator);
        return this;
    }

    /// <summary>
    /// Registers an outbound webhook to be delivered to the target URL when the given event occurs.
    /// </summary>
    /// <param name="eventName">The domain event name that triggers the webhook (e.g. <c>order-created</c>).</param>
    /// <param name="url">The target URL to which the webhook is delivered via HTTP.</param>
    /// <returns>The same <see cref="FlirtyOptions"/> instance, to allow chaining calls.</returns>
    /// <remarks>
    /// Stub from issue #34: the registration is gathered and provided in the container; the active
    /// delivery follows in EPIC 4 (M2). See <see cref="FlirtyWebhookRegistration"/>.
    /// </remarks>
    public FlirtyOptions AddWebhook(string eventName, string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        Webhooks.Add(new FlirtyWebhookRegistration(eventName, url));
        return this;
    }

    /// <summary>
    /// Registers an outbound webhook to be delivered to the target URL at the given trigger point
    /// (<paramref name="scope"/>) – optionally filtered by a condition expression.
    /// </summary>
    /// <param name="scope">
    /// The point in the dialog flow (see <see cref="TriggerScope"/>) at which the webhook fires; maps
    /// 1:1 to the notification published by the core.
    /// </param>
    /// <param name="url">The target URL to which the webhook is delivered via HTTP POST.</param>
    /// <param name="expression">
    /// Optional condition expression that is evaluated via <see cref="IExpressionEvaluator"/> and decides
    /// about firing (e.g. <c>age &gt; 18</c>). <see langword="null"/>/empty ⇒ unconditional.
    /// </param>
    /// <returns>The same <see cref="FlirtyOptions"/> instance, to allow chaining calls.</returns>
    /// <remarks>
    /// Since issue #33: these registrations are actively delivered by the built-in <c>WebhookNotificationHandler</c>
    /// via <c>IHttpClientFactory</c> (retry/timeout). If <paramref name="expression"/> is set,
    /// the handler loads session and dialog for evaluation. See <see cref="FlirtyWebhookRegistration"/>.
    /// </remarks>
    public FlirtyOptions AddWebhook(TriggerScope scope, string url, string? expression = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        Webhooks.Add(new FlirtyWebhookRegistration(scope.ToString(), url, scope, expression));
        return this;
    }
}
