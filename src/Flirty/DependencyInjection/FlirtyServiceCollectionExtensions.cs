using Flirty.Pipeline;
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
    /// Issue #34 erweitert diese Registrierung zum vollständigen <c>AddFlirty(o =&gt; …)</c>
    /// (Provider-Wahl, Auto-Migration, Webhooks, austauschbarer Condition-Evaluator).
    /// Offen-generische Pipeline-Behaviors werden bei Mediator bewusst manuell registriert.
    /// </remarks>
    /// <param name="services">Die zu erweiternde Service-Collection.</param>
    /// <returns>Dieselbe <see cref="IServiceCollection"/>, um Aufrufe verketten zu können.</returns>
    public static IServiceCollection AddFlirty(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);

        services.AddSingleton(typeof(IPipelineBehavior<,>), typeof(LoggingPipelineBehavior<,>));
        services.AddSingleton(typeof(IPipelineBehavior<,>), typeof(ValidationPipelineBehavior<,>));

        return services;
    }
}
