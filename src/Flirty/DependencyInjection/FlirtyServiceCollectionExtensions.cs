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
/// Extension-Methoden zur Registrierung der Flirty-Engine im
/// Dependency-Injection-Container.
/// </summary>
public static class FlirtyServiceCollectionExtensions
{
    /// <summary>
    /// Registriert den Mediator (martinothamar, Source-Generator) samt den
    /// Basis-Pipeline-Behaviors (<see cref="LoggingPipelineBehavior{TMessage, TResponse}"/> und
    /// <see cref="ValidationPipelineBehavior{TMessage, TResponse}"/>) im übergebenen
    /// <see cref="IServiceCollection"/>.
    /// </summary>
    /// <remarks>
    /// Stub aus Issue #14: stellt die minimale Mediator-Verdrahtung des Cores bereit.
    /// Der Options-Overload <see cref="AddFlirty(IServiceCollection, Action{FlirtyOptions})"/> (Issue #20)
    /// baut hierauf auf und aktiviert bei <c>o.ApplyMigrations()</c> die Auto-Migration. Der Overload
    /// erweitert <see cref="FlirtyOptions"/> zudem (seit Issue #34) additiv um Provider-Wahl (inkl.
    /// <see cref="FlirtyDbContext"/>-Registrierung), Webhooks und austauschbaren Expression-Evaluator.
    /// Offen-generische Pipeline-Behaviors werden bei Mediator bewusst manuell registriert.
    /// Seit Issue #21 wird zusätzlich <see cref="IDialogStore"/> (Implementierung
    /// <see cref="DialogStore"/>) als <see cref="ServiceLifetime.Scoped"/> registriert – dieselbe
    /// Lebensdauer wie der <see cref="FlirtyDbContext"/>, den der Store voraussetzt. Die Registrierung
    /// selbst ist inert; aufgelöst werden kann <see cref="IDialogStore"/> erst, sobald ein
    /// <see cref="FlirtyDbContext"/> (Provider + <c>MigrationsAssembly</c>) registriert ist – komfortabel
    /// via <c>o.UseSqlite/UsePostgreSql/UseSqlServer</c> (seit #34) oder extern per <c>AddDbContext</c>.
    /// Seit Issue #25 wird zusätzlich die Runtime-Facade <see cref="IFlirtyEngine"/> (Implementierung
    /// <see cref="FlirtyEngine"/>) als <see cref="ServiceLifetime.Scoped"/> registriert – dieselbe
    /// Lebensdauer wie Mediator und <see cref="IDialogStore"/>, die sie mittelbar nutzt.
    /// Seit Issue #26 wird der <see cref="IExpressionEvaluator"/> (Default
    /// <see cref="DynamicExpressoExpressionEvaluator"/>) als <see cref="ServiceLifetime.Singleton"/>
    /// registriert – die Engine ist zustandslos und wird vom <see cref="SubmitAnswerCommandHandler"/>
    /// zur Auswertung der Übergänge (Branching) benötigt. Der austauschbare Overload
    /// <c>o.UseExpressionEvaluator&lt;T&gt;()</c> steht seit #34 bereit.
    /// Seit Issue #30 wird der <see cref="IAnswerValidator"/> (Default <see cref="AnswerValidator"/>) als
    /// <see cref="ServiceLifetime.Singleton"/> registriert (zustandslos) und das
    /// <c>AnswerValidationPipelineBehavior</c> je antworteinreichendem Command
    /// (<see cref="SubmitAnswerCommand"/>/<see cref="EditAnswerCommand"/>) <b>geschlossen</b> als
    /// <see cref="ServiceLifetime.Scoped"/> – so validiert es den Antwortwert (Typ + <c>ValidationRules</c>)
    /// vor dem Handler, ohne für <c>FlirtyDbContext</c>-freie Nachrichten aufgelöst zu werden.
    /// </remarks>
    /// <param name="services">Die zu erweiternde Service-Collection.</param>
    /// <returns>Dieselbe <see cref="IServiceCollection"/>, um Aufrufe verketten zu können.</returns>
    public static IServiceCollection AddFlirty(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);

        services.AddSingleton(typeof(IPipelineBehavior<,>), typeof(LoggingPipelineBehavior<,>));
        services.AddSingleton(typeof(IPipelineBehavior<,>), typeof(ValidationPipelineBehavior<,>));

        services.AddScoped<IDialogStore, DialogStore>();
        services.AddScoped<IFlirtyEngine, FlirtyEngine>();

        services.AddSingleton<IExpressionEvaluator, DynamicExpressoExpressionEvaluator>();

