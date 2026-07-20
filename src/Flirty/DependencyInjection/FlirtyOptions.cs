using Flirty.Domain;
using Flirty.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Konfigurationsobjekt für <see cref="FlirtyServiceCollectionExtensions.AddFlirty(IServiceCollection, System.Action{FlirtyOptions})"/>.
/// </summary>
/// <remarks>
/// In Issue #20 bewusst minimal (<see cref="ApplyMigrations"/>). Issue #34 erweitert dieselbe Klasse
/// <b>additiv</b> und ohne Bruch der bestehenden Oberfläche um: die Provider-Wahl
/// (<see cref="UseSqlite(string)"/>/<see cref="UsePostgreSql(string)"/>/<see cref="UseSqlServer(string)"/>
/// inkl. automatischer <see cref="Flirty.Persistence.FlirtyDbContext"/>-Registrierung mit der korrekten
/// <c>MigrationsAssembly</c>), einen austauschbaren Expression-Evaluator
/// (<see cref="UseExpressionEvaluator{TEvaluator}"/>) und die Registrierung von Outbound-Webhooks
/// (<see cref="AddWebhook(string, string)"/>). Alle Setzer sammeln nur Konfigurationszustand; die
/// eigentlichen Registrierungen nimmt der <c>AddFlirty(Action&lt;FlirtyOptions&gt;)</c>-Overload nach der
/// Auswertung dieses Objekts vor.
/// </remarks>
public sealed class FlirtyOptions
{
    /// <summary>
    /// Gibt an, ob beim Host-Start automatisch migriert werden soll. Wird über
    /// <see cref="ApplyMigrations"/> gesetzt und von
    /// <see cref="FlirtyServiceCollectionExtensions.AddFlirty(IServiceCollection, System.Action{FlirtyOptions})"/>
    /// ausgewertet.
    /// </summary>
    internal bool MigrationsEnabled { get; private set; }

    /// <summary>
    /// Konfiguration des <see cref="Flirty.Persistence.FlirtyDbContext"/> (Provider + Verbindung +
    /// <c>MigrationsAssembly</c>), gesetzt durch eine der <c>Use*</c>-Provider-Methoden. <c>null</c>,
    /// solange kein Provider gewählt wurde (dann muss der Kontext extern per <c>AddDbContext</c> kommen).
    /// </summary>
    internal Action<DbContextOptionsBuilder>? ConfigureDbContext { get; private set; }

    /// <summary>
    /// Typ eines benutzerdefinierten <see cref="IExpressionEvaluator"/>, der die Default-Registrierung
    /// ersetzt. <c>null</c>, solange <see cref="UseExpressionEvaluator{TEvaluator}"/> nicht aufgerufen wurde.
    /// </summary>
    internal Type? ExpressionEvaluatorType { get; private set; }

    /// <summary>Die über <see cref="AddWebhook(string, string)"/> gesammelten Outbound-Webhooks.</summary>
    internal List<FlirtyWebhookRegistration> Webhooks { get; } = [];

    /// <summary>
    /// Aktiviert die Auto-Migration: registriert den
    /// <see cref="Flirty.Hosting.FlirtyMigrationHostedService"/>, der beim Host-Start alle ausstehenden
    /// EF-Core-Migrationen auf den registrierten <see cref="Flirty.Persistence.FlirtyDbContext"/> anwendet.
    /// </summary>
    /// <returns>Dieselbe <see cref="FlirtyOptions"/>-Instanz, um Aufrufe verketten zu können.</returns>
    public FlirtyOptions ApplyMigrations()
    {
        MigrationsEnabled = true;
        return this;
    }

    /// <summary>
    /// Wählt SQLite als Datenbank-Provider und registriert den <see cref="Flirty.Persistence.FlirtyDbContext"/>
    /// mit der Migrations-Assembly <c>Flirty.Migrations.Sqlite</c>.
    /// </summary>
    /// <param name="connectionString">Die SQLite-Verbindungszeichenfolge (z. B. <c>Data Source=flirty.db</c>).</param>
    /// <returns>Dieselbe <see cref="FlirtyOptions"/>-Instanz, um Aufrufe verketten zu können.</returns>
    /// <remarks>Ein erneuter Aufruf einer <c>Use*</c>-Provider-Methode überschreibt die vorige Wahl.</remarks>
    public FlirtyOptions UseSqlite(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        ConfigureDbContext = options =>
            options.UseSqlite(connectionString, sqlite => sqlite.MigrationsAssembly("Flirty.Migrations.Sqlite"));
        return this;
    }

