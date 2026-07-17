using Flirty.Expressions;
using Flirty.Hosting;
using Flirty.Persistence;
using Flirty.Pipeline;
using Flirty.Runtime;
using Flirty.Validation;
using Mediator;

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
    /// baut hierauf auf und aktiviert bei <c>o.ApplyMigrations()</c> die Auto-Migration. Issue #34 erweitert
    /// <see cref="FlirtyOptions"/> additiv um Provider-Wahl (inkl. <see cref="FlirtyDbContext"/>-Registrierung),
    /// Webhooks und austauschbaren Expression-Evaluator.
    /// Offen-generische Pipeline-Behaviors werden bei Mediator bewusst manuell registriert.
    /// Seit Issue #21 wird zusätzlich <see cref="IDialogStore"/> (Implementierung
    /// <see cref="DialogStore"/>) als <see cref="ServiceLifetime.Scoped"/> registriert – dieselbe
    /// Lebensdauer wie der <see cref="FlirtyDbContext"/>, den der Store voraussetzt. Die Registrierung
    /// selbst ist inert; aufgelöst werden kann <see cref="IDialogStore"/> erst, sobald ein
    /// <see cref="FlirtyDbContext"/> (Provider + <c>MigrationsAssembly</c>) registriert ist – komfortabel
    /// via <c>o.UseSqlite/UsePostgreSql/UseSqlServer</c> ab #34, bis dahin per <c>AddDbContext</c>.
    /// Seit Issue #25 wird zusätzlich die Runtime-Facade <see cref="IFlirtyEngine"/> (Implementierung
    /// <see cref="FlirtyEngine"/>) als <see cref="ServiceLifetime.Scoped"/> registriert – dieselbe
    /// Lebensdauer wie Mediator und <see cref="IDialogStore"/>, die sie mittelbar nutzt.
    /// Seit Issue #26 wird der <see cref="IExpressionEvaluator"/> (Default
    /// <see cref="DynamicExpressoExpressionEvaluator"/>) als <see cref="ServiceLifetime.Singleton"/>
    /// registriert – die Engine ist zustandslos und wird vom <see cref="SubmitAnswerCommandHandler"/>
    /// zur Auswertung der Übergänge (Branching) benötigt. Der austauschbare Overload
    /// <c>o.UseExpressionEvaluator&lt;T&gt;()</c> folgt in #34.
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
    /// In Issue #20 steuert die einzige Option <see cref="FlirtyOptions.ApplyMigrations"/>, ob der
    /// <see cref="FlirtyMigrationHostedService"/> registriert wird (Auto-Migration beim Host-Start).
    /// Voraussetzung dafür ist ein bereits registrierter <see cref="FlirtyDbContext"/> (Provider +
    /// <c>MigrationsAssembly</c>); die komfortable Provider-Wahl <c>o.UseSqlite/UsePostgreSql/UseSqlServer</c>
    /// folgt in #34.
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

        if (options.MigrationsEnabled)
        {
            services.AddHostedService<FlirtyMigrationHostedService>();
        }

        return services;
    }
}
