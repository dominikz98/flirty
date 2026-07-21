using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Flirty.Samples.Web;

/// <summary>
/// Baut den Demo-Dialog beim Start der App auf. Implementiert <see cref="IHostedLifecycleService"/> und
/// nutzt <see cref="StartedAsync"/> – dieser Punkt liegt <em>nach</em> dem Start aller Hosted Services
/// (inkl. Kestrel und der Auto-Migration), sodass die App ihre eigenen Admin-Endpunkte bereits über HTTP
/// erreichen kann und das Schema existiert. Weil <see cref="StartedAsync"/> vom Host abgewartet wird, ist
/// der Dialog nach dem Start deterministisch vorhanden (wichtig für die E2E-Tests).
/// </summary>
public sealed class DemoProvisioningHostedService : IHostedLifecycleService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IServiceProvider _services;
    private readonly ILogger<DemoProvisioningHostedService> _logger;

    /// <summary>Initialisiert den Dienst mit den für das Provisioning benötigten Abhängigkeiten.</summary>
    /// <param name="httpClientFactory">Fabrik für den auf diese App zeigenden Admin-Client.</param>
    /// <param name="services">Service-Provider für den <see cref="Flirty.Persistence.FlirtyDbContext"/>-Scope.</param>
    /// <param name="logger">Logger für das Provisioning-Ergebnis.</param>
    public DemoProvisioningHostedService(
        IHttpClientFactory httpClientFactory, IServiceProvider services, ILogger<DemoProvisioningHostedService> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(logger);
        _httpClientFactory = httpClientFactory;
        _services = services;
        _logger = logger;
    }

    /// <summary>Führt das Provisioning aus, nachdem der Host (inkl. Kestrel) gestartet ist.</summary>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns>Ein Task, der den Abschluss des Provisionings darstellt.</returns>
    public async Task StartedAsync(CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(WebSampleApp.AdminHttpClientName);
        await DemoDialogProvisioner.EnsureProvisionedAsync(client, _services, _logger, cancellationToken);
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