    /// <summary>
    /// Wählt PostgreSQL als Datenbank-Provider und registriert den <see cref="Flirty.Persistence.FlirtyDbContext"/>
    /// mit der Migrations-Assembly <c>Flirty.Migrations.PostgreSql</c>.
    /// </summary>
    /// <param name="connectionString">Die PostgreSQL-Verbindungszeichenfolge.</param>
    /// <returns>Dieselbe <see cref="FlirtyOptions"/>-Instanz, um Aufrufe verketten zu können.</returns>
    /// <remarks>Ein erneuter Aufruf einer <c>Use*</c>-Provider-Methode überschreibt die vorige Wahl.</remarks>
    public FlirtyOptions UsePostgreSql(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        ConfigureDbContext = options =>
            options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly("Flirty.Migrations.PostgreSql"));
        return this;
    }

    /// <summary>
    /// Wählt SQL Server als Datenbank-Provider und registriert den <see cref="Flirty.Persistence.FlirtyDbContext"/>
    /// mit der Migrations-Assembly <c>Flirty.Migrations.SqlServer</c>.
    /// </summary>
    /// <param name="connectionString">Die SQL-Server-Verbindungszeichenfolge.</param>
    /// <returns>Dieselbe <see cref="FlirtyOptions"/>-Instanz, um Aufrufe verketten zu können.</returns>
    /// <remarks>Ein erneuter Aufruf einer <c>Use*</c>-Provider-Methode überschreibt die vorige Wahl.</remarks>
    public FlirtyOptions UseSqlServer(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        ConfigureDbContext = options =>
            options.UseSqlServer(connectionString, sqlServer => sqlServer.MigrationsAssembly("Flirty.Migrations.SqlServer"));
        return this;
    }

    /// <summary>
    /// Ersetzt den Default-<see cref="IExpressionEvaluator"/> (<c>DynamicExpressoExpressionEvaluator</c>)
    /// durch eine eigene Implementierung. Der Typ wird als <see cref="ServiceLifetime.Singleton"/>
    /// registriert (wie der Default; die Engine ist zustandslos).
    /// </summary>
    /// <typeparam name="TEvaluator">Der zu registrierende Evaluator-Typ.</typeparam>
    /// <returns>Dieselbe <see cref="FlirtyOptions"/>-Instanz, um Aufrufe verketten zu können.</returns>
    public FlirtyOptions UseExpressionEvaluator<TEvaluator>()
        where TEvaluator : class, IExpressionEvaluator
    {
        ExpressionEvaluatorType = typeof(TEvaluator);
        return this;
    }

    /// <summary>
    /// Registriert einen Outbound-Webhook, der beim Eintreten des angegebenen Ereignisses an die Ziel-URL
    /// ausgeliefert werden soll.
    /// </summary>
    /// <param name="eventName">Der fachliche Ereignisname, der den Webhook auslöst (z. B. <c>order-created</c>).</param>
    /// <param name="url">Die Ziel-URL, an die der Webhook per HTTP ausgeliefert wird.</param>
    /// <returns>Dieselbe <see cref="FlirtyOptions"/>-Instanz, um Aufrufe verketten zu können.</returns>
    /// <remarks>
    /// Stub aus Issue #34: die Registrierung wird gesammelt und im Container bereitgestellt; die aktive
    /// Auslieferung folgt in EPIC 4 (M2). Siehe <see cref="FlirtyWebhookRegistration"/>.
    /// </remarks>
    public FlirtyOptions AddWebhook(string eventName, string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        Webhooks.Add(new FlirtyWebhookRegistration(eventName, url));
        return this;
    }

    /// <summary>
    /// Registriert einen Outbound-Webhook, der beim angegebenen Trigger-Zeitpunkt (<paramref name="scope"/>)
    /// an die Ziel-URL ausgeliefert wird – optional gefiltert durch einen Bedingungsausdruck.
    /// </summary>
    /// <param name="scope">
    /// Der Zeitpunkt im Dialogablauf (siehe <see cref="TriggerScope"/>), zu dem der Webhook auslöst; mappt
    /// 1:1 auf die vom Core publizierte Notification.
    /// </param>
    /// <param name="url">Die Ziel-URL, an die der Webhook per HTTP POST ausgeliefert wird.</param>
    /// <param name="expression">
    /// Optionaler Bedingungsausdruck, der über <see cref="IExpressionEvaluator"/> ausgewertet wird und über
    /// das Auslösen entscheidet (z. B. <c>age &gt; 18</c>). <see langword="null"/>/leer ⇒ bedingungslos.
    /// </param>
    /// <returns>Dieselbe <see cref="FlirtyOptions"/>-Instanz, um Aufrufe verketten zu können.</returns>
    /// <remarks>
    /// Seit Issue #33: Diese Registrierungen werden vom eingebauten <c>WebhookNotificationHandler</c> aktiv
    /// über <c>IHttpClientFactory</c> (Retry/Timeout) ausgeliefert. Ist <paramref name="expression"/> gesetzt,
    /// lädt der Handler zur Auswertung Session und Dialog nach. Siehe <see cref="FlirtyWebhookRegistration"/>.
    /// </remarks>
    public FlirtyOptions AddWebhook(TriggerScope scope, string url, string? expression = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        Webhooks.Add(new FlirtyWebhookRegistration(scope.ToString(), url, scope, expression));
        return this;
    }
}
