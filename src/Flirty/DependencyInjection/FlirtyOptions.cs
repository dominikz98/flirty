using System.Text.Json;
using Flirty.Domain;
using Flirty.Expressions;
using Flirty.Persistence;
using Flirty.Validation;
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
    /// The custom question types gathered via <see cref="AddQuestionType(string, string, string)"/>,
    /// keyed ordinally – see <see cref="FlirtyQuestionTypeRegistry"/> for why not case-insensitively.
    /// </summary>
    internal Dictionary<string, FlirtyQuestionType> QuestionTypes { get; } =
        new(StringComparer.Ordinal);

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

    /// <summary>
    /// Declares a custom question type <b>with</b> its own validator. The validator type is registered
    /// as <see cref="ServiceLifetime.Scoped"/> and resolved from the request scope, so it may take
    /// scoped dependencies – including the same <see cref="Flirty.Persistence.FlirtyDbContext"/> the
    /// handler uses.
    /// </summary>
    /// <typeparam name="TValidator">The <see cref="IQuestionTypeValidator"/> implementation.</typeparam>
    /// <param name="key">The key the type is stored under, see the other overload.</param>
    /// <param name="displayName">A human-readable name for clients.</param>
    /// <param name="sample">An optional example answer as JSON.</param>
    /// <returns>The same <see cref="FlirtyOptions"/> instance, to allow chaining calls.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="key"/> is empty, uses characters outside <c>[a-z0-9-]</c> or is already declared;
    /// or <paramref name="sample"/> is not valid JSON.
    /// </exception>
    public FlirtyOptions AddQuestionType<TValidator>(
        string key, string displayName, string? sample = null)
        where TValidator : class, IQuestionTypeValidator
        => AddQuestionType(key, displayName, typeof(TValidator), sample);

    /// <summary>
    /// Declares a custom question type <b>without</b> a validator: the answer is then checked for
    /// well-formed JSON only. That is a legitimate declaration rather than a half-finished one – it
    /// names a shape for clients (via <paramref name="displayName"/> and <paramref name="sample"/>)
    /// without claiming semantics the host does not check.
    /// </summary>
    /// <param name="key">
    /// The key the type is stored under (in <see cref="Flirty.Domain.Question.CustomTypeKey"/>) and
    /// looked up by. Lowercase ASCII letters, digits and <c>-</c> only, compared ordinally.
    /// </param>
    /// <param name="displayName">A human-readable name for clients.</param>
    /// <param name="sample">An optional example answer as JSON.</param>
    /// <returns>The same <see cref="FlirtyOptions"/> instance, to allow chaining calls.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="key"/> is empty, uses characters outside <c>[a-z0-9-]</c> or is already declared;
    /// or <paramref name="sample"/> is not valid JSON.
    /// </exception>
    public FlirtyOptions AddQuestionType(string key, string displayName, string? sample = null)
        => AddQuestionType(key, displayName, validatorType: null, sample);

    private FlirtyOptions AddQuestionType(
        string key, string displayName, Type? validatorType, string? sample)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        if (!IsQuestionTypeKey(key))
        {
            throw new ArgumentException(
                $"The custom question type key '{key}' is not usable. Use lowercase ASCII letters, "
                + "digits and '-' only: the key is stored in the Question.CustomTypeKey column and "
                + "looked up ordinally, so a casing difference between the declaration and a question "
                + "would silently degrade that question to a plain JSON check.",
                nameof(key));
        }

        if (sample is not null && !IsJson(sample))
        {
            throw new ArgumentException(
                $"The sample value of the custom question type '{key}' is not valid JSON. It is handed "
                + "to clients as an example answer, so a malformed one would teach them a malformed "
                + "shape.",
                nameof(sample));
        }

        if (!QuestionTypes.TryAdd(key, new FlirtyQuestionType(key, displayName, validatorType, sample)))
        {
            throw new ArgumentException(
                $"The custom question type '{key}' is already declared.", nameof(key));
        }

        return this;
    }

    private static bool IsQuestionTypeKey(string key)
        => key.All(character =>
            char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character is '-');

    private static bool IsJson(string value)
    {
        try
        {
            using var document = JsonDocument.Parse(value);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