        // Issue #30: fachliche Antwort-Validierung. Der Validator ist zustandslos (Singleton); das
        // Behavior wird bewusst GESCHLOSSEN je antworteinreichendem Command registriert (nicht
        // offen-generisch), weil es den scoped IDialogStore braucht und sonst auch für Nachrichten
        // ohne registrierten FlirtyDbContext konstruiert würde. Scoped, damit es sich den Kontext mit
        // dem Handler teilt. Nach den Basis-Behaviors registriert -> läuft direkt vor dem Handler.
        services.AddSingleton<IAnswerValidator, AnswerValidator>();
        services.AddScoped<
            IPipelineBehavior<SubmitAnswerCommand, SubmitAnswerResult>,
            AnswerValidationPipelineBehavior<SubmitAnswerCommand, SubmitAnswerResult>>();
        services.AddScoped<
            IPipelineBehavior<EditAnswerCommand, EditAnswerResult>,
            AnswerValidationPipelineBehavior<EditAnswerCommand, EditAnswerResult>>();

        return services;
    }

    /// <summary>
    /// Registriert die Flirty-Engine wie <see cref="AddFlirty(IServiceCollection)"/> und wertet zusätzlich
    /// die über <paramref name="configure"/> gesetzten <see cref="FlirtyOptions"/> aus.
    /// </summary>
    /// <remarks>
    /// Wertet die über <paramref name="configure"/> gesetzten Optionen aus und nimmt additiv folgende
    /// Registrierungen vor:
    /// <list type="bullet">
    /// <item>Provider-Wahl (<c>o.UseSqlite/UsePostgreSql/UseSqlServer</c>, seit #34): registriert den
    /// <see cref="FlirtyDbContext"/> mit Provider und passender <c>MigrationsAssembly</c>.</item>
    /// <item>Austauschbarer Evaluator (<c>o.UseExpressionEvaluator&lt;T&gt;()</c>, seit #34): ersetzt die
    /// Default-Singleton-Registrierung des <see cref="IExpressionEvaluator"/>.</item>
    /// <item>Webhooks (<c>o.AddWebhook(...)</c>, seit #34): stellt die gesammelten
    /// <see cref="FlirtyWebhookRegistration"/> als <see cref="IReadOnlyList{T}"/> bereit (Stub; die aktive
    /// Auslieferung folgt in EPIC 4/M2).</item>
    /// <item>Auto-Migration (<see cref="FlirtyOptions.ApplyMigrations"/>, Issue #20): registriert den
    /// <see cref="FlirtyMigrationHostedService"/>. Setzt einen registrierten <see cref="FlirtyDbContext"/>
    /// voraus – entweder über die Provider-Wahl oder extern per <c>AddDbContext</c>.</item>
    /// </list>
    /// </remarks>
    /// <param name="services">Die zu erweiternde Service-Collection.</param>
    /// <param name="configure">Delegat zum Konfigurieren der <see cref="FlirtyOptions"/>.</param>
    /// <returns>Dieselbe <see cref="IServiceCollection"/>, um Aufrufe verketten zu können.</returns>
    public static IServiceCollection AddFlirty(this IServiceCollection services, Action<FlirtyOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddFlirty();

        var options = new FlirtyOptions();
        configure(options);

        // Provider-Wahl (#34): registriert den FlirtyDbContext samt Provider und MigrationsAssembly.
        // Default-Lifetime von AddDbContext ist Scoped – dieselbe Lebensdauer wie IDialogStore/IFlirtyEngine.
        if (options.ConfigureDbContext is not null)
        {
            services.AddDbContext<FlirtyDbContext>(options.ConfigureDbContext);
        }

        // Austauschbarer Evaluator (#34): ersetzt die in AddFlirty() gesetzte Default-Singleton-Registrierung.
        if (options.ExpressionEvaluatorType is not null)
        {
            services.Replace(ServiceDescriptor.Singleton(typeof(IExpressionEvaluator), options.ExpressionEvaluatorType));
        }

        // Webhook-Stub (#34): die gesammelten Registrierungen immer bereitstellen (ggf. leer), damit der
        // Outbound-Handler aus EPIC 4 (M2) einen zuverlässig auflösbaren Registry-Punkt vorfindet.
        services.AddSingleton<IReadOnlyList<FlirtyWebhookRegistration>>(options.Webhooks.AsReadOnly());

        if (options.MigrationsEnabled)
        {
            services.AddHostedService<FlirtyMigrationHostedService>();
        }

        return services;
    }
}
